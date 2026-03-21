namespace AiMessagingCore.Models;

/// <summary>
/// Encapsulates the outcome of a completed chat turn, including the final message and usage metrics.
/// </summary>
public sealed class ChatSessionResult
{
    public ChatSessionResult(ChatMessage message, AiUsageMetrics metrics)
    {
        Message = message;
        Metrics = metrics;
    }

    /// <summary>The fully-assembled assistant reply.</summary>
    public ChatMessage Message { get; }

    /// <summary>Token usage and timing metrics for this turn.</summary>
    public AiUsageMetrics Metrics { get; }

    /// <summary>Convenience accessor — full text of the assistant reply.</summary>
    public string Content => Message.Content;

    /// <summary>True when the response was cut short by a cancellation.</summary>
    public bool WasCancelled { get; init; }
}
