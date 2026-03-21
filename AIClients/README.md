# AiMessagingCore

A provider-neutral .NET 10 messaging library for streaming AI chat — supporting OpenAI, Anthropic, DeepSeek, Grok, Groq, Duck, LM Studio, and LlamaSharp through a single unified API.

---

## Projects

| Project | Type | Description |
|---|---|---|
| `AiMessagingCore` | Class Library (`net10.0`) | Core library — all abstractions, models, providers |
| `AIClients` | WinForms App (`net10.0-windows`) | Interactive test harness demonstrating the library |

---

## Quick Start

```csharp
// Load settings and push API keys into environment variables
var config = AiSettings.LoadFromFile("ai-settings.json");
AiSettings.ApplyToEnvironment(config);

// Build a session using the static fluent entry point
var session = AiSessionBuilder
    .WithProvider("Anthropic")
    .WithModel("claude-sonnet-4-5")
    .WithTemperature(0.7)
    .WithMaxTokens(2048)
    .EnableStreaming()
    .Build();

// Wire standard .NET EventHandler events
session.OnTokenReceived     += (_, e) => Console.Write(e.Token);
session.OnResponseCompleted += (_, e) =>
    Console.WriteLine($"\n[{e.ProviderName}/{e.ModelName}] tokens={e.TotalTokens} tps={e.TokensPerSecond:F1} ttfb={e.TimeToFirstToken.TotalMilliseconds:F0}ms");
session.OnError             += (_, e) => Console.WriteLine($"\nError: {e.Message}");

await session.SendAsync("Explain event-driven architecture.");
```

### Switch providers mid-conversation

History is preserved across the switch.

```csharp
await session.SwitchProviderAsync("Groq");
await session.SendAsync("Continue where we left off…");
```

### DI / factory-based usage

```csharp
var localModels = new InMemoryLocalModelManager();
var factory = new AiProviderFactory([
    new OpenAiProvider(),
    new AnthropicProvider(),
    new LmStudioProvider(localModels)
]);

var session = new AiSessionBuilder(factory, "OpenAI")
    .WithModel("gpt-4o")
    .WithSystemMessage("You are a helpful assistant.")
    .Build();
```

---

## Configuration

On first run, `AiSettings.Load()` writes `ai-settings.json` next to the executable with defaults for all eight providers. Edit it to supply real API keys.

```json
{
  "defaultProvider": "OpenAI",
  "timeoutSeconds": 120,
  "retryPolicy": {
    "maxAttempts": 3,
    "baseDelayMilliseconds": 200
  },
  "providers": {
    "OpenAI": {
      "providerType": "OpenAI",
      "apiKey": "sk-...",
      "baseUrl": "https://api.openai.com/v1",
      "streamingEnabled": true,
      "defaults": {
        "model": "gpt-4o",
        "temperature": 0.7,
        "maxTokens": 4096,
        "systemPrompt": "You are a helpful assistant."
      }
    }
  }
}
```

`AiSettings.ApplyToEnvironment(config)` propagates keys to process environment variables so all provider sessions can read them. Placeholder values containing `YOUR_` are skipped automatically.

### Environment variable mapping

| Provider | API Key | Base URL |
|---|---|---|
| OpenAI | `OPENAI_API_KEY` | `OPENAI_BASE_URL` |
| Anthropic | `ANTHROPIC_API_KEY` | `ANTHROPIC_BASE_URL` |
| DeepSeek | `DEEPSEEK_API_KEY` | `DEEPSEEK_BASE_URL` |
| Grok | `XAI_API_KEY` | `XAI_BASE_URL` |
| Groq | `GROQ_API_KEY` | `GROQ_BASE_URL` |
| Duck | `DUCK_API_KEY` | `DUCK_BASE_URL` |
| LM Studio | *(none)* | `LMSTUDIO_BASE_URL` |
| LlamaSharp | *(none)* | `LLAMASHARP_MODEL_DIR` |

---

## Library Structure

