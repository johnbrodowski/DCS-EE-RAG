using LocalRAG;
using LocalRAG.Benchmarks;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Short normal end-to-end comparison: seeds the SciFact subset ONCE, then runs the
/// full SearchEmbeddingsAsync pipeline under several search-side configs (LSH vs exact
/// vector, fuzzy off vs always). Only search flags change, so no reseeding — fast.
/// Gated on the model + BEIR set.
/// </summary>
[Trait("Category", "ConfigCompare")]
public class ConfigCompareTests
{
    private readonly ITestOutputHelper _output;
    public ConfigCompareTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Compare_SearchConfigs()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dir = Environment.GetEnvironmentVariable("BEIR_SCIFACT_DIR");
        Skip.If(string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir), "Set BEIR_SCIFACT_DIR.");

        int nQueries = int.TryParse(Environment.GetEnvironmentVariable("CMP_QUERIES"), out var q) ? q : 30;
        int nCorpus  = int.TryParse(Environment.GetEnvironmentVariable("CMP_CORPUS"), out var c) ? c : 500;

        var tempDb = Path.Combine(Path.GetTempPath(), $"cmp_{Guid.NewGuid():N}.db");
        var config = new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = tempDb,
            IndexChunkEmbeddings = false   // fast seed
        };

        var dataset = BeirDatasetLoader.Load(dir!, "BEIR-SciFact", "test", maxQueries: nQueries, maxCorpus: nCorpus);
        _output.WriteLine($"{dataset.KnowledgeEntries.Count} docs, {dataset.Cases.Count} queries (clean).");

        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await Task.Delay(500);
            var slugToId = await EERagBenchmark.SeedDatabaseAsync(dataset, db, generateEmbeddings: true);

            var opts = new RetrievalBenchmarkOptions { TopK = 100, Ks = new[] { 1, 5, 10 }, SearchLevel = 1 };

            var variants = new (string label, bool exact, FuzzyLexicalMode fuzzy)[]
            {
                ("lsh  / no-fuzzy",   false, FuzzyLexicalMode.Off),
                ("exact/ no-fuzzy",   true,  FuzzyLexicalMode.Off),
                ("exact/ fuzzy",      true,  FuzzyLexicalMode.Always),
            };

            _output.WriteLine($"{"config",-16} {"nDCG@10",8} {"Recall@10",10} {"MRR",6}");
            foreach (var (label, exact, fuzzy) in variants)
            {
                config.UseExactVectorSearch = exact;
                config.FuzzyLexicalMode = fuzzy;
                var o = opts.Clone(); o.Label = label;
                var r = await RetrievalBenchmark.RunAsync(dataset, slugToId, db, o);
                _output.WriteLine($"{label,-16} {r.MeanNdcgAt(10),8:F4} {r.MeanRecallAt(10),10:F4} {r.Mrr,6:F4}");
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
}
