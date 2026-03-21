using AiMessagingCore.Configuration;
using AiMessagingCore.Models;

namespace AiMessagingCore.Abstractions;

/// <summary>
/// Strategy interface for provider-specific messaging operations.
/// Implement this to add a new AI backend.
/// </summary>
public interface IAiProvider
{
    /// <summary>Unique provider name, e.g. "OpenAI", "Anthropic".</summary>
    string Name { get; }

    /// <summary>Flags describing what this provider supports at runtime.</summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>Returns models available for this provider.</summary>
    ValueTask<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a new stateful chat session with the given options.</summary>
    IChatSession CreateSession(ChatSessionOptions options);
}
