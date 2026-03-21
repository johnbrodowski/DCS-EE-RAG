using AiMessagingCore.Models;

namespace AiMessagingCore.Events;

/// <summary>
/// Event data raised after a provider has delivered the full response for a turn.
/// Proxies the key <see cref="AiUsageMetrics"/> fields so handlers can write
/// <c>(_, e) =&gt; Console.WriteLine($"Tokens: {e.TotalTokens} ttfb: {e.TimeToFirstToken}")</c>
/// without drilling into <see cref="Metrics"/>.
/// </summary>
public sealed class ResponseCompletedEventArgs : EventArgs
{
    public ResponseCompletedEventArgs(AiUsageMetrics metrics)
    {
        Metrics = metrics;
    }

    /// <summary>Full metrics record for this turn.</summary>
    public AiUsageMetrics Metrics { get; }

    // Convenience proxies
    public string ProviderName      => Metrics.ProviderName;
    public string ModelName         => Metrics.ModelName;
    public int    PromptTokens      => Metrics.PromptTokens;
    public int    CompletionTokens  => Metrics.CompletionTokens;
    public int    TotalTokens       => Metrics.TotalTokens;
    public double TokensPerSecond   => Metrics.TokensPerSecond;
    public TimeSpan TimeToFirstToken  => Metrics.TimeToFirstToken;
    public TimeSpan TotalResponseTime => Metrics.TotalResponseTime;
}
