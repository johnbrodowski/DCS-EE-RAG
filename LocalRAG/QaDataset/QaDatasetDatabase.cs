using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace LocalRAG.QaDataset
{
    /// <summary>
    /// Manages a separate SQLite database for QA dataset items and benchmark run results.
    /// Uses a different file from the main EE-RAG embeddings database.
    /// </summary>
    public class QaDatasetDatabase : IDisposable
    {
        private readonly string _dbPath;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed;

        public string DbPath => _dbPath;

        public QaDatasetDatabase(string dbPath)
        {
            _dbPath = dbPath;
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        public async Task InitializeAsync()
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync();
            await CreateTablesAsync(conn);
        }

        private SqliteConnection OpenConnection()
            => new SqliteConnection($"Data Source={_dbPath}");

        private static async Task CreateTablesAsync(SqliteConnection conn)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS qa_items (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Question TEXT NOT NULL,
                    Answers TEXT NOT NULL DEFAULT '[]',
                    DefCorrectPredictions TEXT NOT NULL DEFAULT '[]',
                    PossCorrectPredictions TEXT NOT NULL DEFAULT '[]',
                    DefIncorrectPredictions TEXT NOT NULL DEFAULT '[]',
                    AnswerAndDefCorrectPredictions TEXT NOT NULL DEFAULT '[]',
                    QuestionEmbedding TEXT,
                    IsEmbedded INTEGER NOT NULL DEFAULT 0,
                    DatasetSource TEXT,
                    ImportedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_qa_items_embedded ON qa_items(IsEmbedded);

                CREATE TABLE IF NOT EXISTS qa_run_results (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RunId TEXT NOT NULL,
                    QaItemId INTEGER NOT NULL,
                    Question TEXT NOT NULL,
                    ModelResponse TEXT,
                    Outcome TEXT NOT NULL DEFAULT 'Indeterminate',
                    MatchedAnswer TEXT,
                    RunAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_qa_results_runid ON qa_run_results(RunId);
            ";
            await using var cmd = new SqliteCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── Import ────────────────────────────────────────────────────────────

        public async Task<int> ImportItemAsync(QaDatasetItem item)
        {
            await _writeLock.WaitAsync();
            try
            {
                await using var conn = OpenConnection();
                await conn.OpenAsync();

                const string sql = @"
                    INSERT INTO qa_items
                        (Question, Answers, DefCorrectPredictions, PossCorrectPredictions,
                         DefIncorrectPredictions, AnswerAndDefCorrectPredictions, DatasetSource)
                    VALUES (@q, @ans, @def, @poss, @defInc, @andDef, @src);
                    SELECT last_insert_rowid();";

                await using var cmd = new SqliteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@q", item.Question);
                cmd.Parameters.AddWithValue("@ans", JsonConvert.SerializeObject(item.Answers));
                cmd.Parameters.AddWithValue("@def", JsonConvert.SerializeObject(item.DefCorrectPredictions));
                cmd.Parameters.AddWithValue("@poss", JsonConvert.SerializeObject(item.PossCorrectPredictions));
                cmd.Parameters.AddWithValue("@defInc", JsonConvert.SerializeObject(item.DefIncorrectPredictions));
                cmd.Parameters.AddWithValue("@andDef", JsonConvert.SerializeObject(item.AnswerAndDefCorrectPredictions));
                cmd.Parameters.AddWithValue("@src", (object?)item.DatasetSource ?? DBNull.Value);

                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // ── Counts ────────────────────────────────────────────────────────────

        public async Task<(int Total, int Embedded)> GetItemCountAsync()
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync();

            await using var cmd = new SqliteCommand(
                "SELECT COUNT(*), COALESCE(SUM(CASE WHEN IsEmbedded=1 THEN 1 ELSE 0 END),0) FROM qa_items",
                conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.GetInt32(0), reader.GetInt32(1));
            return (0, 0);
        }

        // ── Backfill ──────────────────────────────────────────────────────────

        /// <summary>
        /// Clears all embeddings so they can be regenerated with new parameters.
        /// </summary>
        public async Task ResetAllEmbeddingsAsync()
        {
            await _writeLock.WaitAsync();
            try
            {
                await using var conn = OpenConnection();
                await conn.OpenAsync();

                await using var cmd = new SqliteCommand(
                    "UPDATE qa_items SET QuestionEmbedding = NULL, IsEmbedded = 0",
                    conn);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<List<QaDatasetItem>> GetUnembeddedItemsAsync(int batchSize = 50)
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync();

            await using var cmd = new SqliteCommand(
                "SELECT Id, Question FROM qa_items WHERE IsEmbedded = 0 LIMIT @limit",
                conn);
            cmd.Parameters.AddWithValue("@limit", batchSize);

            var items = new List<QaDatasetItem>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(new QaDatasetItem { Id = reader.GetInt32(0), Question = reader.GetString(1) });
            return items;
        }

        public async Task UpdateEmbeddingAsync(int id, float[] embedding)
        {
            await _writeLock.WaitAsync();
            try
            {
                await using var conn = OpenConnection();
                await conn.OpenAsync();

                await using var cmd = new SqliteCommand(
                    "UPDATE qa_items SET QuestionEmbedding = @emb, IsEmbedded = 1 WHERE Id = @id",
                    conn);
                cmd.Parameters.AddWithValue("@emb", JsonConvert.SerializeObject(embedding));
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // ── Benchmark run ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns up to <paramref name="count"/> items in random order.
        /// When <paramref name="onlyEmbedded"/> is true only items with embeddings are returned.
        /// </summary>
        public async Task<List<QaDatasetItem>> GetItemsForRunAsync(int count, bool onlyEmbedded = true)
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync();

            var where = onlyEmbedded ? "WHERE IsEmbedded = 1" : "";
            var sql = $@"
                SELECT Id, Question, Answers, DefCorrectPredictions, PossCorrectPredictions,
                       DefIncorrectPredictions, AnswerAndDefCorrectPredictions, QuestionEmbedding, IsEmbedded
                FROM qa_items {where} ORDER BY RANDOM() LIMIT @count";

            await using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@count", count);

            var items = new List<QaDatasetItem>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new QaDatasetItem
                {
                    Id = reader.GetInt32(0),
                    Question = reader.GetString(1),
                    Answers = JsonConvert.DeserializeObject<List<string>>(reader.GetString(2)) ?? new(),
                    DefCorrectPredictions = JsonConvert.DeserializeObject<List<string>>(reader.GetString(3)) ?? new(),
                    PossCorrectPredictions = JsonConvert.DeserializeObject<List<string>>(reader.GetString(4)) ?? new(),
                    DefIncorrectPredictions = JsonConvert.DeserializeObject<List<string>>(reader.GetString(5)) ?? new(),
                    AnswerAndDefCorrectPredictions = JsonConvert.DeserializeObject<List<string>>(reader.GetString(6)) ?? new(),
                    IsEmbedded = reader.GetInt32(8) == 1
                };
                if (!reader.IsDBNull(7))
                    item.QuestionEmbedding = JsonConvert.DeserializeObject<float[]>(reader.GetString(7));
                items.Add(item);
            }
            return items;
        }

        /// <summary>Loads all (Id, Embedding) pairs for similarity search during a run.</summary>
        public async Task<List<(int Id, float[] Embedding)>> GetAllEmbeddingsAsync()
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync();

            await using var cmd = new SqliteCommand(
                "SELECT Id, QuestionEmbedding FROM qa_items WHERE IsEmbedded = 1 AND QuestionEmbedding IS NOT NULL",
                conn);

            var result = new List<(int Id, float[] Embedding)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var emb = JsonConvert.DeserializeObject<float[]>(reader.GetString(1));
                if (emb != null)
                    result.Add((reader.GetInt32(0), emb));
            }
            return result;
        }

        // ── Results ───────────────────────────────────────────────────────────

        public async Task SaveRunResultAsync(QaRunResult result)
        {
            await _writeLock.WaitAsync();
            try
            {
                await using var conn = OpenConnection();
                await conn.OpenAsync();

                const string sql = @"
                    INSERT INTO qa_run_results (RunId, QaItemId, Question, ModelResponse, Outcome, MatchedAnswer)
                    VALUES (@runId, @itemId, @q, @resp, @outcome, @matched)";

                await using var cmd = new SqliteCommand(sql, conn);
                cmd.Parameters.AddWithValue("@runId", result.RunId);
                cmd.Parameters.AddWithValue("@itemId", result.QaItemId);
                cmd.Parameters.AddWithValue("@q", result.Question);
                cmd.Parameters.AddWithValue("@resp", (object?)result.ModelResponse ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@outcome", result.Outcome.ToString());
                cmd.Parameters.AddWithValue("@matched", (object?)result.MatchedAnswer ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<List<QaRunResult>> GetRunResultsAsync(string runId)
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync();

            await using var cmd = new SqliteCommand(
                @"SELECT Id, RunId, QaItemId, Question, ModelResponse, Outcome, MatchedAnswer, RunAt
                  FROM qa_run_results WHERE RunId = @runId ORDER BY Id",
                conn);
            cmd.Parameters.AddWithValue("@runId", runId);

            var results = new List<QaRunResult>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new QaRunResult
                {
                    Id = reader.GetInt32(0),
                    RunId = reader.GetString(1),
                    QaItemId = reader.GetInt32(2),
                    Question = reader.GetString(3),
                    ModelResponse = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Outcome = Enum.Parse<QaOutcome>(reader.GetString(5)),
                    MatchedAnswer = reader.IsDBNull(6) ? null : reader.GetString(6),
                    RunAt = reader.GetDateTime(7)
                });
            }
            return results;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _writeLock.Dispose();
                _disposed = true;
            }
        }
    }
}
