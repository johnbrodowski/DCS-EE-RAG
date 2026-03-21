using System.Text.Json;

namespace AiMessagingCore.Configuration;

/// <summary>
/// Static helper for loading, saving, and applying <see cref="AiLibrarySettings"/>.
///
/// <example>
/// <code>
/// var settings = AiSettings.LoadFromFile("ai-settings.json");
/// AiSettings.ApplyToEnvironment(settings);
/// </code>
/// </example>
/// </summary>
public static class AiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Maps provider name → (API-key env-var, base-URL env-var).
    // Empty string means the provider has no variable of that type.
    private static readonly Dictionary<string, (string ApiKeyVar, string BaseUrlVar)> EnvVarMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["OpenAI"]     = ("OPENAI_API_KEY",    "OPENAI_BASE_URL"),
            ["Anthropic"]  = ("ANTHROPIC_API_KEY", "ANTHROPIC_BASE_URL"),
            ["DeepSeek"]   = ("DEEPSEEK_API_KEY",  "DEEPSEEK_BASE_URL"),
            ["Grok"]       = ("XAI_API_KEY",        "XAI_BASE_URL"),
            ["Groq"]       = ("GROQ_API_KEY",       "GROQ_BASE_URL"),
            ["Duck"]       = ("DUCK_API_KEY",       "DUCK_BASE_URL"),
            ["LMStudio"]   = (string.Empty,         "LMSTUDIO_BASE_URL"),
            ["LlamaSharp"] = (string.Empty,         string.Empty),
        };

    /// <summary>
    /// Loads settings from <paramref name="path"/>.
    /// If the file does not exist, a default file is written at that path and returned.
    /// </summary>
    public static AiLibrarySettings LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = CreateDefault();
            SaveToFile(defaults, path);
            return defaults;
        }

        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<AiLibrarySettings>(json, JsonOptions) ?? CreateDefault();

        // Rebuild with case-insensitive comparer (STJ loses it on deserialize).
        return new AiLibrarySettings
        {
            DefaultProvider  = raw.DefaultProvider,
            TimeoutSeconds   = raw.TimeoutSeconds,
            LoggingVerbosity = raw.LoggingVerbosity,
            RetryPolicy      = raw.RetryPolicy,
            Providers        = new Dictionary<string, ProviderSettings>(raw.Providers, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Loads from the default path: <c>&lt;app-base&gt;/ai-settings.json</c>.
    /// </summary>
    public static AiLibrarySettings Load()
        => LoadFromFile(DefaultFilePath);

    /// <summary>Saves <paramref name="settings"/> to <paramref name="path"/>.</summary>
    public static void SaveToFile(AiLibrarySettings settings, string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));

    /// <summary>Saves to the default path.</summary>
    public static void Save(AiLibrarySettings settings)
        => SaveToFile(settings, DefaultFilePath);

    /// <summary>
    /// Propagates API keys and base URLs from <paramref name="settings"/> to process
    /// environment variables so provider sessions can read them.
    /// Placeholder values containing "YOUR_" are skipped.
    /// </summary>
    public static void ApplyToEnvironment(AiLibrarySettings settings)
    {
        foreach (var (providerName, ps) in settings.Providers)
        {
            if (!EnvVarMap.TryGetValue(providerName, out var vars))
                continue;

            if (!string.IsNullOrWhiteSpace(vars.ApiKeyVar)
                && !string.IsNullOrWhiteSpace(ps.ApiKey)
                && !ps.ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(vars.ApiKeyVar, ps.ApiKey);
            }

            if (!string.IsNullOrWhiteSpace(vars.BaseUrlVar)
                && !string.IsNullOrWhiteSpace(ps.BaseUrl))
            {
                Environment.SetEnvironmentVariable(vars.BaseUrlVar, ps.BaseUrl);
            }

            if (string.Equals(providerName, "LlamaSharp", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(ps.LocalModelPath))
            {
                Environment.SetEnvironmentVariable("LLAMASHARP_MODEL_DIR", ps.LocalModelPath);
            }
        }
    }

    /// <summary>Default settings file path: next to the executable.</summary>
    public static readonly string DefaultFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai-settings.json");

    private static AiLibrarySettings CreateDefault() => new()
    {
        DefaultProvider  = "OpenAI",
        TimeoutSeconds   = 120,
        LoggingVerbosity = "Information",
        RetryPolicy = new RetryPolicySettings
        {
            MaxAttempts                    = 3,
            BaseDelayMilliseconds          = 200,
            CircuitBreakerEnabled          = false,
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerDurationSeconds  = 30
        },
        Providers = new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase)
        {
            ["OpenAI"] = new ProviderSettings
            {
                ProviderType     = "OpenAI",
                ApiKey           = "sk-YOUR_OPENAI_API_KEY",
                BaseUrl          = "https://api.openai.com/v1",
                StreamingEnabled = true,
                Defaults         = new ModelDefaults { Model = "gpt-4o", Temperature = 0.7, MaxTokens = 4096, TopP = 1.0, SystemPrompt = "You are a helpful assistant.", SampleQuery = "What can you help me with today?" }
            },
            ["Anthropic"] = new ProviderSettings
            {
                ProviderType     = "Anthropic",
                ApiKey           = "sk-ant-YOUR_ANTHROPIC_API_KEY",
                BaseUrl          = "https://api.anthropic.com/v1",
                StreamingEnabled = true,
                Defaults         = new ModelDefaults { Model = "claude-sonnet-4-5-20250929", Temperature = 0.7, MaxTokens = 4096, TopP = 1.0, SystemPrompt = "You are a helpful assistant.", SampleQuery = "What can you help me with today?" }
            },
            ["DeepSeek"] = new ProviderSettings
            {
                ProviderType     = "DeepSeek",
                ApiKey           = "YOUR_DEEPSEEK_API_KEY",
                BaseUrl          = "https://api.deepseek.com",
                StreamingEnabled = true,
                Defaults         = new ModelDefaults { Model = "deepseek-chat", Temperature = 0.7, MaxTokens = 4096, TopP = 1.0, SystemPrompt = "You are a helpful assistant.", SampleQuery = "What can you help me with today?" }
            },
            ["Grok"] = new ProviderSettings
            {
                ProviderType     = "Grok",
                ApiKey           = "YOUR_XAI_API_KEY",
                BaseUrl          = "https://api.x.ai/v1",
                StreamingEnabled = true,
                Defaults         = new ModelDefaults { Model = "grok-3-beta", Temperature = 0.7, MaxTokens = 4096, TopP = 1.0, SystemPrompt = "You are a helpful assistant.", SampleQuery = "What can you help me with today?" }
            },
            ["Groq"] = new ProviderSettings
            {
                ProviderType     = "Groq",
                ApiKey           = "gsk_YOUR_GROQ_API_KEY",
                BaseUrl          = "https://api.groq.com/openai/v1",
                StreamingEnabled = true,
                Defaults         = new ModelDefaults { Model = "llama-3.3-70b-versatile", Temperature = 0.7, MaxTokens = 4096, TopP = 1.0, SystemPrompt = "You are a helpful assistant.", SampleQuery = "What can you help me with today?" }
            },
            ["Duck"] = new ProviderSettings
            {
                ProviderType     = "Duck",
                ApiKey           = string.Empty,
                BaseUrl          = string.Empty,
                StreamingEnabled = true,
                Defaults         = new ModelDefaults { Model = "gpt-4o-mini", Temperature = 0.7, MaxTokens = 4096, TopP = 1.0, SystemPrompt = "You are a helpful assistant.", SampleQuery = "What can you help me with today?" }
            },
            ["LMStudio"] = new ProviderSettings
            {
                ProviderType     = "LMStudio",
                BaseUrl          = "http://localhost:1234/v1",
                StreamingEnabled = true,
                Defaults         = new ModelDefaults { Model = "local-model", Temperature = 0.7, MaxTokens = 4096, TopP = 1.0, SystemPrompt = "You are a helpful assistant.", SampleQuery = "What can you help me with today?" }
            },
            ["LlamaSharp"] = new ProviderSettings
            {
                ProviderType     = "LlamaSharp",
                LocalModelPath   = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"),
                StreamingEnabled = true,
                Defaults         = new ModelDefaults { Model = "model.gguf", Temperature = 0.1, MaxTokens = 4096, TopP = 1.0, SystemPrompt = "You are a helpful assistant.", SampleQuery = "What can you help me with today?" }
            }
        }
    };
}
