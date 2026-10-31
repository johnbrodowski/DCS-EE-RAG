using LocalRAG;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// An older database gains the columns added since; a newer one is refused; and vectors made by another embedding
/// model are never mixed with the loaded model's (G4-7, G4-10). Gated on the model.
/// </summary>
[Trait("Category", "Integration")]
public class SchemaAndModelTests
{
    private static (RAGConfiguration Config, string DbPath)? Setup()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        if (!File.Exists(modelPath)) return null;
        var dbPath = Path.Combine(Path.GetTempPath(), $"schema_{Guid.NewGuid():N}.db");
        return (new RAGConfiguration { ModelPath = modelPath, VocabularyPath = vocabPath, DatabasePath = dbPath, IndexChunkEmbeddings = false }, dbPath);
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

    private static void Exec(string dbPath, string sql)
    {
        using var raw = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        raw.Open();
        using var cmd = new SqliteCommand(sql, raw);
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(string dbPath, string sql)
    {
        using var raw = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        raw.Open();
        using var cmd = new SqliteCommand(sql, raw);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [SkippableFact]
    public async Task OlderDatabase_GainsTheMissingColumns_AndKeepsItsRows()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var (config, dbPath) = setup!.Value;
        try
        {
            Exec(dbPath, "CREATE TABLE embeddings (Id INTEGER PRIMARY KEY AUTOINCREMENT, RequestID TEXT UNIQUE, Request TEXT, TextResponse TEXT);" +
                         "INSERT INTO embeddings (RequestID, Request, TextResponse) VALUES ('old', 'An older row about containers.', 'reply');");

            var db = new EmbeddingDatabaseNew(config);
            await db.EnsureReadyAsync();

            var old = await db.GetFeedbackDataByRequestIdAsync("old");
            Assert.Equal("An older row about containers.", old?.Request);
            await db.AddRequestToEmbeddingDatabaseAsync("new", "A new row about databases.", embed: true);
            Assert.NotNull((await db.GetFeedbackDataByRequestIdAsync("new"))?.RequestEmbedding);
            await db.DisposeAsync();

            Assert.Equal(1, Scalar(dbPath, "PRAGMA user_version"));
        }
        finally { await CleanUp(dbPath); }
    }

    [SkippableFact]
    public async Task DatabaseFromANewerVersion_IsRefused()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var (config, dbPath) = setup!.Value;
        try
        {
            Exec(dbPath, "CREATE TABLE embeddings (Id INTEGER PRIMARY KEY AUTOINCREMENT, RequestID TEXT UNIQUE, Request TEXT); PRAGMA user_version = 99;");

            var db = new EmbeddingDatabaseNew(config);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.EnsureReadyAsync());
            await db.DisposeAsync();
        }
        finally { await CleanUp(dbPath); }
    }

    [SkippableFact]
    public async Task VectorsFromAnotherModel_AreRefusedUntilEverythingIsReEmbedded()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var (config, dbPath) = setup!.Value;
        try
        {
            var db1 = new EmbeddingDatabaseNew(config);
            await db1.EnsureReadyAsync();
            await db1.AddRequestToEmbeddingDatabaseAsync("kube", "Kubernetes orchestrates containers across clusters.", embed: true);
            Assert.Null(db1.EmbeddingModelMismatch);
            await db1.DisposeAsync();
            await Task.Delay(200);
            SqliteConnection.ClearAllPools();

            // Pretend the stored vectors came from a different model.
            Exec(dbPath, "UPDATE rag_meta SET Value = 'some-other-model.onnx|1|2|3|4|True|False' WHERE Key = 'embedding_model'");

            var db2 = new EmbeddingDatabaseNew(config);
            await db2.EnsureReadyAsync();
            Assert.NotNull(db2.EmbeddingModelMismatch);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                db2.SearchEmbeddingsAsync("containers", topK: 3, minimumSimilarity: 0.0f, searchLevel: 1));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                db2.AddRequestToEmbeddingDatabaseAsync("pg", "PostgreSQL is a relational database.", embed: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db2.BackfillEmbeddingsAsync("missing"));

            var result = await db2.BackfillEmbeddingsAsync("all");
            Assert.Equal(0, result.FailedCount);
            Assert.Null(db2.EmbeddingModelMismatch);

            var hits = await db2.SearchEmbeddingsAsync("container orchestration", topK: 3, minimumSimilarity: 0.0f, searchLevel: 1);
            Assert.Contains(hits, r => r.RequestID == "kube");
            await db2.DisposeAsync();
            await Task.Delay(200);
            SqliteConnection.ClearAllPools();

            // The fix persisted: a fresh open sees no mismatch.
            var db3 = new EmbeddingDatabaseNew(config);
            await db3.EnsureReadyAsync();
            Assert.Null(db3.EmbeddingModelMismatch);
            await db3.DisposeAsync();
        }
        finally { await CleanUp(dbPath); }
    }
}
