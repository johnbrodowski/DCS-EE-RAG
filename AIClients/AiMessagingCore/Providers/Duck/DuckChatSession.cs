using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Duck;

/// <summary>
/// Duck.ai chat session. When DUCK_API_KEY and DUCK_BASE_URL are absent,
/// returns a descriptive fallback message instead of making an HTTP call.
/// </summary>
public sealed class DuckChatSession : ChatSessionBase
{
    private static readonly HttpClient HttpClient = new();

    public DuckChatSession(ChatSessionOptions options) : base(options) { }

    protected override async IAsyncEnumerable<ChatMessage> ExecuteStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        RequestOverrides? overrides,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var apiKey  = Environment.GetEnvironmentVariable("DUCK_API_KEY");
        var baseUrl = Environment.GetEnvironmentVariable("DUCK_BASE_URL");
        var model   = overrides?.Model ?? Model;

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl))
        {
            var fallback = "Duck provider is not configured for API mode; set DUCK_BASE_URL and DUCK_API_KEY.";
            foreach (var token in fallback.Split(' '))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(10, cancellationToken);
                yield return new ChatMessage(ChatRole.Assistant, token + " ", DateTimeOffset.UtcNow, Model: model);
            }
            yield break;
        }

        var request = new
        {
            model,
            stream   = true,
            messages = messages.Select(ToMessage)
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

            var data = line[6..].Trim();
            if (data == "[DONE]") yield break;

            using var doc = JsonDocument.Parse(data);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            if (!choices[0].TryGetProperty("delta", out var delta) || !delta.TryGetProperty("content", out var contentEl)) continue;

            var content = contentEl.GetString();
            if (string.IsNullOrEmpty(content)) continue;

            yield return new ChatMessage(ChatRole.Assistant, content, DateTimeOffset.UtcNow, Model: model);
        }
    }

    private static object ToMessage(ChatMessage m) => new
    {
        role    = m.Role switch { ChatRole.System => "system", ChatRole.Assistant => "assistant", _ => "user" },
        content = m.Content
    };
}
