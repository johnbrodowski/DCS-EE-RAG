using AiMessagingCore.Configuration;
using AiMessagingCore.Events;
using AiMessagingCore.Models;

namespace AiMessagingCore.Abstractions;

/// <summary>
/// Represents a stateful chat session that preserves provider-neutral context across turns.
/// </summary>
public interface IChatSession
{
    string SessionId { get; }

    string ProviderName { get; }

    string Model { get; }

    IReadOnlyList<ChatMessage> Messages { get; }

    // ── Events (standard EventHandler pattern) ─────────────────────────────

    event EventHandler<ResponseStartedEventArgs>?   OnResponseStarted;

    event EventHandler<TokenReceivedEventArgs>?     OnTokenReceived;

    event EventHandler<ResponseCompletedEventArgs>? OnResponseCompleted;

    event EventHandler<AiErrorEventArgs>?             OnError;

    event EventHandler?                             OnCancelled;

    // ── Operations ──────────────────────────────────────────────────────────

    /// <summary>Sends a user message and returns the final assembled assistant reply.</summary>
    ValueTask<ChatMessage> SendAsync(
        string userMessage,
        RequestOverrides? overrides = null,
        CancellationToken cancellationToken = default);

    /// <summary>Streams the response token-by-token as an async sequence.</summary>
    IAsyncEnumerable<ChatMessage> StreamAsync(
        string userMessage,
        RequestOverrides? overrides = null,
        CancellationToken cancellationToken = default);

    /// <summary>Switches the active model within the same session, preserving history.</summary>
    ValueTask SwitchModelAsync(string model, CancellationToken cancellationToken = default);

    /// <summary>Switches to a different provider while preserving message history.</summary>
    ValueTask SwitchProviderAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// EE-RAG transient injection — sends a user message with background context that is
    /// provided to the model for this one inference call and then discarded.  The background
    /// content is NEVER appended to the persistent <see cref="Messages"/> history, so it
    /// cannot pollute future turns or be re-indexed into the knowledge base.
    /// Only the clean user turn and the assistant reply enter the persistent record.
    /// </summary>
    ValueTask<ChatMessage> SendWithTransientBackgroundAsync(
        string userMessage,
        string transientBackground,
        RequestOverrides? overrides = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the last user+assistant turn from persistent history.
    /// Used by EE-RAG to discard a Phase-1 RETRIEVE response before Phase-2,
    /// so only the final answer appears in conversation history.
    /// </summary>
    void TrimLastTurn();
}
