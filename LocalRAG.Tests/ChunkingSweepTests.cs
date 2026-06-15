using System.Diagnostics;
using LocalRAG;
using LocalRAG.Benchmarks;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Sweeps chunking settings (no-chunk / N fixed chunks × overlap %) on the current
/// model and reports retrieval quality + seed cost per variant. Chunking changes what
/// is indexed, so each variant reseeds a fresh DB. Grid is small by default and
/// env-adjustable so an external tuner can drive it. Gated on the model + BEIR set.
/// </summary>
[Trait("Category", "ChunkingSweep")]
public class ChunkingSweepTests
{
    private readonly ITestOutputHelper _output;
    public ChunkingSweepTests(ITestOutputHelper output) => _output = output;

    private sealed record Variant(string Label, bool Chunk, int Count, double Overlap);

    [SkippableFact]
    public async Task Sweep_Chunking()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");
        var dir = Environment.GetEnvironmentVariable("BEIR_SCIFACT_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir), "Set BEIR_SCIFACT_DIR.");

        int nQueries = int.TryParse(Environment.GetEnvironmentVariable("CHUNK_QUERIES"), out var q) ? q : 30;
        int nCorpus  = int.TryParse(Environment.GetEnvironmentVariable("CHUNK_CORPUS"), out var c) ? c : 400;

        // Default demo grid; override with CHUNK_VARIANTS="2x20,3x20,3x30" (NxOverlap) plus
        // the implicit "no-chunk" baseline.
        var variants = ParseVariants(Environment.GetEnvironmentVariable("CHUNK_VARIANTS"));

        var dataset = BeirDatasetLoader.Load(dir!, "BEIR-SciFact", "test", maxQueries: nQueries, maxCorpus: nCorpus);
        _output.WriteLine($"{dataset.KnowledgeEntries.Count} docs, {dataset.Cases.Count} queries.");
        _output.WriteLine($"{"variant",-14} {"seed(s)",8} {"nDCG@10",8} {"Recall@10",10} {"MRR",6}");

        foreach (var v in variants)
        {
            var tempDb = Path.Combine(Path.GetTempPath(), $"chunk_{Guid.NewGuid():N}.db");
            var config = new RAGConfiguration
            {
                ModelPath = modelPath,
                VocabularyPath = vocabPath,
                DatabasePath = tempDb,
                UseExactVectorSearch = true,
                IndexChunkEmbeddings = v.Chunk,
                ChunkingStrategy = ChunkingStrategy.FixedCount,
                ChunkCount = v.Count,
                OverlapPercentage = v.Overlap
            };

            EmbeddingDatabaseNew? db = null;
            try
            {
                db = new EmbeddingDatabaseNew(config);
                await Task.Delay(500);
                var sw = Stopwatch.StartNew();
                var slugToId = await EERagBenchmark.SeedDatabaseAsync(dataset, db, generateEmbeddings: true);
                sw.Stop();

                var opts = new RetrievalBenchmarkOptions { TopK = 100, Ks = new[] { 1, 5, 10 }, SearchLevel = 1, Label = v.Label };
                var r = await RetrievalBenchmark.RunAsync(dataset, slugToId, db, opts);
                _output.WriteLine($"{v.Label,-14} {sw.Elapsed.TotalSeconds,8:F0} {r.MeanNdcgAt(10),8:F4} {r.MeanRecallAt(10),10:F4} {r.Mrr,6:F4}");
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
    }

    private static List<Variant> ParseVariants(string? env)
    {
        var list = new List<Variant> { new("no-chunk", false, 1, 0) };
        if (string.IsNullOrWhiteSpace(env))
        {
            list.Add(new("2chunk/20%", true, 2, 20));
            list.Add(new("3chunk/20%", true, 3, 20));
            return list;
        }
        foreach (var tok in env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = tok.Split('x', 'X');
            if (parts.Length == 2 && int.TryParse(parts[0], out var n) && double.TryParse(parts[1], out var ov))
                list.Add(new($"{n}chunk/{ov:F0}%", true, n, ov));
        }
        return list;
    }
}
