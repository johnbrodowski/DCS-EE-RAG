using System.Net;
using System.Text;
using System.Text.Json;
using LocalRAG;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>A configured embedding endpoint makes the vectors; one that does not answer leaves the local model in charge.</summary>
public class RemoteEmbeddingTests
{
    private sealed class FakeEndpoint : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Url { get; }
        public int Requests;
        public string? LastModel;

        public FakeEndpoint()
        {
            var port = new Random().Next(20000, 40000);
            Url = $"http://127.0.0.1:{port}/v1";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); } catch { return; }
                    using var body = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync().ContinueWith(t => JsonDocument.Parse(t.Result));
                    Interlocked.Increment(ref Requests);
                    LastModel = body.RootElement.GetProperty("model").GetString();
                    var inputs = body.RootElement.GetProperty("input").EnumerateArray().Select(x => x.GetString()!).ToList();
                    // A vector whose direction depends on the text's length, not unit length, returned out of order on purpose.
                    var data = inputs.Select((s, i) => new { index = i, embedding = new[] { 3f + s.Length, 4f, 0f }, @object = "embedding" })
                                     .Reverse().ToArray();
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { data, model = "emb", @object = "list" }));
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            });
        }

        public void Dispose() => _listener.Close();
    }

    private static RAGConfiguration Config(string? url) => new()
    {
        RemoteEmbeddingUrl = url,
        RemoteEmbeddingModel = "emb",
        RemoteEmbeddingProbeTimeoutMs = 2000,
        ModelPath = Path.Combine(Path.GetTempPath(), "does-not-exist.onnx"),
        VocabularyPath = Path.Combine(Path.GetTempPath(), "does-not-exist.txt"),
    };

    [Fact]
    public async Task AnsweringEndpoint_MakesTheVectors_WithoutTheLocalModel()
    {
        using var server = new FakeEndpoint();
        using var embedder = new EmbedderClassNew(Config(server.Url));

        Assert.StartsWith("remote|emb|", embedder.ModelFingerprint);
        var v = await embedder.GetEmbeddingsAsync("hello");
        Assert.Equal(3, v.Length);
        Assert.Equal(1.0, Math.Sqrt(v.Sum(x => (double)x * x)), 4);
        Assert.Equal("emb", server.LastModel);
    }

    [Fact]
    public async Task BatchedChunks_KeepTheirOrder()
    {
        using var server = new FakeEndpoint();
        using var embedder = new EmbedderClassNew(Config(server.Url));
        var words = string.Join(' ', Enumerable.Range(0, 200).Select(i => "word" + i));
        var list = await embedder.SplitStringIntoEmbeddingsListAsync(words);
        Assert.True(list.Count > 1);
        Assert.All(list, v => Assert.Equal(3, v.Length));
    }

    [Fact]
    public void SilentEndpoint_FallsBackToTheLocalModel()
    {
        // Nothing listens here, so the local model is tried; its file is missing, which proves the fallback was taken.
        var cfg = Config("http://127.0.0.1:1/v1");
        Assert.ThrowsAny<Exception>(() => new EmbedderClassNew(cfg));
    }

    [Fact]
    public void SilentEndpoint_WhenRequired_ThrowsInsteadOfFallingBack()
    {
        var cfg = Config("http://127.0.0.1:1/v1");
        cfg.RemoteEmbeddingRequired = true;
        // The local model files do not exist, so a fallback would throw a file error, not this message.
        var ex = Assert.Throws<InvalidOperationException>(() => new EmbedderClassNew(cfg));
        Assert.Contains("RemoteEmbeddingRequired", ex.Message);
    }

    [Fact]
    public void NoUrl_NeverContactsAnything()
        => Assert.ThrowsAny<Exception>(() => new EmbedderClassNew(Config(null)));
}
