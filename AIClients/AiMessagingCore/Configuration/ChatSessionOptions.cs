namespace AiMessagingCore.Configuration;

/// <summary>
/// Session-level immutable options supplied at construction time.
/// </summary>
public sealed class ChatSessionOptions
{
    public required string ProviderName { get; init; }

    public required string Model { get; init; }

    public bool StreamingEnabled { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    public double? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public double? TopP { get; init; }

    public bool? EnableReasoning { get; init; }

    public string? SystemMessage { get; init; }
}
