using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Local;

public sealed class LmStudioProvider : AiProviderBase
{
    private readonly ILocalModelManager _localModelManager;

    public LmStudioProvider(ILocalModelManager localModelManager) : base(
        "LMStudio",
        new ProviderCapabilities(
            SupportsStreaming: true,
            SupportsModelListing: true,
            SupportsRuntimeModelSwitch: true,
            SupportsReasoningOptions: false,
            SupportsLocalLifecycle: true,
            SupportsCancellation: true,
            SupportsTimeoutOverride: true))
    {
        _localModelManager = localModelManager;
    }

    public override ValueTask<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> models = ["llama-3.1-8b-instruct", "mistral-7b-instruct"];
        return ValueTask.FromResult(models);
    }

    public override IChatSession CreateSession(ChatSessionOptions options)
        => new LmStudioChatSession(options, _localModelManager);
}
