namespace AiMessagingCore.Events;

/// <summary>
/// Event data raised when a provider session encounters an unrecoverable error.
/// </summary>
public sealed class ErrorEventArgs : EventArgs
{
    public ErrorEventArgs(Exception error)
    {
        Error = error;
    }

    public Exception Error { get; }

    public string Message => Error.Message;
}
