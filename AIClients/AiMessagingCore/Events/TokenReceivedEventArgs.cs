using AiMessagingCore.Models;

namespace AiMessagingCore.Events;

/// <summary>
/// Event data for each streaming token received from a provider.
/// </summary>
public sealed class TokenReceivedEventArgs : EventArgs
{
    public TokenReceivedEventArgs(string token, ChatMessage partialMessage)
    {
        Token = token;
        PartialMessage = partialMessage;
    }

    /// <summary>The raw text fragment emitted by the provider.</summary>
    public string Token { get; }

    /// <summary>The underlying partial <see cref="ChatMessage"/> carrying this token.</summary>
    public ChatMessage PartialMessage { get; }
}
