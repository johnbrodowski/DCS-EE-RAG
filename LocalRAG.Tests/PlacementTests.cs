using System.Diagnostics;
using LocalRAG;
using LocalRAG.Benchmarks;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Pins the leaderboard placement: pure exact dense-cosine retrieval over the FULL
/// SciFact corpus (most comparable to published bi-encoder nDCG@10), plus a
/// cross-encoder reranked pass. Brute-force in-memory (no DB / LSH / memory channel),
/// so it's embedding-bound rather than slowed by SQL scans. Gated on the model.
/// </summary>
[Trait("Category", "Placement")]
public class PlacementTests
{
    private readonly ITestOutputHelper _output;
    public PlacementTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task SciFact_Placement_DenseAndReranked()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");
        var dir = Environment.GetEnvironmentVariable("BEIR_SCIFACT_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir), "Set BEIR_SCIFACT_DIR.");

        int? maxQueries = int.TryParse(Environment.GetEnvironmentVariable("PLACE_QUERIES"), out var q) ? q : (int?)null;

        var dataset = BeirDatasetLoader.Load(dir!, "BEIR-SciFact", "test", maxQueries, maxCorpus: null);
        var slugToText = dataset.KnowledgeEntries.ToDictionary(e => e.Slug, e => e.Request);
        _output.WriteLine($"Full corpus {dataset.KnowledgeEntries.Count} docs, {dataset.Cases.Count} queries.");

        // Optional reranker.
        var rerankModel = Environment.GetEnvironmentVariable("RERANKER_MODEL_PATH");
        var rerankVocab = Environment.GetEnvironmentVariable("RERANKER_VOCAB_PATH");
        RerankerClass? reranker = (!string.IsNullOrWhiteSpace(rerankModel) && File.Exists(rerankModel))
            ? new RerankerClass(rerankModel, rerankVocab!, defaults.MaxSequenceLength, defaults.LowercaseInput)
            : null;

        using var embedder = new EmbedderClassNew(new RAGConfiguration { ModelPath = modelPath, VocabularyPath = vocabPath });

        // Embed corpus + queries.
        var sw = Stopwatch.StartNew();
        var docVecs = new Dictionary<string, float[]>(dataset.KnowledgeEntries.Count);
        foreach (var e in dataset.KnowledgeEntries)
            docVecs[e.Slug] = await embedder.GetEmbeddingsAsync(e.Request);
        var qVecs = new List<(BenchmarkCase c, float[] v)>();
        foreach (var c in dataset.Cases)
            qVecs.Add((c, await embedder.GetEmbeddingsAsync(c.Query)));
        sw.Stop();
        _output.WriteLine($"Embedded {docVecs.Count} docs + {qVecs.Count} queries in {sw.Elapsed.TotalSeconds:F0}s.");

        var slugs = docVecs.Keys.ToArray();
        const int rerankK = 20;

        double ndcgDense = 0, recallDense = 0, ndcgRr = 0, recallRr = 0;
        int n = 0;
        foreach (var (c, qv) in qVecs)
        {
            var expected = new HashSet<string>(c.ExpectedSlugs.Where(docVecs.ContainsKey));
            if (expected.Count == 0) continue;
            n++;

            var ranked = slugs
                .Select(s => (s, score: Dot(qv, docVecs[s])))
                .OrderByDescending(x => x.score)
                .Select(x => x.s)
                .ToList();

            ndcgDense += NdcgAtK(expected, ranked, 10);
            recallDense += (double)ranked.Take(10).Count(expected.Contains) / expected.Count;

            if (reranker != null)
            {
                var head = ranked.Take(rerankK).ToList();
                var scores = reranker.Score(c.Query, head.Select(s => slugToText[s]).ToList());
                var rerankedHead = head
                    .Select((s, i) => (s, score: scores[i]))
                    .OrderByDescending(x => x.score)
                    .Select(x => x.s)
                    .ToList();
                var rerankedFull = rerankedHead.Concat(ranked.Skip(rerankK)).ToList();

                ndcgRr += NdcgAtK(expected, rerankedFull, 10);
                recallRr += (double)rerankedFull.Take(10).Count(expected.Contains) / expected.Count;
            }
        }

        _output.WriteLine("");
        _output.WriteLine($"{"pipeline",-22} {"nDCG@10",8} {"Recall@10",10}");
        _output.WriteLine($"{"exact dense (GTE-int8)",-22} {ndcgDense / n,8:F4} {recallDense / n,10:F4}");
        if (reranker != null)
            _output.WriteLine($"{"+ cross-encoder rerank",-22} {ndcgRr / n,8:F4} {recallRr / n,10:F4}");

        reranker?.Dispose();
        Assert.True(n > 0);
    }

    private static double Dot(float[] a, float[] b)
    { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    private static double NdcgAtK(HashSet<string> rel, List<string> ranked, int k)
    {
        double dcg = 0; int lim = Math.Min(k, ranked.Count);
        for (int i = 0; i < lim; i++) if (rel.Contains(ranked[i])) dcg += 1.0 / Math.Log2(i + 2);
        double idcg = 0; int ideal = Math.Min(k, rel.Count);
        for (int i = 0; i < ideal; i++) idcg += 1.0 / Math.Log2(i + 2);
        return idcg == 0 ? 0 : dcg / idcg;
    }
}
