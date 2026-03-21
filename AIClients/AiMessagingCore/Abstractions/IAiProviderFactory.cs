namespace AiMessagingCore.Abstractions;

/// <summary>
/// Factory abstraction for resolving <see cref="IAiProvider"/> instances by name.
/// Designed to be DI-friendly.
/// </summary>
public interface IAiProviderFactory
{
    IAiProvider Create(string providerName);

    IReadOnlyCollection<string> RegisteredProviders { get; }
}
