namespace AiMessagingCore.Models;

/// <summary>
/// Provider-neutral chat message used for history persistence and wire translation.
/// </summary>
public sealed record ChatMessage(
    ChatRole Role,
    string Content,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string>? Metadata = null,
    TokenUsage? TokenUsage = null,
    string? Model = null);
