using System.Diagnostics;
using LocalRAG.Benchmarks;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Tests for the EE-RAG benchmark infrastructure.
/// Non-AI unit tests always run.
/// DB integration tests require BERT model (BERT_MODEL_PATH env var).
/// AI integration tests require ANTHROPIC_API_KEY env var.
/// </summary>
public class EERagBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public EERagBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Forwards Debug.WriteLine calls to xUnit's ITestOutputHelper
    /// so all pipeline trace output appears in test results.
    /// </summary>
    private sealed class XunitTraceListener : TraceListener
    {
        private readonly ITestOutputHelper _output;
        public XunitTraceListener(ITestOutputHelper output) => _output = output;
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message != null)
            {
                try { _output.WriteLine(message); }
                catch (InvalidOperationException) { /* test already finished */ }
            }
        }
    }
    // ── Pure unit tests: EERagPipeline helpers ─────────────────────────────

    [Fact]
    public void ParseRetrieveIds_SingleId()
    {
        var ids = EERagPipeline.ParseRetrieveIds("RETRIEVE 7");
        Assert.Equal(new[] { 7 }, ids);
    }

    [Fact]
    public void ParseRetrieveIds_MultipleIds()
    {
        var ids = EERagPipeline.ParseRetrieveIds("RETRIEVE 3 7 12");
        Assert.Equal(new[] { 3, 7, 12 }, ids);
    }

    [Fact]
    public void ParseRetrieveIds_NoCommand()
    {
        var ids = EERagPipeline.ParseRetrieveIds("Here is my answer about the topic.");
        Assert.Empty(ids);
    }

    [Fact]
    public void ParseRetrieveIds_CaseInsensitive()
    {
        var ids = EERagPipeline.ParseRetrieveIds("retrieve 5");
        Assert.Equal(new[] { 5 }, ids);
    }

    [Fact]
    public void GenerateEmbeddingHash_IsDeterministic()
    {
        var embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        var hash1 = EERagPipeline.GenerateEmbeddingHash(embedding);
        var hash2 = EERagPipeline.GenerateEmbeddingHash(embedding);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GenerateEmbeddingHash_IsEightChars()
    {
        var embedding = new float[] { 1.0f, 2.0f, 3.0f };
        var hash = EERagPipeline.GenerateEmbeddingHash(embedding);
        Assert.Equal(8, hash.Length);
    }

    [Fact]
    public void BuildCandidateHeader_ContainsAllEntries()
    {
        var candidates = new List<FeedbackDatabaseValues>
        {
            new() { Id = 1, Summary = "First summary" },
            new() { Id = 2, Summary = "Second summary" },
            new() { Id = 3, Summary = "Third summary" }
        };
        var header = EERagPipeline.BuildCandidateHeader(candidates);
        Assert.Contains("[ID:1]", header);
        Assert.Contains("[ID:2]", header);
        Assert.Contains("[ID:3]", header);
    }

    // ── Pure unit tests: dataset structure ────────────────────────────────

    [Fact]
    public void LoadEmbeddedDataset_HasExpectedCounts()
    {
        var dataset = EERagBenchmark.LoadEmbeddedDataset();
        Assert.True(dataset.KnowledgeEntries.Count >= 25,
            $"Expected >=25 knowledge entries. Got {dataset.KnowledgeEntries.Count}.");
        Assert.True(dataset.Cases.Count >= 15,
            $"Expected >=15 benchmark cases. Got {dataset.Cases.Count}.");
    }

    [Fact]
    public void BenchmarkCases_AllSlugsExist()
    {
        var dataset = EERagBenchmark.LoadEmbeddedDataset();
        var slugSet = dataset.KnowledgeEntries.Select(e => e.Slug).ToHashSet();
        foreach (var benchCase in dataset.Cases)
        {
            foreach (var slug in benchCase.ExpectedSlugs)
            {
                Assert.True(slugSet.Contains(slug),
                    $"Case '{benchCase.Id}' references unknown slug '{slug}'.");
            }
        }
    }

    // ── DB integration tests (require BERT model) ─────────────────────────

    [SkippableFact]
    public async Task SeedDatabase_CreatesAllEntries()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(
            File.Exists(modelPath),
            "BERT model not configured. Set BERT_MODEL_PATH env var.");

        var tempDb = Path.Combine(Path.GetTempPath(), $"bench_seed_{Guid.NewGuid():N}.db");
        var config = new RAGConfiguration
        {
            ModelPath      = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath   = tempDb
        };

        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await Task.Delay(500);

            var dataset = EERagBenchmark.LoadEmbeddedDataset();
            var slugToId = await EERagBenchmark.SeedDatabaseAsync(
                dataset, db, generateEmbeddings: false);

            Assert.Equal(dataset.KnowledgeEntries.Count, slugToId.Count);

            var stats = await db.GetStatsAsync();
            Assert.Equal(dataset.KnowledgeEntries.Count, stats.TotalRecords);
        }
        finally
        {
            if (db != null) await db.DisposeAsync();
            await Task.Delay(200);
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); break; }
                catch (IOException) { await Task.Delay(100); }
            }
        }
    }
}
