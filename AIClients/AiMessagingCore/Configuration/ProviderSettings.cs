namespace AiMessagingCore.Configuration;

/// <summary>
/// Per-provider configuration block stored in <c>ai-settings.json</c>.
/// </summary>
public sealed class ProviderSettings
{
    public string ProviderType { get; init; } = string.Empty;

    public string? ApiKey { get; init; }

    public string? BaseUrl { get; init; }

    public bool StreamingEnabled { get; init; } = true;

    public bool EnableReasoning { get; init; }

    public string? LocalModelPath { get; init; }

    public ModelDefaults Defaults { get; set; } = new();
}
