namespace AiMessagingCore.Configuration;

/// <summary>
/// Root strongly-typed settings object for the messaging library.
/// Serialized to / deserialized from <c>ai-settings.json</c>.
/// </summary>
public sealed class AiLibrarySettings
{
    public string DefaultProvider { get; init; } = string.Empty;

    public Dictionary<string, ProviderSettings> Providers { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int TimeoutSeconds { get; init; } = 120;

    public RetryPolicySettings RetryPolicy { get; init; } = new();

    public string LoggingVerbosity { get; init; } = "Information";

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(DefaultProvider))
            errors.Add("DefaultProvider is required.");

        if (Providers.Count == 0)
            errors.Add("At least one provider configuration is required.");

        if (!string.IsNullOrWhiteSpace(DefaultProvider) && !Providers.ContainsKey(DefaultProvider))
            errors.Add($"DefaultProvider '{DefaultProvider}' is not present in Providers.");

        if (TimeoutSeconds <= 0)
            errors.Add("TimeoutSeconds must be greater than zero.");

        return errors;
    }
}
