using LocalRAG;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>Parsing and turn-pairing of a ContextMigrate export (no model needed).</summary>
public class ChatExportImporterTests
{
    private static string Line(string chat, int i, string role, string content, string? ts = "2025-01-15T09:00:00+00:00") =>
        $"{{\"id\":\"claude:{chat}:{i}\",\"provider\":\"claude\",\"chatId\":\"{chat}\",\"chatTitle\":\"T-{chat}\"," +
        $"\"index\":{i},\"role\":\"{role}\",\"content\":\"{content}\",\"timestamp\":{(ts == null ? "null" : $"\"{ts}\"")}}}";

    [Fact]
    public void PairsUserWithFollowingAssistant_PerChat()
    {
        var lines = new[]
        {
            Line("a", 0, "user", "q1"), Line("a", 1, "assistant", "r1"),
            Line("a", 2, "user", "q2"), Line("a", 3, "assistant", "r2a"), Line("a", 4, "assistant", "r2b"),
            Line("b", 0, "user", "bq"), Line("b", 1, "assistant", "br"),
        };
        var turns = ChatExportImporter.BuildTurns(ChatExportImporter.ParseJsonLines(lines));

        Assert.Equal(3, turns.Count);
        Assert.Equal("claude:a:0", turns[0].RequestId);
        Assert.Equal("r1", turns[0].Response);
        Assert.Equal("r2a\n\nr2b", turns[1].Response);
        Assert.Equal(4, turns[1].AssistantIndex);
        Assert.Equal("T-b", turns[2].ChatTitle);
    }

    [Fact]
    public void LoneMessagesAndBlanksAreHandled()
    {
        var lines = new[]
        {
            "not json",
            Line("a", 0, "assistant", "orphan"),
            Line("a", 1, "user", "q1"),
            Line("a", 2, "user", "q2"), Line("a", 3, "assistant", "r2"),
            Line("a", 4, "user", "   "),
        };
        var turns = ChatExportImporter.BuildTurns(ChatExportImporter.ParseJsonLines(lines));

        Assert.Equal(3, turns.Count);
        Assert.Equal("Assistant: orphan", turns[0].Request);
        Assert.Null(turns[1].Response);       // q1 had no reply
        Assert.Equal("r2", turns[2].Response);
    }

    [Fact]
    public void ReadsExportFolderAndChatFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cmx_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "claude"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "claude", "c1.json"),
                "{\"provider\":\"claude\",\"id\":\"c1\",\"title\":\"Hello\",\"messages\":[" +
                "{\"role\":\"user\",\"content\":\"hi\",\"index\":0,\"timestamp\":\"2025-01-01T00:00:00Z\"}," +
                "{\"role\":\"assistant\",\"content\":\"hey\",\"index\":1}]}");
            File.WriteAllText(Path.Combine(dir, "index.json"), "[]");

            var viaFolder = ChatExportImporter.BuildTurns(ChatExportImporter.ReadMessages(dir));
            var viaFile = ChatExportImporter.BuildTurns(ChatExportImporter.ReadMessages(Path.Combine(dir, "claude", "c1.json")));

            Assert.Single(viaFolder);
            Assert.Equal("claude:c1:0", viaFolder[0].RequestId);
            Assert.Equal("hey", viaFile[0].Response);
            Assert.NotNull(viaFile[0].Timestamp);
        }
        finally { Directory.Delete(dir, true); }
    }
}
