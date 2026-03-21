namespace AiMessagingCore.Configuration;

/// <summary>
/// Model behavior defaults persisted per-provider in <c>ai-settings.json</c>.
/// Previously named <c>ModelSettings</c>; renamed to <c>ModelDefaults</c> for clarity.
/// </summary>
public sealed class ModelDefaults
{
    public string Model { get; set; } = string.Empty;

    public double Temperature { get; set; } = 0.7;

    public int MaxTokens { get; set; } = 1024;

    public double TopP { get; set; } = 1.0;

    public string SystemPrompt { get; set; } = string.Empty;

    public string SampleQuery { get; set; } = string.Empty;
}
