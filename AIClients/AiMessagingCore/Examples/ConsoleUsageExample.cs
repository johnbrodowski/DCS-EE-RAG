using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Serialization;

namespace AiMessagingCore.Examples;

/// <summary>
/// Demonstrates the clean fluent API available to library consumers.
/// </summary>
public static class ConsoleUsageExample
{
    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Load settings and apply API keys to environment variables.
        var config = AiSettings.LoadFromFile("ai-settings.json");
        AiSettings.ApplyToEnvironment(config);

        // ── Build a session using the static fluent entry point ─────────────
        var session = AiSessionBuilder
            .WithProvider("OpenAI")
            .WithModel("gpt-4o")
            .WithTemperature(0.7)
            .WithMaxTokens(4096)
            .EnableStreaming()
            .Build();

        // Wire standard EventHandler events.
        session.OnTokenReceived     += (_, e) => Console.Write(e.Token);
        session.OnResponseCompleted += (_, e) =>
            Console.WriteLine($"\n[{e.ProviderName}/{e.ModelName}] tokens={e.TotalTokens} tps={e.TokensPerSecond:F2} ttfb={e.TimeToFirstToken.TotalMilliseconds:F0}ms");

        await session.SendAsync("Explain asynchronous event-driven architecture.", cancellationToken: cancellationToken);

        // Persist context as JSON.
        var serializer  = new SystemTextJsonChatSerializer();
        var contextJson = serializer.Serialize(session.Messages);
        Console.WriteLine("\nSerialized context:");
        Console.WriteLine(contextJson);

        // ── Switch provider mid-conversation, history is preserved ──────────
        await session.SwitchProviderAsync("Groq", cancellationToken);

        await session.SendAsync("Continue where we left off…", cancellationToken: cancellationToken);
    }
}
