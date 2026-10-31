using LocalRAG;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// Disposing the database must release callers still waiting on a queued update, refuse new ones, and work with
/// "await using". Gated on the model.
/// </summary>
[Trait("Category", "Integration")]
public class DisposeTests
{
    private static (RAGConfiguration Config, string DbPath)? Setup()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        if (!File.Exists(modelPath)) return null;
        var dbPath = Path.Combine(Path.GetTempPath(), $"dispose_{Guid.NewGuid():N}.db");
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

    [SkippableFact]
    public async Task Dispose_ReleasesCallersWaitingOnQueuedUpdates()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        try
        {
            var db = new EmbeddingDatabaseNew(setup!.Value.Config);
            await db.EnsureReadyAsync();
            await db.AddRequestToEmbeddingDatabaseAsync("row", "Kubernetes orchestrates containers across clusters.", embed: false);

            var pending = Enumerable.Range(0, 30).Select(i => db.UpdateTextResponse("row", $"reply {i}", embed: true)).ToList();
            await db.DisposeAsync();

            var all = Task.WhenAll(pending.Select(async t => { try { await t; } catch (OperationCanceledException) { } }));
            var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(60)));
            Assert.Same(all, finished); // no caller is left waiting forever
        }
        finally { await CleanUp(setup!.Value.DbPath); }
    }

    [SkippableFact]
    public async Task UpdateAfterDispose_Throws()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        try
        {
            var db = new EmbeddingDatabaseNew(setup!.Value.Config);
            await db.EnsureReadyAsync();
            await db.AddRequestToEmbeddingDatabaseAsync("row", "Some text.", embed: false);
            await db.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => db.UpdateTextResponse("row", "late", embed: false));
        }
        finally { await CleanUp(setup!.Value.DbPath); }
    }

    [SkippableFact]
    public async Task AwaitUsing_DisposesTheDatabase()
    {
        var setup = Setup();
        Skip.If(setup == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        try
        {
            EmbeddingDatabaseNew captured;
            await using (var db = new EmbeddingDatabaseNew(setup!.Value.Config))
            {
                captured = db;
                await db.EnsureReadyAsync();
                await db.AddRequestToEmbeddingDatabaseAsync("row", "Some text.", embed: false);
            }

            await Assert.ThrowsAsync<ObjectDisposedException>(() => captured.UpdateTextResponse("row", "late", embed: false));
        }
        finally { await CleanUp(setup!.Value.DbPath); }
    }
}
