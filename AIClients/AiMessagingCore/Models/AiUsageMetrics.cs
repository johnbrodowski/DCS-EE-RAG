namespace AiMessagingCore.Models;

/// <summary>
/// Unified response usage metrics surfaced after each completed turn.
/// </summary>
public sealed record AiUsageMetrics(
    string ProviderName,
    string ModelName,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    double TokensPerSecond,
    TimeSpan TimeToFirstToken,
    TimeSpan TotalResponseTime);
