using LocalRAG;
using LocalRAG.Benchmarks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// One failing case (a rate limit, a network error) must not throw away the cases already run, must not count in any
/// mean, and a run that keeps failing must stop and say so (G4-13). Gated on the model.
/// </summary>
[Trait("Category", "Integration")]
public class EERagBenchmarkErrorTests
{
    private static BenchmarkDataset Dataset() => new()
    {
        Name = "tiny",
        KnowledgeEntries =
        {
            new KnowledgeEntry
            {
                Slug = "kube",
                Request = "Kubernetes orchestrates containers across clusters.",
                TextResponse = "Kubernetes schedules containers onto nodes.",
                Summary = "Kubernetes container orchestration"
            }
        },
        Cases =
        {
            new BenchmarkCase { Id = "c1", Query = "container orchestration", ExpectedSlugs = { "kube" }, ExpectRetrieval = true },
            new BenchmarkCase { Id = "c2", Query = "FAILME container orchestration", ExpectedSlugs = { "kube" }, ExpectRetrieval = true },
            new BenchmarkCase { Id = "c3", Query = "FAILME again", ExpectedSlugs = { }, ExpectRetrieval = false },
            new BenchmarkCase { Id = "c4", Query = "container orchestration", ExpectedSlugs = { "kube" }, ExpectRetrieval = true },
            new BenchmarkCase { Id = "c5", Query = "what colour is the sky", ExpectedSlugs = { }, ExpectRetrieval = false },
        }
    };

    private static async Task<(EmbeddingDatabaseNew Db, Dictionary<string, int> Slugs, string DbPath)?> OpenAsync(BenchmarkDataset dataset)
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        if (!File.Exists(modelPath)) return null;
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench_err_{Guid.NewGuid():N}.db");
        var db = new EmbeddingDatabaseNew(new RAGConfiguration
        {
            ModelPath = modelPath, VocabularyPath = vocabPath, DatabasePath = dbPath, IndexChunkEmbeddings = false
        });
        await db.EnsureReadyAsync();
        var slugs = await EERagBenchmark.SeedDatabaseAsync(dataset, db, generateEmbeddings: true);
        return (db, slugs, dbPath);
    }

    private static async Task CleanUp(EmbeddingDatabaseNew db, string dbPath)
    {
        await db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        for (int i = 0; i < 3; i++)
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); break; }
            catch (IOException) { await Task.Delay(100); }
        }
    }

    [SkippableFact]
    public async Task AFailingCase_IsRecordedAndLeftOutOfTheMeans_WhileTheRestStillRun()
    {
        var dataset = Dataset();
        var opened = await OpenAsync(dataset);
        Skip.If(opened == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var (db, slugs, dbPath) = opened!.Value;
        try
        {
            var report = await EERagBenchmark.RunAsync(dataset, slugs,
                prompt => prompt.Contains("FAILME") ? throw new IOException("connection reset") : Task.FromResult("I will answer directly."),
                db, new BenchmarkOptions { MaxConsecutiveErrors = 5 });

            Assert.Equal(5, report.TotalCases);
            Assert.Equal(2, report.ErroredCases);
            Assert.Null(report.AbortReason);
            Assert.Equal(3, report.CaseResults.Count(r => r.Error == null));
            Assert.Equal(2, report.PositiveCases);   // c1 and c4 (c2 failed)
            Assert.Equal(1, report.NegativeCases);   // c5 (c3 failed)
            Assert.Equal(1.0, report.NegativeCorrectRate, 6);
            Assert.Contains("ERRORS: 2", report.FormatSummary());
        }
        finally { await CleanUp(db, dbPath); }
    }

    [SkippableFact]
    public async Task RepeatedFailures_StopTheRunAndSayWhy()
    {
        var dataset = Dataset();
        var opened = await OpenAsync(dataset);
        Skip.If(opened == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var (db, slugs, dbPath) = opened!.Value;
        try
        {
            var report = await EERagBenchmark.RunAsync(dataset, slugs,
                _ => throw new InvalidOperationException("401 invalid api key"),
                db, new BenchmarkOptions { MaxConsecutiveErrors = 2 });

            Assert.Equal(2, report.TotalCases);
            Assert.Equal(2, report.ErroredCases);
            Assert.NotNull(report.AbortReason);
            Assert.Contains("RUN STOPPED EARLY", report.FormatSummary());
        }
        finally { await CleanUp(db, dbPath); }
    }
}
