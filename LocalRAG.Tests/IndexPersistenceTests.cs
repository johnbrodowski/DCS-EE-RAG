using LocalRAG;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Proves the in-memory search indexes are rebuilt from the persisted SQLite DB on
/// startup: seed a DB, dispose it, reopen a brand-new instance on the same file, and
/// confirm vector search works without re-embedding anything. Gated on the model.
/// </summary>
[Trait("Category", "Integration")]
public class IndexPersistenceTests
{
    private readonly ITestOutputHelper _output;
    public IndexPersistenceTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task ExactIndex_SurvivesRestart()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"persist_{Guid.NewGuid():N}.db");
        RAGConfiguration Config() => new()
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = dbPath,
            IndexChunkEmbeddings = false,
            UseExactVectorSearch = true
        };

        try
        {
            // ── Session 1: seed and dispose ──
            var db1 = new EmbeddingDatabaseNew(Config());
            await db1.EnsureReadyAsync();
            await db1.AddRequestToEmbeddingDatabaseAsync("kube", "Kubernetes orchestrates containers across clusters.", embed: true);
            await db1.AddRequestToEmbeddingDatabaseAsync("pg", "PostgreSQL is a relational database with indexing.", embed: true);
            await db1.AddRequestToEmbeddingDatabaseAsync("git", "Git tracks source code changes with commits.", embed: true);
            _output.WriteLine($"Session 1 indexed {db1.GetExactIndexCount()} docs.");
            await db1.DisposeAsync();
            await Task.Delay(200);

            // ── Session 2: reopen the SAME file, no re-embedding ──
            var db2 = new EmbeddingDatabaseNew(Config());
            await db2.EnsureReadyAsync();
            int warmed = db2.GetExactIndexCount();
            _output.WriteLine($"Session 2 warmed {warmed} docs from disk (no re-embedding).");

            var results = await db2.SearchEmbeddingsAsync("container orchestration", topK: 3, minimumSimilarity: 0.0f, searchLevel: 1);
            _output.WriteLine($"Search after restart: {string.Join(", ", results.Select(r => r.RequestID))}");

            Assert.Equal(3, warmed); // all docs reloaded from disk
            Assert.Contains(results, r => r.RequestID == "kube");
            await db2.DisposeAsync();
        }
        finally
        {
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); break; }
                catch (IOException) { await Task.Delay(100); }
            }
        }
    }
}
