using LocalRAG;
using LocalRAG.Benchmarks;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Measures retrieval robustness to messy queries — the "smells like shit ->
/// smells like flowers" goal. It perturbs SciFact queries (character typos +
/// dropped words) and compares the full pipeline with exact vs fuzzy lexical
/// matching, against the clean-query baseline. Seeds once, then re-queries with
/// the flag flipped (flags are read at search time). Gated on the model + BEIR set.
/// </summary>
[Trait("Category", "NoisyQuery")]
public class NoisyQueryTests
{
    private readonly ITestOutputHelper _output;
    public NoisyQueryTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task FuzzyLexical_RecoversNoisyQueries()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dir = Environment.GetEnvironmentVariable("BEIR_SCIFACT_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir), "Set BEIR_SCIFACT_DIR.");

        int nQueries = int.TryParse(Environment.GetEnvironmentVariable("NOISY_QUERIES"), out var q) ? q : 60;
        int nCorpus  = int.TryParse(Environment.GetEnvironmentVariable("NOISY_CORPUS"), out var c) ? c : 1500;

        var tempDb = Path.Combine(Path.GetTempPath(), $"noisy_{Guid.NewGuid():N}.db");
        var config = new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = tempDb,
            IndexChunkEmbeddings = false,   // fast path
            UseExactVectorSearch = true     // local-first default
        };

        var dataset = BeirDatasetLoader.Load(dir!, "BEIR-SciFact", "test", maxQueries: nQueries, maxCorpus: nCorpus);
        var perturbed = Perturb(dataset);
        _output.WriteLine($"{dataset.KnowledgeEntries.Count} docs, {dataset.Cases.Count} queries. Example perturbation:");
        for (int i = 0; i < Math.Min(3, dataset.Cases.Count); i++)
            _output.WriteLine($"  '{dataset.Cases[i].Query}'  ->  '{perturbed.Cases[i].Query}'");

        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await Task.Delay(500);
            var slugToId = await EERagBenchmark.SeedDatabaseAsync(dataset, db, generateEmbeddings: true);

            var baseOpts = new RetrievalBenchmarkOptions { TopK = 100, Ks = new[] { 1, 5, 10 }, SearchLevel = 1 };

            config.FuzzyLexicalMode = FuzzyLexicalMode.Off;
            var clean      = await RetrievalBenchmark.RunAsync(dataset,   slugToId, db, Label(baseOpts, "clean-exact"));
            var noisyExact = await RetrievalBenchmark.RunAsync(perturbed, slugToId, db, Label(baseOpts, "noisy-exact"));

            config.FuzzyLexicalMode = FuzzyLexicalMode.Always;
            var noisyFuzzy = await RetrievalBenchmark.RunAsync(perturbed, slugToId, db, Label(baseOpts, "noisy-fuzzy"));

            _output.WriteLine("");
            _output.WriteLine($"{"config",-14} {"nDCG@10",8} {"Recall@10",10} {"MRR",6}");
            foreach (var r in new[] { clean, noisyExact, noisyFuzzy })
                _output.WriteLine($"{r.Label,-14} {r.MeanNdcgAt(10),8:F4} {r.MeanRecallAt(10),10:F4} {r.Mrr,6:F4}");

            double recovered = noisyFuzzy.MeanNdcgAt(10) - noisyExact.MeanNdcgAt(10);
            double lostToNoise = clean.MeanNdcgAt(10) - noisyExact.MeanNdcgAt(10);
            _output.WriteLine("");
            _output.WriteLine($"Noise cost (clean - noisy-exact): {lostToNoise:F4} nDCG@10");
            _output.WriteLine($"Fuzzy recovery (noisy-fuzzy - noisy-exact): {recovered:F4} nDCG@10");

            Assert.True(noisyFuzzy.PositiveCases > 0);
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

    private static RetrievalBenchmarkOptions Label(RetrievalBenchmarkOptions o, string label)
    {
        var c = o.Clone();
        c.Label = label;
        return c;
    }

    // ── Query perturbation: drop ~20% of words + 1–2 character typos ──
    private static BenchmarkDataset Perturb(BenchmarkDataset src)
    {
        var rnd = new Random(7);
        return new BenchmarkDataset
        {
            Name = src.Name + " (noisy)",
            KnowledgeEntries = src.KnowledgeEntries,
            Cases = src.Cases.Select(cs => new BenchmarkCase
            {
                Id = cs.Id,
                Query = PerturbQuery(cs.Query, rnd),
                ExpectedSlugs = cs.ExpectedSlugs,
                ExpectRetrieval = cs.ExpectRetrieval
            }).ToList()
        };
    }

    private static string PerturbQuery(string query, Random rnd)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (words.Count > 3)
        {
            int drop = Math.Max(1, (int)(words.Count * 0.2));
            for (int i = 0; i < drop && words.Count > 2; i++)
                words.RemoveAt(rnd.Next(words.Count));
        }

        int typos = 0;
        for (int i = 0; i < words.Count && typos < 2; i++)
        {
            if (words[i].Length >= 5 && rnd.NextDouble() < 0.5)
            {
                words[i] = Typo(words[i], rnd);
                typos++;
            }
        }
        return string.Join(' ', words);
    }

    private static string Typo(string w, Random rnd)
    {
        int op = rnd.Next(3);
        int i = rnd.Next(1, w.Length - 1);
        var c = w.ToCharArray();
        switch (op)
        {
            case 0: (c[i], c[i - 1]) = (c[i - 1], c[i]); return new string(c); // swap adjacent
            case 1: return w.Remove(i, 1);                                     // delete a char
            default: c[i] = (char)('a' + rnd.Next(26)); return new string(c);  // substitute
        }
    }
}
