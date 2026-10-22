using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;
using LocalRAG;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// Drives the real EERagPipeline against a real database and the real ChatSessionBase
/// (history, TrimLastTurn, transient injection) with only the model's replies scripted.
/// Proves what the model is shown on each call and what is left in the saved history.
/// Gated on the embedding model like the other integration tests.
/// </summary>
[Trait("Category", "Integration")]
public class EERagPipelineHistoryTests : IAsyncLifetime
{
    private const string Kube = "Kubernetes orchestrates containers across clusters.";
    private const string Pg = "PostgreSQL is a relational database with indexing.";
    private const string Git = "Git tracks source code changes with commits.";
    private const string Question = "How do containers get orchestrated?";
    private const string Seed = "You are a test assistant.";

    private string _dbPath = "";
    private EmbeddingDatabaseNew? _db;

    public async Task InitializeAsync()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        if (!File.Exists(modelPath)) return; // tests skip themselves

        _dbPath = Path.Combine(Path.GetTempPath(), $"eerag_hist_{Guid.NewGuid():N}.db");
        _db = new EmbeddingDatabaseNew(new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = _dbPath,
            IndexChunkEmbeddings = false
        });
        await _db.EnsureReadyAsync();
        await _db.AddRequestToEmbeddingDatabaseAsync("kube", Kube, embed: true);
        await _db.AddRequestToEmbeddingDatabaseAsync("pg", Pg, embed: true);
        await _db.AddRequestToEmbeddingDatabaseAsync("git", Git, embed: true);
        // The row the pipeline writes its audit into; unrelated text so it isn't elected.
        await _db.AddRequestToEmbeddingDatabaseAsync("cur", "Placeholder for the current turn.", embed: true);
    }

    public async Task DisposeAsync()
    {
        if (_db != null) await _db.DisposeAsync();
        for (int i = 0; i < 3 && _dbPath != "" && File.Exists(_dbPath); i++)
        {
            try { File.Delete(_dbPath); }
            catch (IOException) { await Task.Delay(100); }
        }
    }

    // ── Fake session: real history logic, scripted model ────────────────────

    private sealed class ScriptedSession : ChatSessionBase
    {
        private readonly Func<int, IReadOnlyList<ChatMessage>, string> _reply;
        public List<List<ChatMessage>> Calls { get; } = new();

        public ScriptedSession(Func<int, IReadOnlyList<ChatMessage>, string> reply)
            : base(new ChatSessionOptions { ProviderName = "fake", Model = "fake", SystemMessage = Seed })
            => _reply = reply;

        protected override async IAsyncEnumerable<ChatMessage> ExecuteStreamAsync(
            IReadOnlyList<ChatMessage> messages, RequestOverrides? overrides,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var snapshot = messages.ToList();
            Calls.Add(snapshot);
            await Task.Yield();
            yield return new ChatMessage(ChatRole.Assistant, _reply(Calls.Count - 1, snapshot), DateTimeOffset.UtcNow);
        }
    }

    private static string Flat(IEnumerable<ChatMessage> msgs) =>
        string.Join("\n", msgs.Select(m => $"{m.Role}: {m.Content}"));

    /// <summary>Elects the candidate whose summary line mentions <paramref name="word"/>.</summary>
    private static string Elect(IReadOnlyList<ChatMessage> msgs, string word)
    {
        var header = msgs.First(m => m.Role == ChatRole.System && m.Content.Contains("[ID:")).Content;
        var m = Regex.Match(header, @"\[ID:(\d+)\]\s*" + Regex.Escape(word));
        Assert.True(m.Success, $"no candidate mentioning '{word}' in the header:\n{header}");
        return $"RETRIEVE {m.Groups[1].Value}";
    }

    private Task<EERagResult> Run(ScriptedSession s, string message = Question) =>
        EERagPipeline.RunPipelineAsync(message, "cur", s, _db!, topK: 5);

    // ── Tests ───────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Elected_SecondCallSeesOnlyChosenEntry_AndHistoryKeepsOnlyCleanTurn()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var s = new ScriptedSession((i, msgs) => i == 0 ? Elect(msgs, "Kubernetes") : "Use Kubernetes.");

        var result = await Run(s);

        Assert.Equal(2, s.Calls.Count);

        // Call 1: the model is shown every summary, in a transient system message, before the question.
        var c1 = s.Calls[0];
        Assert.Equal(ChatRole.User, c1[^1].Role);
        Assert.Equal(Question, c1[^1].Content);
        var header = c1[^2];
        Assert.Equal(ChatRole.System, header.Role);
        Assert.Contains(EERagPipeline.UntrustedNotice, header.Content);
        Assert.Contains("Kubernetes", header.Content);
        Assert.Contains("PostgreSQL", header.Content);
        Assert.Contains("Git tracks", header.Content);

        // Call 2: only the chosen entry, in full; no summaries, no selection exchange, one user turn.
        var c2 = s.Calls[1];
        var all2 = Flat(c2);
        Assert.Equal(ChatRole.User, c2[^1].Role);
        Assert.Equal(ChatRole.System, c2[^2].Role);
        Assert.Contains(Kube, c2[^2].Content);
        Assert.Contains("Retrieved Context", c2[^2].Content);
        Assert.DoesNotContain("Potentially Relevant Context", all2);
        Assert.DoesNotContain("PostgreSQL", all2);
        Assert.DoesNotContain("Git tracks", all2);
        Assert.DoesNotContain("RETRIEVE", all2);
        Assert.Single(c2, m => m.Role == ChatRole.User);
        Assert.DoesNotContain(c2, m => m.Role == ChatRole.Assistant);

        // Saved history: seed + the user's question + the final answer, nothing else.
        Assert.Collection(s.Messages,
            m => { Assert.Equal(ChatRole.System, m.Role); Assert.Equal(Seed, m.Content); },
            m => { Assert.Equal(ChatRole.User, m.Role); Assert.Equal(Question, m.Content); },
            m => { Assert.Equal(ChatRole.Assistant, m.Role); Assert.Equal("Use Kubernetes.", m.Content); });

        Assert.Single(result.ElectedIds);
        Assert.Single(result.ElectedHashes);
        Assert.StartsWith("Use Kubernetes.", result.FinalResponse);
        Assert.Contains($"[RAG:{result.ElectedHashes[0]}]", result.FinalResponse);
    }

    [SkippableFact]
    public async Task NoRetrieval_FirstReplyIsTheAnswer_AndNothingTransientIsSaved()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var s = new ScriptedSession((_, _) => "Answering directly.");

        var result = await Run(s);

        Assert.Single(s.Calls);
        Assert.Equal("Answering directly.", result.FinalResponse);
        Assert.Empty(result.ElectedIds);
        Assert.Equal(3, s.Messages.Count); // seed, question, answer
        Assert.DoesNotContain("Potentially Relevant Context", Flat(s.Messages));
        Assert.DoesNotContain("Kubernetes", Flat(s.Messages));
    }

    [SkippableFact]
    public async Task NextTurn_DoesNotInheritEarlierRetrievedContent()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var s = new ScriptedSession((i, msgs) => i == 0 ? Elect(msgs, "Kubernetes") : "ok");

        await Run(s);                                  // calls 0 and 1
        await Run(s, "What does git do?");             // call 2: no election

        var turn2 = s.Calls[2];
        Assert.DoesNotContain("Retrieved Context", Flat(turn2));
        // (The header's own instructions say RETRIEVE, so only look at the conversation turns.)
        Assert.DoesNotContain("RETRIEVE", Flat(turn2.Where(m => m.Role != ChatRole.System)));
        // The earlier turn is there as a plain user/assistant pair, and the only system
        // messages are the seed and this turn's candidate header.
        Assert.Contains(turn2, m => m.Role == ChatRole.User && m.Content == Question);
        Assert.Equal(2, turn2.Count(m => m.Role == ChatRole.System));
        Assert.Equal(5, s.Messages.Count);             // seed + 2 clean turns
    }

    [SkippableFact]
    public async Task RejectedElection_IsNotReturnedAsTheAnswer_OrKeptInHistory()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        // The model asks for an id that was never offered; the pipeline must refuse it
        // and still give a real answer.
        var s = new ScriptedSession((i, _) => i == 0 ? "RETRIEVE 987654" : "Direct answer.");

        var result = await Run(s);

        Assert.Empty(result.ElectedIds);
        Assert.Equal("Direct answer.", result.FinalResponse);
        Assert.DoesNotContain("RETRIEVE", Flat(s.Messages));
        Assert.DoesNotContain("Potentially Relevant Context", Flat(s.Calls[^1]));
        Assert.Equal(3, s.Messages.Count);
    }

    [SkippableFact]
    public async Task Audit_RecordsSurfacedAndElected_OnTheCurrentRow()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var s = new ScriptedSession((i, msgs) => i == 0 ? Elect(msgs, "Kubernetes") : "done");

        var result = await Run(s);

        var meta = (await _db!.GetFeedbackDataByRequestIdAsync("cur"))?.MetaData ?? "";
        Assert.Contains("RagCandidatesSurfaced", meta);
        Assert.Contains("RagEntriesElected", meta);
        Assert.Contains(result.ElectedHashes[0], meta);
    }
}
