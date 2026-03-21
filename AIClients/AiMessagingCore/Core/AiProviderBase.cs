using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Models;

namespace AiMessagingCore.Core;

/// <summary>
/// Convenience base class for provider implementations.
/// Stores <see cref="Name"/> and <see cref="Capabilities"/> and requires subclasses
/// to implement only <see cref="ListModelsAsync"/> and <see cref="CreateSession"/>.
/// </summary>
public abstract class AiProviderBase : IAiProvider
{
    protected AiProviderBase(string name, ProviderCapabilities capabilities)
    {
        Name = name;
        Capabilities = capabilities;
    }

    public string Name { get; }

    public ProviderCapabilities Capabilities { get; }

    public abstract ValueTask<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);

    public abstract IChatSession CreateSession(ChatSessionOptions options);
}
