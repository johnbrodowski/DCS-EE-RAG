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

    private Task<EERagResult> Run(ScriptedSession s, string message = Question, int topK = 5,
        DcsContextAssembler? dcs = null) =>
        EERagPipeline.RunPipelineAsync(message, "cur", s, _db!, topK: topK, dcsAssembler: dcs);

    private static List<int> HeaderIds(IReadOnlyList<ChatMessage> call) =>
        Regex.Matches(call.First(m => m.Role == ChatRole.System && m.Content.Contains("[ID:")).Content, @"\[ID:(\d+)\]")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();

    private async Task<Newtonsoft.Json.Linq.JObject> MetaOf(string requestId) =>
        Newtonsoft.Json.Linq.JObject.Parse((await _db!.GetFeedbackDataByRequestIdAsync(requestId))?.MetaData ?? "{}");

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

    // ── MORE: the next set of candidates, once, controlled by the system ─────

    [Theory]
    [InlineData("MORE", true)]
    [InlineData("more.", true)]
    [InlineData("  MORE \n", true)]
    [InlineData("MORE 50", false)]
    [InlineData("MORE topK=100", false)]
    [InlineData("More information is available.", false)]
    [InlineData("RETRIEVE 3", false)]
    [InlineData("", false)]
    public void IsMoreRequest_AcceptsOnlyTheBareWord(string reply, bool expected) =>
        Assert.Equal(expected, EERagPipeline.IsMoreRequest(reply));

    [SkippableFact]
    public async Task More_ShowsTheNextSet_ThenElectsFromIt_AndLeavesNoTrace()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        ScriptedSession? self = null;
        var s = new ScriptedSession((i, msgs) =>
        {
            if (i == 0) return "MORE";
            if (i == 1) // elect something that was NOT in the first set
            {
                var first = HeaderIds(self!.Calls[0]);
                var added = HeaderIds(msgs).First(id => !first.Contains(id));
                return $"RETRIEVE {added}";
            }
            return i == 2 ? "Answer from the extra set." : "Plain answer.";
        });
        self = s;

        var result = await Run(s, topK: 2);

        Assert.Equal(3, s.Calls.Count);

        // Call 0: two candidates and the offer of MORE.
        Assert.Equal(2, HeaderIds(s.Calls[0]).Count);
        Assert.Contains("respond with EXACTLY: MORE", Flat(s.Calls[0]));

        // Call 1: the same two plus the next two; no second MORE offered; the bare MORE reply is gone.
        var h1 = HeaderIds(s.Calls[1]);
        Assert.Equal(4, h1.Count);
        Assert.Contains("Further candidates have been added below.", Flat(s.Calls[1]));
        Assert.DoesNotContain("respond with EXACTLY: MORE", Flat(s.Calls[1]));
        Assert.DoesNotContain(s.Calls[1], m => m.Role == ChatRole.Assistant);
        Assert.Single(s.Calls[1], m => m.Role == ChatRole.User);

        // Call 2: just the elected entry; history is a clean pair.
        Assert.Equal(3, s.Messages.Count);
        Assert.DoesNotContain("MORE", Flat(s.Messages.Where(m => m.Role != ChatRole.System)));
        Assert.True(result.MoreRequested);
        Assert.Equal(2, result.Election.MoreAddedCount);
        Assert.Equal(2, result.Election.Proposed.Count(p => p.Batch == 1));
        Assert.Equal(2, result.Election.Proposed.Count(p => p.Batch == 2));
        Assert.All(result.ElectedIds, id => Assert.Contains(result.Election.Proposed, p => p.Id == id && p.Batch == 2));

        // The audit lists all four surfaced ids, including the added ones.
        var surfaced = (await MetaOf("cur"))["RagCandidatesSurfaced"]!.Select(t => (int)t).ToList();
        Assert.Equal(h1.OrderBy(x => x), surfaced.OrderBy(x => x));

        // Back to normal right after: the next turn gets a normal-size list and MORE is on offer again.
        await Run(s, "What does git do?", topK: 2);
        Assert.Equal(2, HeaderIds(s.Calls[3]).Count);
        Assert.Contains("respond with EXACTLY: MORE", Flat(s.Calls[3]));
    }

    [SkippableFact]
    public async Task More_CannotBeParameterised_OrUsedTwice()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        // "MORE 50" is not a request: no widening happens, one call only.
        var a = new ScriptedSession((_, _) => "MORE 50");
        var ra = await Run(a, topK: 2);
        Assert.Single(a.Calls);
        Assert.False(ra.MoreRequested);
        Assert.Equal(2, HeaderIds(a.Calls[0]).Count);

        // A second bare MORE is refused and the model is asked for a direct answer.
        var b = new ScriptedSession((i, _) => i < 2 ? "MORE" : "Direct answer.");
        var rb = await Run(b, topK: 2);
        Assert.Equal(3, b.Calls.Count);
        Assert.Equal("Direct answer.", rb.FinalResponse);
        Assert.Equal("rejected-retry", rb.Election.Outcome);
        Assert.Equal(3, b.Messages.Count);
        Assert.DoesNotContain("MORE", Flat(b.Messages.Where(m => m.Role != ChatRole.System)));
        Assert.DoesNotContain("Potentially Relevant Context", Flat(b.Calls[2]));
    }

    // ── Election log: enough for a reviewer to judge the choice ──────────────

    [SkippableFact]
    public async Task ElectionLog_RecordsProposedInFull_Replies_Choice_AndInjection()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        await _db!.AddRequestToEmbeddingDatabaseAsync("long", string.Concat(Enumerable.Repeat("alpha beta ", 1000)), embed: true);
        var s = new ScriptedSession((i, msgs) => i == 0 ? Elect(msgs, "Kubernetes") : "Use Kubernetes.");

        var result = await Run(s);

        var log = (await MetaOf("cur"))["RagElectionLog"]!;
        Assert.Equal("elected", (string)log["Outcome"]!);
        Assert.Equal(Question, (string)log["UserMessage"]!);
        Assert.Equal("full", (string)log["Injected"]!);
        Assert.True((int)log["InjectedChars"]! > Kube.Length);
        Assert.StartsWith("RETRIEVE ", (string)log["ModelReplies"]![0]!);
        Assert.Equal(result.ElectedIds.Single(), (int)log["ElectedIds"]![0]!);

        // Every proposed entry is there with its whole text, not just the summary the model saw.
        var proposed = log["Proposed"]!.ToList();
        Assert.True(proposed.Count >= 4);
        var kube = proposed.Single(p => ((string)p["FullText"]!).Contains(Kube));
        Assert.Contains("Kubernetes", (string)kube["SummaryShown"]!);
        Assert.Contains("Request: " + Kube, (string)kube["FullText"]!);
        Assert.Contains(proposed, p => ((string)p["FullText"]!).Contains(Pg));
        Assert.Contains(proposed, p => ((string)p["FullText"]!).Contains(Git));

        // Long entries are capped in the log and flagged, not silently cut.
        var big = proposed.Single(p => (string)p["RequestID"]! == "long");
        Assert.True((bool)big["FullTextTruncated"]!);
        Assert.Equal(EERagPipeline.TraceTextCap, ((string)big["FullText"]!).Length);
        Assert.Equal(result.Election.Proposed.Count, proposed.Count);
    }

    [SkippableFact]
    public async Task ElectionLog_RecordsRejectedAndDirectOutcomes()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        var rej = new ScriptedSession((i, _) => i == 0 ? "RETRIEVE 987654" : "Direct answer.");
        var r1 = await Run(rej);
        Assert.Equal("rejected-retry", r1.Election.Outcome);
        Assert.Equal(new[] { 987654 }, r1.Election.RejectedIds);
        Assert.Equal("none", r1.Election.Injected);
        Assert.Equal(new[] { "RETRIEVE 987654" }, r1.Election.ModelReplies);

        var direct = new ScriptedSession((_, _) => "Just answering.");
        var r2 = await Run(direct);
        Assert.Equal("answered-directly", r2.Election.Outcome);
        Assert.Empty(r2.Election.ElectedIds);
        Assert.Equal("answered-directly", (string)(await MetaOf("cur"))["RagElectionLog"]!["Outcome"]!);
    }

    // ── DCS continuity: reorders the search's own candidates, never injects ──

    [SkippableFact]
    public async Task Dcs_OnlyReordersSearchCandidates_AndInjectsNothingOfItsOwn()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        var key = DcsIntentClassifier.Classify(Question);
        var dcs = new DcsContextAssembler();
        var thread = dcs.FindOrCreateDcn(key);
        // One real row ("pg") that DCS considers part of the thread, registered first so the
        // 20-per-thread cap keeps it, then thirty highly relevant "ghost" messages that are
        // not in the search results at all.
        dcs.RegisterMessage(new DcsMessageRecord
        {
            MessageId = "pg", Key = new DcsKey { IntentIds = key.IntentIds, DomainIds = key.DomainIds, DcnIds = [thread.DcnId] },
            HardLinkedDcns = [thread.DcnId], Content = "GHOST-SECRET-pg"
        });
        for (int i = 0; i < 30; i++)
            dcs.RegisterMessage(new DcsMessageRecord
            {
                MessageId = $"ghost-{i}", Key = new DcsKey { IntentIds = key.IntentIds, DomainIds = key.DomainIds, DcnIds = [thread.DcnId] },
                HardLinkedDcns = [thread.DcnId], Content = $"GHOST-SECRET-{i}"
            });
        var stamp = thread.LastReferencedUtc;
        var dcnCount = dcs.DcnStore.Count;
        var msgCount = dcs.MessageStore.Count;

        var plain = new ScriptedSession((_, _) => "ok");
        await Run(plain);

        var withDcs = new ScriptedSession((i, msgs) => i == 0 ? Elect(msgs, "Kubernetes") : "Use Kubernetes.");
        var result = await Run(withDcs, dcs: dcs);

        // Same candidates as without DCS; only the order may differ.
        Assert.Equal(HeaderIds(plain.Calls[0]).OrderBy(x => x), HeaderIds(withDcs.Calls[0]).OrderBy(x => x));

        // Nothing from DCS itself reaches the model, on either call.
        Assert.DoesNotContain("GHOST-SECRET", Flat(withDcs.Calls.SelectMany(c => c)));

        // The one boosted candidate led the list; it was not injected because it was not elected.
        var pgId = result.Election.Proposed.Single(p => p.RequestID == "pg").Id;
        Assert.Equal(pgId, HeaderIds(withDcs.Calls[0])[0]);
        Assert.Equal(new[] { pgId }, result.Election.DcsBoostedIds);
        Assert.Equal(thread.DcnId, result.Election.DcsQueryDcnId);
        Assert.DoesNotContain(Pg, Flat(withDcs.Calls[1]));
        Assert.Contains(Kube, Flat(withDcs.Calls[1]));

        // Querying left DCS state exactly as it was.
        Assert.Equal(dcnCount, dcs.DcnStore.Count);
        Assert.Equal(msgCount, dcs.MessageStore.Count);
        Assert.Equal(stamp, thread.LastReferencedUtc);
    }

    [SkippableFact]
    public async Task Dcs_AbsentOrUnmatched_ChangesNothing()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        var s = new ScriptedSession((_, _) => "ok");
        var none = await Run(s);
        Assert.Null(none.Election.DcsQueryDcnId);
        Assert.Equal(0, none.Election.DcsRelevantCount);
        Assert.Empty(none.Election.DcsBoostedIds);

        var empty = new DcsContextAssembler();
        var s2 = new ScriptedSession((_, _) => "ok");
        var r = await Run(s2, dcs: empty);
        Assert.Empty(r.Election.DcsBoostedIds);
        Assert.Equal(HeaderIds(s.Calls[0]), HeaderIds(s2.Calls[0])); // identical order too
        Assert.Empty(empty.DcnStore);                                 // the query created nothing
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