```
AiMessagingCore/
├── Abstractions/
│   ├── IAiProvider.cs            # Strategy interface for backends
│   ├── IChatSession.cs           # Stateful session contract (events + send/stream)
│   ├── IAiProviderFactory.cs     # Resolves providers by name
│   ├── IChatSerializer.cs        # JSON serialization contract
│   └── ILocalModelManager.cs    # Local model lifecycle
├── Models/
│   ├── ChatMessage.cs            # Provider-neutral message record
│   ├── ChatRole.cs               # System / User / Assistant
│   ├── ChatSessionResult.cs      # Completed-turn result (message + metrics)
│   ├── AiUsageMetrics.cs         # Tokens, TPS, TTFB, total time
│   ├── TokenUsage.cs             # Prompt + completion token counts
│   ├── ProviderCapabilities.cs   # Runtime capability flags
│   └── AiProviderException.cs   # Typed provider error
├── Events/
│   ├── TokenReceivedEventArgs.cs       # e.Token (string), e.PartialMessage
│   ├── ResponseStartedEventArgs.cs     # e.SessionId, e.ProviderName, e.Model
│   ├── ResponseCompletedEventArgs.cs   # e.TotalTokens, e.TimeToFirstToken, …
│   └── AiErrorEventArgs.cs            # e.Error (Exception), e.Message
├── Configuration/
│   ├── AiSettings.cs             # Static helper: LoadFromFile / Save / ApplyToEnvironment
│   ├── AiLibrarySettings.cs      # Root settings object (JSON root)
│   ├── ProviderSettings.cs       # Per-provider config block
│   ├── ModelDefaults.cs          # Default model params per provider
│   ├── ChatSessionOptions.cs     # Immutable session construction options
│   ├── RequestOverrides.cs       # Per-request runtime overrides
│   └── RetryPolicySettings.cs   # Retry + circuit-breaker config
├── Core/
│   ├── ChatSessionBase.cs        # Abstract base: history, streaming, metrics, events
│   ├── AiSession.cs              # Smart wrapper: SwitchProviderAsync + event forwarding
│   ├── AiSessionBuilder.cs       # Fluent builder (static + factory-based entry)
│   ├── AiProviderBase.cs         # Convenience base for provider implementations
│   ├── AiProviderFactory.cs      # Default IAiProviderFactory implementation
│   └── InMemoryLocalModelManager.cs
├── Serialization/
│   └── SystemTextJsonChatSerializer.cs
├── Providers/
│   ├── OpenAI/                   # SSE streaming, Bearer auth
│   ├── Anthropic/                # Messages API, x-api-key, system field
│   ├── DeepSeek/                 # OpenAI-compatible + reasoning model support
│   ├── Grok/                     # xAI, OpenAI-compatible
│   ├── Groq/                     # Ultra-fast hosted inference
│   ├── Duck/                     # Duck.ai, local fallback when unconfigured
│   └── Local/
│       ├── LmStudioProvider      # OpenAI-compatible local HTTP server
│       └── LlamaSharpProvider    # In-process GGUF inference via llama.cpp
└── Examples/
    └── ConsoleUsageExample.cs
```

---

## Key Patterns

### Events — standard `EventHandler<T>`

```csharp
session.OnResponseStarted   += (_, e) => Console.WriteLine($"[{e.ProviderName}] starting…");
session.OnTokenReceived     += (_, e) => Console.Write(e.Token);
session.OnResponseCompleted += (_, e) => Console.WriteLine($"done. {e.TotalTokens} tokens");
session.OnError             += (_, e) => Console.WriteLine($"error: {e.Message}");
session.OnCancelled         += (_, _) => Console.WriteLine("cancelled");
```

### Streaming directly

```csharp
await foreach (var chunk in session.StreamAsync("Tell me a story…"))
{
    if (chunk.Metadata?.ContainsKey("final") == true) break;
    Console.Write(chunk.Content);
}
```

### Saving and restoring context

```csharp
var serializer  = new SystemTextJsonChatSerializer();
var json        = serializer.Serialize(session.Messages);
File.WriteAllText("context.json", json);

// Later — restore into a new session by seeding history manually
var messages = serializer.Deserialize(json);
```

### Per-request overrides

```csharp
await session.SendAsync("Be concise.", overrides: new RequestOverrides
{
    Temperature = 0.2,
    MaxTokens   = 256
});
```

---

## Provider Capabilities

| Provider | Streaming | Model Listing | Reasoning | Local Lifecycle |
|---|:---:|:---:|:---:|:---:|
| OpenAI | ✓ | ✓ | ✓ | |
| Anthropic | ✓ | ✓ | ✓ | |
| DeepSeek | ✓ | ✓ | ✓ | |
| Grok | ✓ | ✓ | ✓ | |
| Groq | ✓ | ✓ | | |
| Duck | ✓ | ✓ | | |
| LM Studio | ✓ | ✓ | | ✓ |
| LlamaSharp | ✓ | ✓ | | ✓ |

All providers support cancellation and timeout override.

---

## Adding a Provider

1. Create `Providers/MyProvider/MyProvider.cs` extending `AiProviderBase`
2. Create `Providers/MyProvider/MyProviderChatSession.cs` extending `ChatSessionBase`
3. Implement `ExecuteStreamAsync` — yield one `ChatMessage` per token/chunk
4. Register it in `AiProviderFactory` or pass it to `new AiSessionBuilder(factory, "MyProvider")`

```csharp
public sealed class MyProviderChatSession : ChatSessionBase
{
    public MyProviderChatSession(ChatSessionOptions options) : base(options) { }

    protected override async IAsyncEnumerable<ChatMessage> ExecuteStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        RequestOverrides? overrides,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // call your API, yield chunks…
        yield return new ChatMessage(ChatRole.Assistant, token, DateTimeOffset.UtcNow);
    }
}
```

---

## Requirements

- .NET 10 SDK
- `LLamaSharp` 0.21.0 (only needed if using the LlamaSharp local provider)
- API keys for whichever cloud providers you use (set in `ai-settings.json` or environment variables)
- For LM Studio: server running at `http://localhost:1234` (configurable)
- For LlamaSharp: `.gguf` model files in `<app>/models/` or `LLAMASHARP_MODEL_DIR`

---

## License

See `LICENSE.txt`.

















