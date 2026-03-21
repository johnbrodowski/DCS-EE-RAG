using AiMessagingCore.Abstractions;

namespace AiMessagingCore.Core;

/// <summary>
/// Default provider factory built from a collection of registered <see cref="IAiProvider"/> instances.
/// </summary>
public sealed class AiProviderFactory : IAiProviderFactory
{
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers;

    public AiProviderFactory(IEnumerable<IAiProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> RegisteredProviders => _providers.Keys.ToArray();

    public IAiProvider Create(string providerName)
    {
        if (_providers.TryGetValue(providerName, out var provider))
            return provider;

        throw new InvalidOperationException(
            $"Provider '{providerName}' is not registered. " +
            $"Registered: {string.Join(", ", _providers.Keys)}");
    }
}
