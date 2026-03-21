namespace AiMessagingCore.Models;

/// <summary>
/// Token usage details for a single interaction.
/// </summary>
public sealed record TokenUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}
