namespace AiMessagingCore.Models;

/// <summary>
/// Unified exception type for provider-independent error handling.
/// </summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException(string providerName, string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderName = providerName;
        Code = code;
    }

    public string ProviderName { get; }

    public string Code { get; }
}
