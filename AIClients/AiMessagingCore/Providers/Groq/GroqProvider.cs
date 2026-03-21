using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Groq;

public sealed class GroqProvider : AiProviderBase
{
    public GroqProvider() : base(
        "Groq",
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
        IReadOnlyList<string> models = ["llama-3.3-70b-versatile", "mixtral-8x7b-32768"];
        return ValueTask.FromResult(models);
    }

    public override IChatSession CreateSession(ChatSessionOptions options)
        => new GroqChatSession(options);
}
