using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.DeepSeek;

public sealed class DeepSeekChatSession : ChatSessionBase
{
    private static readonly HttpClient HttpClient = new();

    public DeepSeekChatSession(ChatSessionOptions options) : base(options) { }

    protected override async IAsyncEnumerable<ChatMessage> ExecuteStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        RequestOverrides? overrides,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? throw new InvalidOperationException("DEEPSEEK_API_KEY is not configured.");

        var model   = overrides?.Model ?? Model;
        var baseUrl = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL") ?? "https://api.deepseek.com";

        var request = new
        {
            model,
            stream      = true,
            temperature = overrides?.Temperature,
            max_tokens  = overrides?.MaxTokens,
            top_p       = overrides?.TopP,
            messages    = messages.Select(ToMessage)
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
