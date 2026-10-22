using System.Diagnostics;
using LocalRAG;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Texts used to be padded to MaxSequenceLength (512) before every inference, so a 20-token
/// chat turn cost as much as a full page. The padding is attention-masked, so padding only to
/// the text's own length must give the same vector. Gated on the model.
/// </summary>
[Trait("Category", "Integration")]
public class EmbedderPaddingTests
{
    private readonly ITestOutputHelper _output;
    public EmbedderPaddingTests(ITestOutputHelper output) => _output = output;

    private static RAGConfiguration Config(string model, string vocab, bool padToMax) => new()
    {
        ModelPath = model, VocabularyPath = vocab, PadToMaxSequenceLength = padToMax
    };

    private static (string model, string vocab) Paths()
    {
        var d = new RAGConfiguration();
        return (Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? d.ModelPath,
                Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? d.VocabularyPath);
    }

    /// <summary>
    /// A full-precision model gives the same vector however it is padded. The int8 model quantizes
    /// activations with a scale taken over the whole padded tensor, so padding changes its rounding:
    /// measured cosine 0.979 on a three-token text, 0.993 on a chunk, with no loss in retrieval
    /// (LoCoMo recall@5 0.539 vs 0.545).
    /// </summary>
    private static double Tolerance(string model) =>
        model.Contains("int8", StringComparison.OrdinalIgnoreCase) || model.Contains("quant", StringComparison.OrdinalIgnoreCase)
            ? 0.97 : 0.9999;

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static readonly string[] Texts =
    {
        "Hi!",
        "Caroline: I went to the support group yesterday and it was really inspiring.",
        "Kubernetes orchestrates containers across clusters of machines, scheduling them onto nodes.",
        string.Concat(Enumerable.Repeat("The immune response to the pathogen involves many interacting cell types. ", 60)), // > 512 tokens
    };

    [SkippableFact]
    public async Task SingleText_VectorIsTheSame_WithOrWithoutPaddingTo512()
    {
        var (m, v) = Paths();
        Skip.IfNot(File.Exists(m), "BERT model not configured. Set BERT_MODEL_PATH.");
        using var trimmed = new EmbedderClassNew(Config(m, v, padToMax: false));
        using var padded = new EmbedderClassNew(Config(m, v, padToMax: true));

        foreach (var t in Texts)
        {
            var a = await trimmed.GetEmbeddingsAsync(t);
            var b = await padded.GetEmbeddingsAsync(t);
            Assert.Equal(b.Length, a.Length);
            Assert.True(Cosine(a, b) > Tolerance(m), $"cosine {Cosine(a, b):F6} for '{t[..Math.Min(30, t.Length)]}'");
        }
    }

    [SkippableFact]
    public async Task ChunkBatch_VectorsAreTheSame_WithOrWithoutPaddingTo512()
    {
        var (m, v) = Paths();
        Skip.IfNot(File.Exists(m), "BERT model not configured. Set BERT_MODEL_PATH.");
        using var trimmed = new EmbedderClassNew(Config(m, v, padToMax: false));
        using var padded = new EmbedderClassNew(Config(m, v, padToMax: true));

        // A long text splits into many chunks of different lengths, embedded as one batch.
        var text = string.Join(" ", Enumerable.Range(0, 400).Select(i => i % 37 == 0 ? "kubernetes" : $"word{i}"));
        var a = await trimmed.SplitStringIntoEmbeddingsListAsync(text);
        var b = await padded.SplitStringIntoEmbeddingsListAsync(text);

        Assert.Equal(b.Count, a.Count);
        Assert.True(a.Count > 3);
        for (int i = 0; i < a.Count; i++)
            Assert.True(Cosine(a[i], b[i]) > Tolerance(m), $"chunk {i}: cosine {Cosine(a[i], b[i]):F6}");
    }

    [SkippableFact]
    public async Task ShortTexts_AreFasterWithoutPaddingTo512()
    {
        var (m, v) = Paths();
        Skip.IfNot(File.Exists(m), "BERT model not configured. Set BERT_MODEL_PATH.");
        var turns = Enumerable.Range(0, 40).Select(i => $"Melanie: I painted a sunrise number {i} last weekend, it felt great.").ToList();

        async Task<double> Rate(bool padToMax)
        {
            using var e = new EmbedderClassNew(Config(m, v, padToMax));
            await e.GetEmbeddingsAsync(turns[0]); // warm up
            var sw = Stopwatch.StartNew();
            foreach (var t in turns) await e.GetEmbeddingsAsync(t);
            return turns.Count / sw.Elapsed.TotalSeconds;
        }

        var slow = await Rate(true);
        var fast = await Rate(false);
        _output.WriteLine($"short chat turns: padded to 512 {slow:F1}/s, padded to own length {fast:F1}/s ({fast / slow:F1}x)");

        Assert.True(fast > slow * 2, $"expected at least 2x on ~20-token texts, got {fast / slow:F2}x");
    }
}
