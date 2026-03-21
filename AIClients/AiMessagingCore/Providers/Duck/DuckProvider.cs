using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Duck;

public sealed class DuckProvider : AiProviderBase
{
    public DuckProvider() : base(
        "Duck",
        new ProviderCapabilities(
            SupportsStreaming: true,
            SupportsModelListing: true,
            SupportsRuntimeModelSwitch: true,
            SupportsReasoningOptions: false,
            SupportsLocalLifecycle: false,
            SupportsCancellation: true,
            SupportsTimeoutOverride: true))
    { }

    public override ValueTask<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> models = ["gpt-4o-mini", "claude-3-haiku", "llama-3.3-70b"];
        return ValueTask.FromResult(models);
    }

    public override IChatSession CreateSession(ChatSessionOptions options)
        => new DuckChatSession(options);
}
