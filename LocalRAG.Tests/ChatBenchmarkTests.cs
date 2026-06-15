using LocalRAG;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Conversational-memory retrieval benchmark over dcs_test_messages.json. Each message
/// is both a corpus doc and a query; the relevant set is the OTHER messages in the same
/// thread (self excluded). Threads are interleaved, so this rewards semantic retrieval,
/// not recency. Runs the full SearchEmbeddingsAsync pipeline under several configs so
/// you can see what's best on YOUR workload (not SciFact). Gated on the model.
/// </summary>
[Trait("Category", "ChatBenchmark")]
public class ChatBenchmarkTests
{
    private readonly ITestOutputHelper _output;
    public ChatBenchmarkTests(ITestOutputHelper output) => _output = output;

    private sealed record Msg(string Id, string Content, string Thread);

    [SkippableFact]
    public async Task Chat_ThreadRetrieval()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dataPath = FindDataFile("dcs_test_messages.json");
        Skip.If(dataPath == null, "dcs_test_messages.json not found.");

        var messages = JArray.Parse(File.ReadAllText(dataPath!))
            .Select(m => new Msg((string)m["id"]!, (string)m["content"]!, (string)m["thread"]!))
            .ToList();

        var threadMembers = messages.GroupBy(m => m.Thread)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToHashSet());

        // Queries: messages in real multi-message threads (exclude noise + cross-ref singletons).
        var queries = messages
            .Where(m => threadMembers[m.Thread].Count >= 2
                     && m.Thread != "noise"
                     && !m.Thread.StartsWith("cross-ref"))
            .ToList();

        _output.WriteLine($"Corpus {messages.Count} msgs, {queries.Count} queries across " +
            $"{queries.Select(q => q.Thread).Distinct().Count()} threads.");

        // Reranker model (optional): construct with it enabled so it loads; toggle per variant.
        var rerankModel = Environment.GetEnvironmentVariable("RERANKER_MODEL_PATH");
        var rerankVocab = Environment.GetEnvironmentVariable("RERANKER_VOCAB_PATH");
        bool hasReranker = !string.IsNullOrWhiteSpace(rerankModel) && File.Exists(rerankModel);

        var tempDb = Path.Combine(Path.GetTempPath(), $"chat_{Guid.NewGuid():N}.db");
        var config = new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = tempDb,
            IndexChunkEmbeddings = false, // messages are short; chunking is a no-op here
            UseReranker = hasReranker,
            RerankerModelPath = rerankModel ?? "",
            RerankerVocabPath = rerankVocab ?? ""
        };

        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await db.EnsureReadyAsync();
            foreach (var m in messages)
                await db.AddRequestToEmbeddingDatabaseAsync(m.Id, m.Content, embed: true);
            await Task.Delay(300);

            var variants = new (string label, bool exact, FuzzyLexicalMode fuzzy, bool rerank)[]
            {
                ("lsh  / no-fuzzy", false, FuzzyLexicalMode.Off,    false),
                ("exact/ no-fuzzy", true,  FuzzyLexicalMode.Off,    false),
                ("exact/ fuzzy",    true,  FuzzyLexicalMode.Always, false),
                ("exact/ rerank",   true,  FuzzyLexicalMode.Off,    true),
            };

            _output.WriteLine($"{"config",-16} {"Recall@5",9} {"nDCG@10",8} {"MRR",6}");
            foreach (var (label, exact, fuzzy, rerank) in variants)
            {
                if (rerank && !hasReranker) { _output.WriteLine($"{label,-16} (reranker model not set — skipped)"); continue; }
                config.UseExactVectorSearch = exact;
                config.FuzzyLexicalMode = fuzzy;
                config.UseReranker = rerank;

                double recall = 0, ndcg = 0, mrr = 0;
                foreach (var qm in queries)
                {
                    var expected = new HashSet<string>(threadMembers[qm.Thread]);
                    expected.Remove(qm.Id);
                    if (expected.Count == 0) continue;

                    var hits = await db.SearchEmbeddingsAsync(qm.Content, topK: 20, minimumSimilarity: 0.0f, searchLevel: 1);
                    var ranked = hits.Select(h => h.RequestID).Where(id => id != null && id != qm.Id).Cast<string>().ToList();

                    recall += (double)ranked.Take(5).Count(expected.Contains) / expected.Count;
                    ndcg += NdcgAtK(expected, ranked, 10);
                    mrr += Rr(expected, ranked);
                }
                int n = queries.Count;
                _output.WriteLine($"{label,-16} {recall / n,9:F4} {ndcg / n,8:F4} {mrr / n,6:F4}");
            }
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

    private static string? FindDataFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        var local = Path.Combine(dir.FullName, name);
        if (File.Exists(local)) return local;
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, name);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return null;
    }
}
