using AiMessagingCore.Models;

namespace AiMessagingCore.Abstractions;

/// <summary>
/// Serializes and deserializes provider-neutral chat context.
/// </summary>
public interface IChatSerializer
{
    string Serialize(IReadOnlyList<ChatMessage> messages);

    IReadOnlyList<ChatMessage> Deserialize(string json);
}
