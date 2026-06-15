using System.Diagnostics;
using LocalRAG;
using LocalRAG.Benchmarks;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Splits seeding cost into (A) pure embedding inference and (B) full seeding
/// (embedding + DB writes + memory index + LSH), to locate the real bottleneck
/// instead of guessing. Gated on the BERT model + BEIR_SCIFACT_DIR.
/// </summary>
[Trait("Category", "Profile")]
public class SeedingProfileTests
{
    private readonly ITestOutputHelper _output;
    public SeedingProfileTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Profile_EmbedOnly_Vs_FullSeed()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dir = Environment.GetEnvironmentVariable("BEIR_SCIFACT_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir), "Set BEIR_SCIFACT_DIR.");

        int n = int.TryParse(Environment.GetEnvironmentVariable("PROFILE_DOCS"), out var pd) ? pd : 200;
        var dataset = BeirDatasetLoader.Load(dir!, "BEIR-SciFact", "test", maxQueries: 40, maxCorpus: n);
        var docs = dataset.KnowledgeEntries;
        _output.WriteLine($"Profiling on {docs.Count} docs.");

        // ── Phase A: pure embedding (single + chunk-list), no DB/index-via-DB ──
        using (var embedder = new EmbedderClassNew(new RAGConfiguration { ModelPath = modelPath, VocabularyPath = vocabPath }))
        {
            // warm up the session (first call pays JIT/graph-init).
            await embedder.GetEmbeddingsAsync(docs[0].Request);

            var sw = Stopwatch.StartNew();
            foreach (var d in docs)
            {
                await embedder.GetEmbeddingsAsync(d.Request);
                await embedder.SplitStringIntoEmbeddingsListAsync(d.Request);
            }
            sw.Stop();
            Report("A) Embed only (single + chunk list)", docs.Count, sw.Elapsed);
        }

        // ── Phase B: full seeding through EmbeddingDatabaseNew ──
        var tempDb = Path.Combine(Path.GetTempPath(), $"profile_{Guid.NewGuid():N}.db");
        var config = new RAGConfiguration { ModelPath = modelPath, VocabularyPath = vocabPath, DatabasePath = tempDb };
        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await Task.Delay(500);
            var sw = Stopwatch.StartNew();
            await EERagBenchmark.SeedDatabaseAsync(dataset, db, generateEmbeddings: true);
            sw.Stop();
            Report("B) Full seed (embed + DB + memory index + LSH)", docs.Count, sw.Elapsed);
        }
        finally
        {
            if (db != null) await db.DisposeAsync();
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(tempDb)) File.Delete(tempDb); break; }
                catch (IOException) { await Task.Delay(100); }
            }
        }
    }

    [SkippableFact]
    public async Task Probe_ThreadScaling()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        _output.WriteLine($"Environment.ProcessorCount = {Environment.ProcessorCount}");

        const string text = "Diffusion tensor MRI reveals microstructural white matter development " +
                            "in preterm and full-term infants assessed in vivo across multiple regions.";
        int iters = 40;

        foreach (var intra in new[] { 1, 2, 4, 8, 0 })
        {
            var cfg = new RAGConfiguration
            {
                ModelPath = modelPath,
                VocabularyPath = vocabPath,
                IntraOpNumThreads = intra,
                InterOpNumThreads = 1
            };
            using var embedder = new EmbedderClassNew(cfg);
            await embedder.GetEmbeddingsAsync(text); // warm up

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iters; i++)
                await embedder.GetEmbeddingsAsync(text);
            sw.Stop();

            _output.WriteLine(
                $"IntraOp={intra,2}: {sw.Elapsed.TotalMilliseconds / iters:F1} ms/embed " +
                $"({iters / sw.Elapsed.TotalSeconds:F1} embeds/s)");
        }
    }

    private void Report(string label, int n, TimeSpan elapsed)
    {
        _output.WriteLine(
            $"{label}: {elapsed.TotalSeconds:F1}s for {n} docs = " +
            $"{elapsed.TotalMilliseconds / n:F1} ms/doc, {n / elapsed.TotalSeconds:F2} docs/s");
    }
}
