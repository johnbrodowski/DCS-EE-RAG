using System.Diagnostics;
using System.Text;
using LocalRAG;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Fast head-to-head model comparison. For each model it embeds a small fixed
/// SciFact set and ranks by EXACT brute-force cosine (no DB, no LSH, no chunking),
/// so it measures the *model's* retrieval quality and speed directly, in ~1 min.
/// All models compared here must use mean pooling (MiniLM variants, GTE).
///
/// Set MODEL_PATHS to a semicolon-separated list of model.onnx paths; vocab.txt is
/// taken from each model's own directory.
/// </summary>
[Trait("Category", "ModelSweep")]
public class ModelComparisonTests
{
    private readonly ITestOutputHelper _output;
    public ModelComparisonTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Sweep_Models_BruteForceCosine()
    {
        var dir = Environment.GetEnvironmentVariable("BEIR_SCIFACT_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir), "Set BEIR_SCIFACT_DIR.");

        var pathsEnv = Environment.GetEnvironmentVariable("MODEL_PATHS");
        // REMOTE_EMBEDDING_URL (an OpenAI-compatible base URL, e.g. http://host:1234/v1) adds every embedding model the
        // server lists, or the ones named in REMOTE_MODELS (semicolon-separated).
        var remoteUrl = Environment.GetEnvironmentVariable("REMOTE_EMBEDDING_URL");
        Skip.If(string.IsNullOrWhiteSpace(pathsEnv) && string.IsNullOrWhiteSpace(remoteUrl),
            "Set MODEL_PATHS (semicolon-separated model.onnx paths) and/or REMOTE_EMBEDDING_URL.");

        int nQueries = int.TryParse(Environment.GetEnvironmentVariable("SWEEP_QUERIES"), out var q) ? q : 30;
        int poolSize = int.TryParse(Environment.GetEnvironmentVariable("SWEEP_POOL"), out var p) ? p : 150;

        // ── Build a small fixed evaluation set ──
        var qrels = LoadQrels(Path.Combine(dir!, "qrels", "test.tsv"));
        var queryText = LoadJsonl(Path.Combine(dir!, "queries.jsonl"));
        var corpus = LoadCorpus(Path.Combine(dir!, "corpus.jsonl"));

        var qids = qrels.Keys.Where(queryText.ContainsKey)
            .OrderBy(x => int.TryParse(x, out var n) ? n : int.MaxValue).ThenBy(x => x)
            .Take(nQueries).ToList();

        var relevant = new HashSet<string>(qids.SelectMany(x => qrels[x]));
        var pool = new List<string>(relevant.Where(corpus.ContainsKey));
        var rnd = new Random(1234);
        foreach (var id in corpus.Keys.OrderBy(_ => rnd.Next()))
        {
            if (pool.Count >= poolSize) break;
            if (!relevant.Contains(id)) pool.Add(id);
        }
        _output.WriteLine($"Eval set: {qids.Count} queries, {pool.Count} docs (exact cosine, no LSH/chunks).");
        _output.WriteLine($"{"model",-34} {"dim",4} {"ms/embed",9} {"nDCG@10",8} {"Recall@10",10} {"MRR",6}");

        var entries = new List<(string Label, RAGConfiguration Cfg)>();
        foreach (var modelPath in (pathsEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!File.Exists(modelPath)) { _output.WriteLine($"{Path.GetFileName(modelPath)}: MISSING"); continue; }
            var vocab = Path.Combine(Path.GetDirectoryName(modelPath)!, "vocab.txt");
            entries.Add((Path.GetFileName(modelPath), new RAGConfiguration { ModelPath = modelPath, VocabularyPath = vocab, RemoteEmbeddingUrl = null }));
        }
        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            var named = Environment.GetEnvironmentVariable("REMOTE_MODELS");
            var remoteModels = string.IsNullOrWhiteSpace(named)
                ? await RemoteEmbeddingClient.ListEmbeddingModelsAsync(remoteUrl!, Environment.GetEnvironmentVariable("DCS_EMBEDDING_API_KEY"))
                : named.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (var model in remoteModels)
                entries.Add(("remote:" + model, new RAGConfiguration { RemoteEmbeddingUrl = remoteUrl, RemoteEmbeddingModel = model, RemoteEmbeddingProbeTimeoutMs = 120000 }));
        }

        // Every model runs at the same time (set SWEEP_SEQUENTIAL=1 for one at a time). ms/embed then includes the
        // contention between them, so compare scores across models and treat the times as a rough guide.
        async Task<string> RunModel(string label, RAGConfiguration cfg)
        {
            try { return await RunModelCore(label, cfg); }
            catch (Exception ex) { return $"{label}: FAILED {ex.GetType().Name}: {ex.Message}"; }
        }

        async Task<string> RunModelCore(string label, RAGConfiguration cfg)
        {
            using var embedder = new EmbedderClassNew(cfg);
            if (cfg.RemoteEmbeddingUrl != null && !embedder.ModelFingerprint.StartsWith("remote|"))
                return $"{label}: endpoint did not answer";
            await embedder.GetEmbeddingsAsync(queryText[qids[0]]); // warm up

            var sw = Stopwatch.StartNew();
            int embeds = 0;
            var docVecs = new Dictionary<string, float[]>(pool.Count);
            foreach (var id in pool) { docVecs[id] = await embedder.GetEmbeddingsAsync(corpus[id]); embeds++; }
            var qVecs = new Dictionary<string, float[]>(qids.Count);
            foreach (var id in qids) { qVecs[id] = await embedder.GetEmbeddingsAsync(queryText[id]); embeds++; }
            sw.Stop();

            double ndcg = 0, recall = 0, mrr = 0;
            foreach (var qid in qids)
            {
                var rel = qrels[qid].Where(corpus.ContainsKey).ToHashSet();
                if (rel.Count == 0) continue;
                var ranked = pool
                    .Select(id => (id, score: Dot(qVecs[qid], docVecs[id])))
                    .OrderByDescending(x => x.score)
                    .Select(x => x.id).ToList();

                ndcg += NdcgAtK(rel, ranked, 10);
                recall += (double)ranked.Take(10).Count(rel.Contains) / rel.Count;
                mrr += Rr(rel, ranked);
            }
            int m = qids.Count;
            int dim = qVecs[qids[0]].Length;
            return $"{label,-34} {dim,4} {sw.Elapsed.TotalMilliseconds / embeds,9:F1} " +
                   $"{ndcg / m,8:F4} {recall / m,10:F4} {mrr / m,6:F4}";
        }

        // A model loaded on demand makes the server fail other requests while it loads, so load them one at a time first.
        foreach (var (label, cfg) in entries.Where(e => e.Cfg.RemoteEmbeddingUrl != null))
        {
            using var warm = new RemoteEmbeddingClient(cfg);
            for (int attempt = 0; attempt < 3 && !await warm.ProbeAsync(180000); attempt++)
                _output.WriteLine($"{label}: load attempt {attempt + 1} failed");
        }

        var lines = Environment.GetEnvironmentVariable("SWEEP_SEQUENTIAL") == "1"
            ? await SequentialAsync(entries, RunModel)
            : await Task.WhenAll(entries.Select(e => Task.Run(() => RunModel(e.Label, e.Cfg))));
        foreach (var line in lines) _output.WriteLine(line);
    }

