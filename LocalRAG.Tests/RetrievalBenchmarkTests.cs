using System.Text;
using LocalRAG;
using LocalRAG.Benchmarks;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// LLM-free retrieval-quality benchmark entry points. These exercise the real
/// SearchEmbeddingsAsync surface, so they require the BERT model (gated on
/// BERT_MODEL_PATH, exactly like the existing integration tests) and skip
/// cleanly when it is absent.
/// </summary>
public class RetrievalBenchmarkTests
{
    private const string V2Resource = "LocalRAG.BenchmarkData.eedag_benchmark_v2.json";

    private readonly ITestOutputHelper _output;

    public RetrievalBenchmarkTests(ITestOutputHelper output) => _output = output;

    // ── Report-only: run, print table, write CSV + JSON ──────────────────────

    [SkippableFact]
    [Trait("Category", "RetrievalBenchmark")]
    public async Task Retrieval_ReportOnly()
    {
        var (config, ok) = BuildConfig();
        Skip.IfNot(ok, "BERT model not configured. Set BERT_MODEL_PATH (and optionally BERT_VOCAB_PATH).");

        var label = Environment.GetEnvironmentVariable("RETRIEVAL_LABEL") ?? "adhoc";
        var dataset = LoadDataset();
        _output.WriteLine($"Dataset '{dataset.Name}': {dataset.KnowledgeEntries.Count} entries, {dataset.Cases.Count} cases.");

        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await Task.Delay(500);

            var slugToId = await EERagBenchmark.SeedDatabaseAsync(dataset, db, generateEmbeddings: true);

            var opts = new RetrievalBenchmarkOptions
            {
                Label = label,
                ConfigSummary = RetrievalBenchmark.SummarizeConfig(config)
            };

            var report = await RetrievalBenchmark.RunAsync(dataset, slugToId, db, opts);

            _output.WriteLine(report.FormatSummary());
            WriteResults(report);

            // Sanity: the benchmark actually scored positive cases.
            Assert.True(report.PositiveCases > 0, "No positive cases were scored.");
        }
        finally
        {
            if (db != null) await db.DisposeAsync();
            CleanupDb(config.DatabasePath);
        }
    }

    // ── BEIR SciFact: external standard-benchmark baseline ───────────────────

    [SkippableFact]
    [Trait("Category", "BeirBenchmark")]
    public async Task Beir_SciFact_Baseline()
    {
        var (config, ok) = BuildConfig();
        Skip.IfNot(ok, "BERT model not configured. Set BERT_MODEL_PATH.");

        var dir = Environment.GetEnvironmentVariable("BEIR_SCIFACT_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir),
            "Set BEIR_SCIFACT_DIR to the extracted SciFact folder (containing corpus.jsonl).");

        int? maxQueries = TryEnvInt("BEIR_MAX_QUERIES");
        int? maxCorpus  = TryEnvInt("BEIR_MAX_CORPUS");

        var dataset = BeirDatasetLoader.Load(dir!, "BEIR-SciFact", "test", maxQueries, maxCorpus);
        _output.WriteLine($"SciFact: {dataset.KnowledgeEntries.Count} docs, {dataset.Cases.Count} queries.");

        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await Task.Delay(500);

            var seedSw = System.Diagnostics.Stopwatch.StartNew();
            var slugToId = await EERagBenchmark.SeedDatabaseAsync(
                dataset, db, generateEmbeddings: true,
                new Progress<string>(m => { if (m.Contains("/100]") || m.StartsWith("Seeding complete")) _output.WriteLine(m); }));
            seedSw.Stop();
            _output.WriteLine($"Seeded {slugToId.Count} docs in {seedSw.Elapsed.TotalSeconds:F1}s.");

            // BEIR docs are single passages → SearchLevel 1; BEIR headline metric is
            // nDCG@10, Recall@100, so request depth 100.
            var opts = new RetrievalBenchmarkOptions
            {
                Label         = Environment.GetEnvironmentVariable("RETRIEVAL_LABEL") ?? "scifact",
                SearchLevel   = 1,
                TopK          = 100,
                Ks            = new[] { 1, 5, 10, 100 },
                ConfigSummary = RetrievalBenchmark.SummarizeConfig(config)
            };

            var report = await RetrievalBenchmark.RunAsync(dataset, slugToId, db, opts);
            _output.WriteLine(report.FormatSummary());
            WriteResults(report);

            Assert.True(report.PositiveCases > 0, "No positive cases were scored.");
        }
        finally
        {
            if (db != null) await db.DisposeAsync();
            CleanupDb(config.DatabasePath);
        }
    }

    private static int? TryEnvInt(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : null;

    // ── Regression gate: opt-in, self-skips until a baseline is committed ─────

    [SkippableFact]
    [Trait("Category", "RetrievalGate")]
    public async Task Retrieval_Gate_RecallAt5_MeetsBaseline()
    {
        var (config, ok) = BuildConfig();
        Skip.IfNot(ok, "BERT model not configured. Set BERT_MODEL_PATH.");

        var baseline = LoadBaseline();
        Skip.If(baseline is null,
            "No retrieval_baseline.json found. Record a baseline (see Retrieval_ReportOnly output) to enable this gate.");

        var dataset = LoadDataset();

        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await Task.Delay(500);

            var slugToId = await EERagBenchmark.SeedDatabaseAsync(dataset, db, generateEmbeddings: true);
            var report = await RetrievalBenchmark.RunAsync(
                dataset, slugToId, db, new RetrievalBenchmarkOptions { Label = "gate" });

            var actual = report.MeanRecallAt(baseline!.K);
            _output.WriteLine($"Gate: MeanRecall@{baseline.K} actual={actual:F4} baseline={baseline.MinRecall:F4} tol={baseline.Tolerance:F4}");

            Assert.True(
                actual >= baseline.MinRecall - baseline.Tolerance,
                $"Retrieval regression: MeanRecall@{baseline.K} {actual:F4} fell below baseline {baseline.MinRecall:F4} (tolerance {baseline.Tolerance:F4}).");
        }
        finally
        {
            if (db != null) await db.DisposeAsync();
            CleanupDb(config.DatabasePath);
        }
    }

    // ── On-demand dataset generation (offline, no model required) ─────────────

    [SkippableFact]
    [Trait("Category", "GenerateDataset")]
    public void Generate_DeterministicDataset_V2()
    {
        Skip.IfNot(
            Environment.GetEnvironmentVariable("GENERATE_DATASET") == "1",
            "Set GENERATE_DATASET=1 to (re)generate eedag_benchmark_v2.json.");

        var source = EERagBenchmark.LoadEmbeddedDataset(); // v1
        var generated = RetrievalCaseGenerator.GenerateDeterministic(source);

        var outPath = Path.Combine(FindRepoRoot(), "LocalRAG", "BenchmarkData", "eedag_benchmark_v2.json");
        RetrievalCaseGenerator.SaveDataset(generated, outPath);

        _output.WriteLine($"Wrote {generated.Cases.Count} cases ({generated.KnowledgeEntries.Count} entries) to {outPath}");
        Assert.True(generated.Cases.Count >= source.Cases.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Prefers the larger generated v2 dataset, falls back to embedded v1.</summary>
    private static BenchmarkDataset LoadDataset()
    {
        try { return EERagBenchmark.LoadEmbeddedDataset(V2Resource); }
        catch (InvalidOperationException) { return EERagBenchmark.LoadEmbeddedDataset(); }
    }

    private static (RAGConfiguration config, bool ok) BuildConfig()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;

        var config = new RAGConfiguration
        {
            ModelPath      = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath   = Path.Combine(Path.GetTempPath(), $"retr_bench_{Guid.NewGuid():N}.db")
        };

        // Optional: disable per-chunk embedding/indexing (INDEX_CHUNKS=0) to seed ~10x
        // faster, indexing only the single full-text embedding.
        if (Environment.GetEnvironmentVariable("INDEX_CHUNKS") == "0")
            config.IndexChunkEmbeddings = false;

        // Optional: exact brute-force vector search instead of LSH (USE_EXACT=1).
        if (Environment.GetEnvironmentVariable("USE_EXACT") == "1")
            config.UseExactVectorSearch = true;

        return (config, File.Exists(modelPath));
    }

    private void WriteResults(RetrievalReport report)
    {
        var dir = Path.Combine(FindRepoRoot(), "BenchmarkResults");
        Directory.CreateDirectory(dir);
        var stamp = report.RunAt.ToString("yyyyMMdd-HHmmss");

        // Append to a single rolling CSV so runs are comparable across commits.
        var csvPath = Path.Combine(dir, "retrieval.csv");
        var sb = new StringBuilder();
        if (!File.Exists(csvPath)) sb.AppendLine(RetrievalReport.CsvHeader());
        foreach (var row in report.ToCsvRows()) sb.AppendLine(row);
        File.AppendAllText(csvPath, sb.ToString());

        // Full per-case detail for drill-down.
        var jsonPath = Path.Combine(dir, $"retrieval-{report.Label}-{stamp}.json");
        File.WriteAllText(jsonPath, JsonConvert.SerializeObject(report, Formatting.Indented));

        _output.WriteLine($"CSV  -> {csvPath}");
        _output.WriteLine($"JSON -> {jsonPath}");
    }

    private static void CleanupDb(string path)
    {
        for (int i = 0; i < 3; i++)
        {
            try { if (File.Exists(path)) File.Delete(path); return; }
            catch (IOException) { Thread.Sleep(100); }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0 || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static RetrievalBaseline? LoadBaseline()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "retrieval_baseline.json"),
                     Path.Combine(FindRepoRoot(), "LocalRAG.Tests", "retrieval_baseline.json")
                 })
        {
            if (File.Exists(candidate))
                return JsonConvert.DeserializeObject<RetrievalBaseline>(File.ReadAllText(candidate));
        }
        return null;
    }

    private sealed class RetrievalBaseline
    {
        public int K { get; set; } = 5;
        public double MinRecall { get; set; }
        public double Tolerance { get; set; } = 0.02;
    }
}
