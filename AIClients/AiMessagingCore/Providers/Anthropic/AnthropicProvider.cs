using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Anthropic;

public sealed class AnthropicProvider : AiProviderBase
{
    public AnthropicProvider() : base(
        "Anthropic",
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
        IReadOnlyList<string> models = ["claude-opus-4-5", "claude-sonnet-4-5", "claude-haiku-4-5"];
        return ValueTask.FromResult(models);
    }

    public override IChatSession CreateSession(ChatSessionOptions options)
        => new AnthropicChatSession(options);
}
