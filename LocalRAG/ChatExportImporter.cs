using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LocalRAG
{
    /// <summary>One user→assistant exchange read from a ContextMigrate export; becomes one database row.</summary>
    public sealed record ImportedTurn(
        string RequestId,
        string Provider,
        string ChatId,
        string ChatTitle,
        string Request,
        string? Response,
        int UserIndex,
        int? AssistantIndex,
        DateTimeOffset? Timestamp);

    public sealed record ChatImportResult(int Messages, int Turns, int Added, int Skipped, int Failed);

    /// <summary>
    /// Imports a chat export written by ContextMigrate (<c>messages.jsonl</c>, or a per-chat
    /// <c>&lt;provider&gt;/&lt;id&gt;.json</c>) into the embedding database. Each user message and the assistant
    /// reply that follows it become one row (Request / TextResponse); the row's RequestID is the user message's
    /// ContextMigrate id (<c>provider:chatId:index</c>), so re-importing the same export adds nothing.
    /// Provider, chat id/title and the original timestamps are kept in the row's MetaData as JSON.
    /// </summary>
    public static class ChatExportImporter
    {
        /// <param name="path">An export folder (its <c>messages.jsonl</c> is read; failing that every chat file below it),
        /// a <c>.jsonl</c> file, or one chat <c>.json</c> file.</param>
        /// <param name="embed">Embed the imported rows (needed for semantic search).</param>
        public static async Task<ChatImportResult> ImportAsync(
            EmbeddingDatabaseNew db, string path, bool embed = true,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var messages = ReadMessages(path).ToList();
            var turns = BuildTurns(messages);
            int added = 0, skipped = 0, failed = 0;

            for (int i = 0; i < turns.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var t = turns[i];
                try
                {
                    if (await db.GetFeedbackDataByRequestIdAsync(t.RequestId) != null) { skipped++; continue; }

                    await db.AddRequestToEmbeddingDatabaseAsync(t.RequestId, t.Request, embed);
                    await db.UpdateDataInEmbeddingDatabaseAsync(new FeedbackDatabaseValues
                    {
                        RequestID = t.RequestId,
                        TextResponse = t.Response,
                        MetaData = MetaDataFor(t),
                        Embed = embed,
                        EmbedTextResponse = embed,
                        EmbedTextResponseList = embed
                    });
                    added++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    progress?.Report($"Failed {t.RequestId}: {ex.Message}");
                }
                if ((i + 1) % 50 == 0 || i + 1 == turns.Count)
                    progress?.Report($"[{i + 1}/{turns.Count}] added {added}, skipped {skipped}, failed {failed}");
            }
            return new ChatImportResult(messages.Count, turns.Count, added, skipped, failed);
        }

        /// <summary>Groups messages by chat, in message order, into user→assistant turns. Consecutive assistant
        /// messages join the same response; consecutive user messages are separate turns; an assistant message with
        /// no user message before it is stored as its own turn with the request "Assistant: …".</summary>
        public static List<ImportedTurn> BuildTurns(IEnumerable<ExportedMessage> messages)
        {
            var turns = new List<ImportedTurn>();
            foreach (var chat in messages.Where(m => !string.IsNullOrWhiteSpace(m.Content))
                                         .GroupBy(m => (m.Provider, m.ChatId)))
            {
                ExportedMessage? user = null;
                var reply = new List<ExportedMessage>();

                void Flush()
                {
                    if (user == null) return;
                    turns.Add(new ImportedTurn(
                        user.Id, user.Provider, user.ChatId, user.ChatTitle, user.Content,
                        reply.Count == 0 ? null : string.Join("\n\n", reply.Select(r => r.Content)),
                        user.Index, reply.Count == 0 ? null : reply[^1].Index, user.Timestamp));
                    user = null;
                    reply.Clear();
                }

                foreach (var m in chat.OrderBy(m => m.Index))
                {
                    if (m.IsUser) { Flush(); user = m; }
                    else if (user != null) reply.Add(m);
                    else
                        turns.Add(new ImportedTurn(
                            m.Id, m.Provider, m.ChatId, m.ChatTitle, "Assistant: " + m.Content, null,
                            m.Index, null, m.Timestamp));
                }
                Flush();
            }
            return turns;
        }

        public static IEnumerable<ExportedMessage> ReadMessages(string path)
        {
            if (Directory.Exists(path))
            {
                var jsonl = Path.Combine(path, "messages.jsonl");
                if (File.Exists(jsonl)) return ParseJsonLines(File.ReadLines(jsonl));
                return Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).Equals("index.json", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(ReadChatFile);
            }
            if (!File.Exists(path)) throw new FileNotFoundException("Chat export not found.", path);
            return path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                ? ParseJsonLines(File.ReadLines(path))
                : ReadChatFile(path);
        }

        public static IEnumerable<ExportedMessage> ParseJsonLines(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JObject o;
                try { o = JObject.Parse(line); } catch (JsonException) { continue; }
                var provider = (string?)o["provider"] ?? "";
                var chatId = (string?)o["chatId"] ?? "";
                var index = (int?)o["index"] ?? 0;
                yield return new ExportedMessage(
                    (string?)o["id"] ?? $"{provider}:{chatId}:{index}", provider, chatId,
                    (string?)o["chatTitle"] ?? "", index, IsUserRole((string?)o["role"]),
                    (string?)o["content"] ?? "", Time(o["timestamp"]));
            }
        }

        private static IEnumerable<ExportedMessage> ReadChatFile(string file)
        {
            JObject o;
            try { o = JObject.Parse(File.ReadAllText(file)); } catch (JsonException) { yield break; }
            if (o["messages"] is not JArray arr) yield break; // not a ContextMigrate chat file
            var provider = (string?)o["provider"] ?? "";
            var chatId = (string?)o["id"] ?? Path.GetFileNameWithoutExtension(file);
            var title = (string?)o["title"] ?? "";
            foreach (var m in arr)
            {
                var index = (int?)m["index"] ?? 0;
                yield return new ExportedMessage(
                    $"{provider}:{chatId}:{index}", provider, chatId, title, index,
                    IsUserRole((string?)m["role"]), (string?)m["content"] ?? "", Time(m["timestamp"]));
            }
        }

        // ContextMigrate writes the Role enum camelCase ("user"/"assistant"); accept the numeric form too (0 = User).
        private static bool IsUserRole(string? role) =>
            string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) || role == "0";

        private static DateTimeOffset? Time(JToken? t) =>
            t != null && t.Type != JTokenType.Null && DateTimeOffset.TryParse(t.ToString(), out var d) ? d : null;

        private static string MetaDataFor(ImportedTurn t) => JsonConvert.SerializeObject(new
        {
            source = "ContextMigrate",
            provider = t.Provider,
            chatId = t.ChatId,
            chatTitle = t.ChatTitle,
            userIndex = t.UserIndex,
            assistantIndex = t.AssistantIndex,
            timestamp = t.Timestamp
        });
    }

    /// <summary>One message as ContextMigrate exported it.</summary>
    public sealed record ExportedMessage(
        string Id, string Provider, string ChatId, string ChatTitle,
        int Index, bool IsUser, string Content, DateTimeOffset? Timestamp);
}
