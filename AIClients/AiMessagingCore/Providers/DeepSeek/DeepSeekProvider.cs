using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.DeepSeek;

public sealed class DeepSeekProvider : AiProviderBase
{
    public DeepSeekProvider() : base(
        "DeepSeek",
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
        IReadOnlyList<string> models = ["deepseek-chat", "deepseek-reasoner"];
        return ValueTask.FromResult(models);
    }

    public override IChatSession CreateSession(ChatSessionOptions options)
        => new DeepSeekChatSession(options);
}
