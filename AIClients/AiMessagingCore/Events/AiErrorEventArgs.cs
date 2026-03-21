namespace AiMessagingCore.Events;

/// <summary>
/// Event data raised when a provider session encounters an unrecoverable error.
/// </summary>
public sealed class AiErrorEventArgs : EventArgs
{
    public AiErrorEventArgs(Exception error)
    {
        Error = error;
    }

    public Exception Error { get; }

    public string Message => Error.Message;
}
