using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Providers.Anthropic;
using AiMessagingCore.Providers.DeepSeek;
using AiMessagingCore.Providers.Duck;
using AiMessagingCore.Providers.Grok;
using AiMessagingCore.Providers.Groq;
using AiMessagingCore.Providers.Local;
using AiMessagingCore.Providers.OpenAI;

namespace AiMessagingCore.Core;

/// <summary>
/// Fluent builder for <see cref="AiSession"/> instances.
///
/// <para><b>Static entry (uses bundled providers, reads keys from env):</b></para>
/// <code>
/// var session = AiSessionBuilder
///     .WithProvider("Anthropic")
///     .WithModel("claude-opus-4")
///     .WithTemperature(0.5)
///     .WithMaxTokens(1500)
///     .EnableStreaming()
///     .Build();
/// </code>
///
/// <para><b>Factory-based entry (DI / test scenarios):</b></para>
/// <code>
/// var session = new AiSessionBuilder(factory, "OpenAI")
///     .WithModel("gpt-4o")
///     .Build();
/// </code>
/// </summary>
public sealed class AiSessionBuilder
{
    private readonly IAiProviderFactory? _factory;
    private string? _provider;
    private string? _model;
    private bool _streaming = true;
    private double? _temperature;
    private int? _maxTokens;
    private double? _topP;
    private bool? _enableReasoning;
    private string? _systemMessage;
    private TimeSpan _timeout = TimeSpan.FromSeconds(120);

    // ── Constructors ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a builder wired to an explicit factory (DI / testing).
    /// </summary>
    /// <param name="factory">Pre-built provider factory.</param>
    /// <param name="provider">Provider name, e.g. "OpenAI".</param>
    public AiSessionBuilder(IAiProviderFactory factory, string provider)
    {
        _factory  = factory;
        _provider = provider;
    }

    private AiSessionBuilder() { }

    // ── Static entry point ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a builder pre-configured with the named provider.
    /// Uses the built-in default provider set; API keys are read from env vars.
    /// </summary>
    public static AiSessionBuilder WithProvider(string provider)
        => new AiSessionBuilder { _provider = provider };

    // ── Fluent configuration ────────────────────────────────────────────────

    public AiSessionBuilder WithModel(string model)
    {
        _model = model;
        return this;
    }

    public AiSessionBuilder WithTemperature(double temperature)
    {
        _temperature = temperature;
        return this;
    }

    public AiSessionBuilder WithMaxTokens(int maxTokens)
    {
        _maxTokens = maxTokens;
        return this;
    }

    public AiSessionBuilder WithTopP(double topP)
    {
        _topP = topP;
        return this;
    }

    public AiSessionBuilder EnableStreaming(bool enabled = true)
    {
        _streaming = enabled;
        return this;
    }

    /// <summary>Alias for <see cref="EnableStreaming"/>.</summary>
    public AiSessionBuilder WithStreaming(bool enabled = true) => EnableStreaming(enabled);

    public AiSessionBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    public AiSessionBuilder WithReasoningEnabled(bool enabled = true)
    {
        _enableReasoning = enabled;
        return this;
    }

    public AiSessionBuilder WithSystemMessage(string systemMessage)
    {
        _systemMessage = systemMessage;
        return this;
    }

    // ── Build ───────────────────────────────────────────────────────────────

    /// <summary>Constructs the <see cref="AiSession"/>.</summary>
    public AiSession Build()
    {
        if (string.IsNullOrWhiteSpace(_provider))
            throw new InvalidOperationException("Provider must be specified via WithProvider() or the constructor.");
        if (string.IsNullOrWhiteSpace(_model))
            throw new InvalidOperationException("WithModel() must be called before Build().");

        var factory = _factory ?? BuildDefaultFactory();

        var options = new ChatSessionOptions
        {
            ProviderName     = _provider,
            Model            = _model,
            StreamingEnabled = _streaming,
            Timeout          = _timeout,
            Temperature      = _temperature,
            MaxTokens        = _maxTokens,
            TopP             = _topP,
            EnableReasoning  = _enableReasoning,
            SystemMessage    = _systemMessage
        };

        var inner = factory.Create(_provider).CreateSession(options);
        return new AiSession(inner, factory, options);
    }

    // ── Default factory ─────────────────────────────────────────────────────

    private static IAiProviderFactory BuildDefaultFactory()
    {
        var localModels = new InMemoryLocalModelManager();
        IAiProvider[] providers =
        [
            new OpenAiProvider(),
            new AnthropicProvider(),
            new DeepSeekProvider(),
            new GrokProvider(),
            new GroqProvider(),
            new DuckProvider(),
            new LmStudioProvider(localModels),
            new LlamaSharpProvider(localModels)
        ];
        return new AiProviderFactory(providers);
    }
}
