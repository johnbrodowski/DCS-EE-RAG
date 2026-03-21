using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Local;

public sealed class LlamaSharpProvider : AiProviderBase
{
    private readonly ILocalModelManager _localModelManager;

    public LlamaSharpProvider(ILocalModelManager localModelManager) : base(
        "LlamaSharp",
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
        var modelDir = Environment.GetEnvironmentVariable("LLAMASHARP_MODEL_DIR")
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");

        if (!Directory.Exists(modelDir))
        {
            IReadOnlyList<string> empty = [];
            return ValueTask.FromResult(empty);
        }

        IReadOnlyList<string> models = Directory
            .GetFiles(modelDir, "*.gguf")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Cast<string>()
            .ToList();

        return ValueTask.FromResult(models);
    }

    public override IChatSession CreateSession(ChatSessionOptions options)
        => new LlamaSharpChatSession(options, _localModelManager);
}
