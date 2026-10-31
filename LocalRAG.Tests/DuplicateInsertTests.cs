using LocalRAG;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>Two callers adding the same RequestID at once: one row, no exception for the loser. Gated on the model.</summary>
[Trait("Category", "Integration")]
public class DuplicateInsertTests
{
    [SkippableFact]
    public async Task ConcurrentInsertsOfOneRequestId_LeaveOneRowAndThrowNothing()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"dup_{Guid.NewGuid():N}.db");
        var db = new EmbeddingDatabaseNew(new RAGConfiguration { ModelPath = modelPath, VocabularyPath = vocabPath, DatabasePath = dbPath, IndexChunkEmbeddings = false });
        try
        {
            await db.EnsureReadyAsync();
            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
                db.AddRequestToEmbeddingDatabaseAsync("same", "Kubernetes orchestrates containers.", embed: true)));

            Assert.Equal(1, (await db.GetStatsAsync()).TotalRecords);
        }
        finally
        {
            await db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); break; }
                catch (IOException) { await Task.Delay(100); }
            }
        }
    }
}
