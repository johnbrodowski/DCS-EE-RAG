namespace AiMessagingCore.Events;

/// <summary>
/// Event data raised when a provider begins streaming a response.
/// </summary>
public sealed class ResponseStartedEventArgs : EventArgs
{
    public ResponseStartedEventArgs(string sessionId, string providerName, string model)
    {
        SessionId = sessionId;
        ProviderName = providerName;
        Model = model;
    }

    public string SessionId { get; }
    public string ProviderName { get; }
    public string Model { get; }
}
