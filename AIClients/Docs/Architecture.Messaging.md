# AI Messaging Library Architecture

## High-level Architecture Diagram (Text)

```text
+--------------------------------------------------------------+
|                        Application Layer                     |
|  (Console/Web/Worker)                                        |
|   - AI session creation via fluent builder/factory           |
+-------------------------------+------------------------------+
                                |
                                v
+--------------------------------------------------------------+
|                        Core Messaging Layer                  |
|  IAiProviderFactory  -> resolves IAiProvider strategies      |
|  AiSessionBuilder    -> fluent session creation              |
|  IChatSession        -> async send/stream API + events       |
|  IChatSerializer     -> provider-neutral JSON context        |
|  IModelManager       -> model discovery/switching            |
|  ILocalModelManager  -> local model lifecycle                |
+-------------------------------+------------------------------+
                                |
                                v
+--------------------------------------------------------------+
|                     Provider Strategy Layer                  |
|  OpenAiProvider  AnthropicProvider  DeepSeek/Groq/Grok/Duck  Local providers|
|  OpenAiSession   AnthropicSession   provider-specific     LMStudio/LlamaSharp |
|  - Translates neutral ChatMessage[] -> provider payload      |
|  - Handles streaming/non-streaming/cancellation/timeouts     |
+-------------------------------+------------------------------+
                                |
                                v
+--------------------------------------------------------------+
|                    External AI Backends                      |
|  Cloud APIs (OpenAI/Anthropic/...) and Local runtimes        |
+--------------------------------------------------------------+
```


## Folder Structure

```text
AIClients/
  Messaging/
    Abstractions/
      IAiProvider.cs
      IChatSession.cs
      IModelManager.cs
      IChatSerializer.cs
      IAiProviderFactory.cs
      ILocalModelManager.cs
    Configuration/
      AiLibrarySettings.cs
      ProviderSettings.cs
      ModelSettings.cs
      RetryPolicySettings.cs
      ChatSessionOptions.cs
      RequestOverrides.cs
    Core/
      AiProviderBase.cs
      ChatSessionBase.cs
      AiProviderFactory.cs
      AiSessionBuilder.cs
      InMemoryLocalModelManager.cs
    Models/
      ChatRole.cs
      ChatMessage.cs
      TokenUsage.cs
      AiUsageMetrics.cs
      ProviderCapabilities.cs
      AiProviderException.cs
    Providers/
      OpenAI/
      Local/
      Anthropic/
      DeepSeek/
      Grok/
      Groq/
      Duck/
    Serialization/
      SystemTextJsonChatSerializer.cs
```

## Extensibility Guide (Add a Provider)

1. Create `YourProvider : AiProviderBase` and declare `ProviderCapabilities`.
2. Implement `ListModelsAsync` and `CreateSession`.
3. Create `YourProviderChatSession : ChatSessionBase`.
4. In `ExecuteStreamAsync`, translate neutral `ChatMessage` history into your provider request format.
5. Yield partial `ChatMessage` chunks during streaming; base class accumulates final response and metrics.
6. Register provider instance in DI and inject into `AiProviderFactory`.
7. Optionally implement custom `ILocalModelManager` if provider runs local models.

## Scope

This architecture intentionally excludes function calling, tools, MCP, embeddings, image/audio generation, vector databases, and agent workflows.
