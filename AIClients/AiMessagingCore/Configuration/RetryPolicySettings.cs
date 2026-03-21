namespace AiMessagingCore.Configuration;

/// <summary>
/// Retry policy and optional circuit-breaker settings.
/// </summary>
public sealed class RetryPolicySettings
{
    public int MaxAttempts { get; init; } = 3;

    public int BaseDelayMilliseconds { get; init; } = 200;

    public bool CircuitBreakerEnabled { get; init; }

    public int CircuitBreakerFailureThreshold { get; init; } = 5;

    public int CircuitBreakerDurationSeconds { get; init; } = 30;
}
