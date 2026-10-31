using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LocalRAG
{
    /// <summary>
    /// Embeddings from an OpenAI-compatible endpoint (LM Studio on the GPU machine, for one). Used instead of the local
    /// ONNX model when <see cref="RAGConfiguration.RemoteEmbeddingUrl"/> is set and the endpoint answers at start-up.
    /// </summary>
    public sealed class RemoteEmbeddingClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private string _model;
        private readonly string _baseUrl;
        private readonly string? _apiKey;

        public int Dimension { get; private set; }
        public string Model => _model;
        public string Endpoint => _endpoint;

        public RemoteEmbeddingClient(RAGConfiguration config)
        {
            _endpoint = config.RemoteEmbeddingUrl!.TrimEnd('/') + "/embeddings";
            _model = config.RemoteEmbeddingModel ?? "";
            _baseUrl = config.RemoteEmbeddingUrl!.TrimEnd('/');
            _apiKey = config.RemoteEmbeddingApiKey;
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
                if (string.IsNullOrWhiteSpace(_model) || _model.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    // No model named: use the first embedding model the server has loaded.
                    var models = await ListEmbeddingModelsAsync(_baseUrl, _apiKey, cts.Token);
                    if (models.Count == 0) return false;
                    _model = models[0];
                }
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

        /// <summary>
        /// The embedding models the server offers. LM Studio's own listing says which models are embedding models; any
        /// other server (or an older LM Studio) is asked for its plain model list, narrowed by name when that is possible.
        /// </summary>
        public static async Task<List<string>> ListEmbeddingModelsAsync(string baseUrl, string? apiKey, CancellationToken ct = default)
        {
            baseUrl = baseUrl.TrimEnd('/');
            var origin = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? baseUrl[..^3] : baseUrl;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrWhiteSpace(apiKey))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(origin + "/api/v0/models", ct));
                var native = doc.RootElement.GetProperty("data").EnumerateArray()
                    .Where(m => m.TryGetProperty("type", out var t) && t.GetString() == "embeddings")
                    .Select(m => m.GetProperty("id").GetString()!).ToList();
                if (native.Count > 0) return native;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { /* not LM Studio, or too old: use the standard listing */ }

            using var std = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "/models", ct));
            var all = std.RootElement.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()!).ToList();
            var named = all.Where(id => System.Text.RegularExpressions.Regex.IsMatch(id, "embed|gte|bge|e5|minilm|nomic|arctic|mxbai", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
            return named.Count > 0 ? named : all;
        }

        private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];

        public void Dispose() => _http.Dispose();
    }
}
