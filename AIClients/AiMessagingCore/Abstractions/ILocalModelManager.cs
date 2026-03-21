namespace AiMessagingCore.Abstractions;

/// <summary>
/// Local model lifecycle management contract.
/// </summary>
public interface ILocalModelManager
{
    ValueTask LoadAsync(string modelId, string modelPath, CancellationToken cancellationToken = default);

    ValueTask UnloadAsync(string modelId, CancellationToken cancellationToken = default);

    ValueTask<long?> GetMemoryUsageBytesAsync(string modelId, CancellationToken cancellationToken = default);

    bool IsLoaded(string modelId);
}
