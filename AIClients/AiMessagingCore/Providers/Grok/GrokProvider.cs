using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Grok;

public sealed class GrokProvider : AiProviderBase
{
    public GrokProvider() : base(
        "Grok",
        new ProviderCapabilities(
            SupportsStreaming: true,
            SupportsModelListing: true,
            SupportsRuntimeModelSwitch: true,
            SupportsReasoningOptions: true,
            SupportsLocalLifecycle: false,
            SupportsCancellation: true,
            SupportsTimeoutOverride: true))
    { }

    public override ValueTask<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> models = ["grok-3", "grok-3-mini"];
        return ValueTask.FromResult(models);
    }

    public override IChatSession CreateSession(ChatSessionOptions options)
        => new GrokChatSession(options);
}
