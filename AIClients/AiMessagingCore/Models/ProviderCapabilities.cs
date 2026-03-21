namespace AiMessagingCore.Models;

/// <summary>
/// Runtime-discoverable capability flags published by each provider.
/// </summary>
public sealed record ProviderCapabilities(
    bool SupportsStreaming,
    bool SupportsModelListing,
    bool SupportsRuntimeModelSwitch,
    bool SupportsReasoningOptions,
    bool SupportsLocalLifecycle,
    bool SupportsCancellation,
    bool SupportsTimeoutOverride);
