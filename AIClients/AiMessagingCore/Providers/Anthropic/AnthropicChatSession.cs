using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Anthropic;

/// <summary>
/// Anthropic chat session — SSE streaming via <c>/messages</c>.
/// System prompt is sent as a separate top-level field per the Anthropic API spec.
/// Reads ANTHROPIC_API_KEY and ANTHROPIC_BASE_URL from environment.
/// </summary>
public sealed class AnthropicChatSession : ChatSessionBase
{
    private static readonly HttpClient HttpClient = new();

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicChatSession(ChatSessionOptions options) : base(options) { }

    protected override async IAsyncEnumerable<ChatMessage> ExecuteStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        RequestOverrides? overrides,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured.");

        var model    = overrides?.Model ?? Model;
        var baseUrl  = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com/v1";

        // Concatenate ALL system-role messages so that transient injections (e.g. RAG candidate
        // headers appended by SendWithTransientBackgroundAsync) are not silently dropped.
        // The Anthropic API accepts a single string for `system`; joining with a double newline
        // preserves the logical boundary between the session prompt and any injected context.
        var systemParts = messages
            .Where(x => x.Role == ChatRole.System)
            .Select(x => x.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
        var sysPrompt = systemParts.Count > 0 ? string.Join("\n\n", systemParts) : null;

        var request = new
        {
            model,
            max_tokens  = overrides?.MaxTokens ?? 1024,
            temperature = overrides?.Temperature,
            top_p       = overrides?.TopP,
            stream      = true,
            system      = sysPrompt,
            messages    = messages
                .Where(x => x.Role is ChatRole.User or ChatRole.Assistant)
                .Select(m => new { role = m.Role == ChatRole.Assistant ? "assistant" : "user", content = m.Content })
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request, RequestJsonOptions), Encoding.UTF8, "application/json");

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
            if (!doc.RootElement.TryGetProperty("type", out var typeEl)) continue;
            if (!string.Equals(typeEl.GetString(), "content_block_delta", StringComparison.Ordinal)) continue;
            if (!doc.RootElement.TryGetProperty("delta", out var delta) || !delta.TryGetProperty("text", out var textEl)) continue;

            var content = textEl.GetString();
            if (string.IsNullOrEmpty(content)) continue;

            yield return new ChatMessage(ChatRole.Assistant, content, DateTimeOffset.UtcNow, Model: model);
        }
    }
}
