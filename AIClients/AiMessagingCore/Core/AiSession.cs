using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Events;
using AiMessagingCore.Models;

namespace AiMessagingCore.Core;

/// <summary>
/// Smart session wrapper returned by <see cref="AiSessionBuilder.Build"/>.
/// Delegates all chat operations to the inner provider session and adds
/// <see cref="SwitchProviderAsync"/> by swapping the inner session while
/// preserving the accumulated message history.
/// </summary>
public sealed class AiSession : IChatSession
{
    private IChatSession _inner;
    private readonly IAiProviderFactory _factory;
    private readonly ChatSessionOptions _baseOptions;

    internal AiSession(IChatSession inner, IAiProviderFactory factory, ChatSessionOptions baseOptions)
    {
        _inner = inner;
        _factory = factory;
        _baseOptions = baseOptions;
        WireEvents();
    }

    // ── IChatSession ────────────────────────────────────────────────────────

    public string SessionId    => _inner.SessionId;
    public string ProviderName => _inner.ProviderName;
    public string Model        => _inner.Model;
    public IReadOnlyList<ChatMessage> Messages => _inner.Messages;

    public event EventHandler<ResponseStartedEventArgs>?   OnResponseStarted;
    public event EventHandler<TokenReceivedEventArgs>?     OnTokenReceived;
    public event EventHandler<ResponseCompletedEventArgs>? OnResponseCompleted;
    public event EventHandler<AiErrorEventArgs>?             OnError;
    public event EventHandler?                             OnCancelled;

    public ValueTask<ChatMessage> SendAsync(
        string userMessage,
        RequestOverrides? overrides = null,
        CancellationToken cancellationToken = default)
        => _inner.SendAsync(userMessage, overrides, cancellationToken);

    public IAsyncEnumerable<ChatMessage> StreamAsync(
        string userMessage,
        RequestOverrides? overrides = null,
        CancellationToken cancellationToken = default)
        => _inner.StreamAsync(userMessage, overrides, cancellationToken);

    public ValueTask<ChatMessage> SendWithTransientBackgroundAsync(
        string userMessage,
        string transientBackground,
        RequestOverrides? overrides = null,
        CancellationToken cancellationToken = default)
        => _inner.SendWithTransientBackgroundAsync(userMessage, transientBackground, overrides, cancellationToken);

    public void TrimLastTurn() => _inner.TrimLastTurn();

    public ValueTask SwitchModelAsync(string model, CancellationToken cancellationToken = default)
        => _inner.SwitchModelAsync(model, cancellationToken);

    /// <summary>
    /// Switches to <paramref name="providerName"/> while preserving the full message history.
    /// All previously attached event handlers are re-wired to the new inner session.
    /// </summary>
    public ValueTask SwitchProviderAsync(string providerName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var history = _inner.Messages.ToList();
        UnwireEvents();

        var newOptions = new ChatSessionOptions
        {
            ProviderName     = providerName,
            Model            = _baseOptions.Model,
            StreamingEnabled = _baseOptions.StreamingEnabled,
            Timeout          = _baseOptions.Timeout,
            Temperature      = _baseOptions.Temperature,
            MaxTokens        = _baseOptions.MaxTokens,
            TopP             = _baseOptions.TopP,
            EnableReasoning  = _baseOptions.EnableReasoning
            // SystemMessage intentionally omitted — already seeded into history
        };

        var newSession = _factory.Create(providerName).CreateSession(newOptions);

        if (newSession is ChatSessionBase csb)
            csb.SeedHistory(history);

        _inner = newSession;
        WireEvents();
        return ValueTask.CompletedTask;
    }

    // ── Event forwarding ────────────────────────────────────────────────────

    private void WireEvents()
    {
        _inner.OnResponseStarted   += ForwardResponseStarted;
        _inner.OnTokenReceived     += ForwardTokenReceived;
        _inner.OnResponseCompleted += ForwardResponseCompleted;
        _inner.OnError             += ForwardError;
        _inner.OnCancelled         += ForwardCancelled;
    }

    private void UnwireEvents()
    {
        _inner.OnResponseStarted   -= ForwardResponseStarted;
        _inner.OnTokenReceived     -= ForwardTokenReceived;
        _inner.OnResponseCompleted -= ForwardResponseCompleted;
        _inner.OnError             -= ForwardError;
        _inner.OnCancelled         -= ForwardCancelled;
    }

    private void ForwardResponseStarted(object? s, ResponseStartedEventArgs e)   => OnResponseStarted?.Invoke(this, e);
    private void ForwardTokenReceived(object? s, TokenReceivedEventArgs e)        => OnTokenReceived?.Invoke(this, e);
    private void ForwardResponseCompleted(object? s, ResponseCompletedEventArgs e)=> OnResponseCompleted?.Invoke(this, e);
    private void ForwardError(object? s, AiErrorEventArgs e)                        => OnError?.Invoke(this, e);
    private void ForwardCancelled(object? s, EventArgs e)                         => OnCancelled?.Invoke(this, e);
}
