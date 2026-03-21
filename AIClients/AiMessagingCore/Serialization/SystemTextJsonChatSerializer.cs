using System.Text.Json;
using AiMessagingCore.Abstractions;
using AiMessagingCore.Models;

namespace AiMessagingCore.Serialization;

/// <summary>
/// System.Text.Json implementation of <see cref="IChatSerializer"/>.
/// </summary>
public sealed class SystemTextJsonChatSerializer : IChatSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string Serialize(IReadOnlyList<ChatMessage> messages)
        => JsonSerializer.Serialize(messages, JsonOptions);

    public IReadOnlyList<ChatMessage> Deserialize(string json)
        => JsonSerializer.Deserialize<List<ChatMessage>>(json, JsonOptions) ?? [];
}
