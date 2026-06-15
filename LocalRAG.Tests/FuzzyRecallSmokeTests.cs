using LocalRAG;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Fast end-to-end check that the trigram index builds (FTS5 'trigram' tokenizer is
/// available) and that fuzzy candidate recall + the UNION query run without error and
/// surface a misspelled distinctive term. Gated on the model; tiny corpus, seconds.
/// </summary>
[Trait("Category", "Integration")]
public class FuzzyRecallSmokeTests
{
    private readonly ITestOutputHelper _output;
    public FuzzyRecallSmokeTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Fuzzy_FindsMisspelledDistinctiveTerm()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var tempDb = Path.Combine(Path.GetTempPath(), $"fuzzy_{Guid.NewGuid():N}.db");
        var config = new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = tempDb,
            IndexChunkEmbeddings = false,
            UseExactVectorSearch = true,
            FuzzyLexicalMode = FuzzyLexicalMode.Always,
            FuzzyCandidateRecall = true,
            FuzzyMatchThreshold = 0.85
        };

        EmbeddingDatabaseNew? db = null;
        try
        {
            db = new EmbeddingDatabaseNew(config);
            await Task.Delay(500);

            var docs = new (string id, string text)[]
            {
                ("kube",     "Kubernetes orchestrates containers across a cluster of nodes."),
                ("postgres", "PostgreSQL is a relational database with strong indexing support."),
                ("auth",     "Reset your password from the account security settings page."),
                ("docker",   "Docker packages applications into portable images."),
                ("git",      "Git tracks changes to source code with commits and branches.")
            };
            foreach (var (id, text) in docs)
                await db.AddRequestToEmbeddingDatabaseAsync(id, text, embed: true);
            await Task.Delay(300);

            // Misspelled + missing words: "kubernates" (typo) "contaners" (typo).
            var results = await db.SearchEmbeddingsAsync(
                "kubernates contaners cluster", topK: 3, minimumSimilarity: 0.0f, searchLevel: 1);

            _output.WriteLine($"Top results: {string.Join(", ", results.Select(r => $"{r.RequestID}:{r.Similarity:F3}"))}");

            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.RequestID == "kube");
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
