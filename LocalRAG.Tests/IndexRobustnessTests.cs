using LocalRAG;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// The in-memory indexes must stay in step with the database: a clear empties them, a backfill fills them,
/// and one corrupt stored embedding must not take the whole warm-up down. Gated on the model.
/// </summary>
[Trait("Category", "Integration")]
public class IndexRobustnessTests
{
    private static (RAGConfiguration Config, string DbPath)? Setup()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        if (!File.Exists(modelPath)) return null;
        var dbPath = Path.Combine(Path.GetTempPath(), $"robust_{Guid.NewGuid():N}.db");
        return (new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = dbPath,
            IndexChunkEmbeddings = false,
            UseExactVectorSearch = true
        }, dbPath);
    }

    private static async Task CleanUp(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        for (int i = 0; i < 3; i++)
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); break; }
            catch (IOException) { await Task.Delay(100); }
        }
    }

    [SkippableFact]
    public async Task ClearAll_EmptiesTheExactIndex()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        try
        {
            var db = new EmbeddingDatabaseNew(setup!.Value.Config);
            await db.EnsureReadyAsync();
            await db.AddRequestToEmbeddingDatabaseAsync("a", "Kubernetes orchestrates containers across clusters.", embed: true);
            await db.AddRequestToEmbeddingDatabaseAsync("b", "PostgreSQL is a relational database with indexing.", embed: true);
            Assert.Equal(2, db.GetExactIndexCount());

            await db.ClearAllDataAsync();

            Assert.Equal(0, db.GetExactIndexCount());
            Assert.Empty(await db.SearchEmbeddingsAsync("containers", topK: 5, minimumSimilarity: 0.0f, searchLevel: 1));
            await db.DisposeAsync();
        }
        finally { await CleanUp(setup!.Value.DbPath); }
    }

    [SkippableFact]
    public async Task Backfill_MakesAnUnembeddedRowVisibleToExactSearch()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        try
        {
            var db = new EmbeddingDatabaseNew(setup!.Value.Config);
            await db.EnsureReadyAsync();
            await db.AddRequestToEmbeddingDatabaseAsync("late", "Kubernetes orchestrates containers across clusters.", embed: false);
            Assert.Equal(0, db.GetExactIndexCount());

            var result = await db.BackfillEmbeddingsAsync();

            Assert.True(result.SuccessCount >= 1, result.ToString());
            Assert.Equal(1, db.GetExactIndexCount());
            var hits = await db.SearchEmbeddingsAsync("container orchestration", topK: 3, minimumSimilarity: 0.0f, searchLevel: 1);
            Assert.Contains(hits, r => r.RequestID == "late");
            await db.DisposeAsync();
        }
        finally { await CleanUp(setup!.Value.DbPath); }
    }

    [SkippableFact]
    public async Task WarmUp_SkipsACorruptRow_AndKeepsTheRest()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var (config, dbPath) = setup!.Value;
        try
        {
            var db1 = new EmbeddingDatabaseNew(config);
            await db1.EnsureReadyAsync();
            await db1.AddRequestToEmbeddingDatabaseAsync("kube", "Kubernetes orchestrates containers across clusters.", embed: true);
            await db1.AddRequestToEmbeddingDatabaseAsync("pg", "PostgreSQL is a relational database with indexing.", embed: true);
            await db1.AddRequestToEmbeddingDatabaseAsync("git", "Git tracks source code changes with commits.", embed: true);
            await db1.DisposeAsync();
            await Task.Delay(200);
            SqliteConnection.ClearAllPools();

            using (var raw = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                raw.Open();
                using var cmd = new SqliteCommand("UPDATE embeddings SET RequestEmbedding = '[0.1, oops' WHERE RequestID = 'pg'", raw);
                Assert.Equal(1, cmd.ExecuteNonQuery());
            }

            var db2 = new EmbeddingDatabaseNew(config);
            await db2.EnsureReadyAsync(); // must not throw

            Assert.Equal(1, db2.WarmUpSkippedRecords);
            Assert.Equal(2, db2.GetExactIndexCount());
            var hits = await db2.SearchEmbeddingsAsync("container orchestration", topK: 3, minimumSimilarity: 0.0f, searchLevel: 1);
            Assert.Contains(hits, r => r.RequestID == "kube");
            await db2.DisposeAsync();
        }
        finally { await CleanUp(dbPath); }
    }

    [SkippableFact]
    public async Task ConcurrentInserts_DoNotCorruptTheLshTables()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        try
        {
            var cfg = setup!.Value.Config;
            cfg.UseExactVectorSearch = false;
            var db = new EmbeddingDatabaseNew(cfg);
            await db.EnsureReadyAsync();

            await Task.WhenAll(Enumerable.Range(0, 24).Select(i =>
                db.AddRequestToEmbeddingDatabaseAsync($"r{i}", $"Document number {i} about topic {i % 5} and subject {i % 3}.", embed: true)));

            var stats = await db.GetStatsAsync();
            Assert.Equal(24, stats.TotalRecords);
            Assert.True(stats.TotalLSHEntries >= 24 * cfg.NumberOfHashTables);
            await db.DisposeAsync();
        }
        finally { await CleanUp(setup!.Value.DbPath); }
    }
}
