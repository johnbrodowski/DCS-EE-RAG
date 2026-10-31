using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LocalRAG
{
    /// <summary>
    /// Embeddings from an OpenAI-compatible endpoint (LM Studio on the GPU machine, for one). Used instead of the local
    /// ONNX model when <see cref="RAGConfiguration.RemoteEmbeddingUrl"/> is set and the endpoint answers at start-up.
    /// </summary>
    internal sealed class RemoteEmbeddingClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _model;

        public int Dimension { get; private set; }
        public string Model => _model;
        public string Endpoint => _endpoint;

        public RemoteEmbeddingClient(RAGConfiguration config)
        {
            _endpoint = config.RemoteEmbeddingUrl!.TrimEnd('/') + "/embeddings";
            _model = config.RemoteEmbeddingModel;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, config.RemoteEmbeddingTimeoutSeconds)) };
            if (!string.IsNullOrWhiteSpace(config.RemoteEmbeddingApiKey))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.RemoteEmbeddingApiKey);
        }

        /// <summary>True when the endpoint returns a vector for a short test text within the probe time. Sets <see cref="Dimension"/>.</summary>
        public async Task<bool> ProbeAsync(int timeoutMs)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                var v = await EmbedAsync(new[] { "probe" }, cts.Token);
                if (v.Count != 1 || v[0].Length == 0) return false;
                Dimension = v[0].Length;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Embeds the texts in one request. The vectors come back in input order and are not normalised.</summary>
        public async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            var body = JsonSerializer.Serialize(new { model = _model, input = texts, encoding_format = "float" });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_endpoint, content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Embedding endpoint returned {(int)response.StatusCode}: {Truncate(json)}");

            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("data").EnumerateArray()
                .OrderBy(d => d.TryGetProperty("index", out var i) ? i.GetInt32() : 0)
                .Select(d => d.GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray())
                .ToList();
            if (items.Count != texts.Count)
                throw new InvalidOperationException($"Embedding endpoint returned {items.Count} vectors for {texts.Count} texts.");
            if (Dimension != 0 && items.Any(v => v.Length != Dimension))
                throw new InvalidOperationException("Embedding endpoint returned a vector of a different size than at start-up.");
            return items;
        }

        private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];

        public void Dispose() => _http.Dispose();
    }
}