    private static async Task<string[]> SequentialAsync(List<(string Label, RAGConfiguration Cfg)> entries, Func<string, RAGConfiguration, Task<string>> run)
    {
        var lines = new List<string>();
        foreach (var (label, cfg) in entries) lines.Add(await run(label, cfg));
        return lines.ToArray();
    }

    // ── helpers ──
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

    private static double Rr(HashSet<string> rel, List<string> ranked)
    { for (int i = 0; i < ranked.Count; i++) if (rel.Contains(ranked[i])) return 1.0 / (i + 1); return 0; }

    private static Dictionary<string, List<string>> LoadQrels(string path)
    {
        var d = new Dictionary<string, List<string>>();
        bool header = false;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!header) { header = true; if (line.StartsWith("query-id")) continue; }
            var p = line.Split('\t');
            if (p.Length < 3 || !int.TryParse(p[2].Trim(), out var s) || s <= 0) continue;
            (d.TryGetValue(p[0].Trim(), out var l) ? l : d[p[0].Trim()] = new()).Add(p[1].Trim());
        }
        return d;
    }

    private static Dictionary<string, string> LoadJsonl(string path)
    {
        var d = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var o = JObject.Parse(line);
            d[(string)o["_id"]!] = (string?)o["text"] ?? "";
        }
        return d;
    }

    private static Dictionary<string, string> LoadCorpus(string path)
    {
        var d = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var o = JObject.Parse(line);
            var title = (string?)o["title"] ?? "";
            var text = (string?)o["text"] ?? "";
            d[(string)o["_id"]!] = string.IsNullOrWhiteSpace(title) ? text.Trim() : $"{title.Trim()}. {text.Trim()}";
        }
        return d;
    }
}
