namespace AiMessagingCore.Configuration;

/// <summary>
/// Per-request runtime override values that take precedence over session defaults.
/// </summary>
public sealed class RequestOverrides
{
    public string? Model { get; init; }

    public double? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public double? TopP { get; init; }

    public bool? StreamingEnabled { get; init; }

    public TimeSpan? Timeout { get; init; }

    public bool? EnableReasoning { get; init; }
}
