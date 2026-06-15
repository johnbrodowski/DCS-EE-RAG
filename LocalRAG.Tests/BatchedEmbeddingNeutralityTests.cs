using LocalRAG;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Proves the batched chunk-embedding path produces the same vectors as the
/// single-text path — i.e. the seeding speedup is a pure throughput optimization
/// and does not change retrieval results. Gated on the BERT model like the other
/// integration tests.
/// </summary>
[Trait("Category", "Integration")]
public class BatchedEmbeddingNeutralityTests
{
    private readonly ITestOutputHelper _output;
    public BatchedEmbeddingNeutralityTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task BatchedPath_MatchesSinglePath()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var config = new RAGConfiguration { ModelPath = modelPath, VocabularyPath = vocabPath };
        using var embedder = new EmbedderClassNew(config);

        // A short text (< WordsPerString) yields a single chunk, so the batched list
        // path runs with N=1 over exactly the same tokens as the single-text path.
        const string shortText = "Diffusion tensor MRI reveals white matter development in infants.";

        var single = await embedder.GetEmbeddingsAsync(shortText);
        var batched = await embedder.SplitStringIntoEmbeddingsListAsync(shortText);

        Assert.Single(batched);
        Assert.Equal(single.Length, batched[0].Length);

        double cos = Cosine(single, batched[0]);
        double maxAbsDiff = MaxAbsDiff(single, batched[0]);
        _output.WriteLine($"cosine={cos:F8}  maxAbsDiff={maxAbsDiff:E3}  dim={single.Length}");

        Assert.True(cos > 0.9999, $"Batched embedding diverged from single path (cosine {cos:F8}).");
        Assert.True(maxAbsDiff < 1e-3, $"Per-component drift too large ({maxAbsDiff:E3}).");
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-12);
    }

    private static double MaxAbsDiff(float[] a, float[] b)
    {
        double m = 0;
        for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i]));
        return m;
    }
}
