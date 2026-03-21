using System.Collections.Concurrent;
using AiMessagingCore.Abstractions;

namespace AiMessagingCore.Core;

/// <summary>
/// Thread-safe in-memory model lifecycle tracker.
/// Does not perform actual loading — use as a lightweight registry.
/// </summary>
public sealed class InMemoryLocalModelManager : ILocalModelManager
{
    private readonly ConcurrentDictionary<string, string> _loadedModels =
        new(StringComparer.OrdinalIgnoreCase);

    public ValueTask LoadAsync(string modelId, string modelPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loadedModels.TryAdd(modelId, modelPath);
        return ValueTask.CompletedTask;
    }

    public ValueTask UnloadAsync(string modelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loadedModels.TryRemove(modelId, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<long?> GetMemoryUsageBytesAsync(string modelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<long?>(null);
    }

    public bool IsLoaded(string modelId) => _loadedModels.ContainsKey(modelId);
}
