using LocalRAG;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// Drives the real EERagPipeline JSON interface (store / search / retrieve) against a real
/// database. No model is involved: the test plays the calling AI by choosing ids itself.
/// Proves what a search shows, what a retrieve returns, and what is left in the audit.
/// Gated on the embedding model like the other integration tests.
/// </summary>
[Trait("Category", "Integration")]
public class EERagPipelineHistoryTests : IAsyncLifetime
{
    private const string Kube = "Kubernetes orchestrates containers across clusters.";
    private const string Pg = "PostgreSQL is a relational database with indexing.";
    private const string Git = "Git tracks source code changes with commits.";
    private const string Question = "How do containers get orchestrated?";

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

    // ── Helpers: the JSON interface, as a calling AI would use it ───────────

    private async Task<JObject> Call(object request, DcsContextAssembler? dcs = null) =>
        JObject.Parse(await EERagPipeline.HandleAsync(
            Newtonsoft.Json.JsonConvert.SerializeObject(request), _db!, dcs));

    private Task<JObject> Search(string query = Question, int topK = 5, bool more = false,
        DcsContextAssembler? dcs = null) =>
        Call(new { op = "search", requestId = "cur", query, topK, more }, dcs);

    private Task<JObject> Retrieve(IEnumerable<int> ids, bool silent = false) =>
        Call(new { op = "retrieve", requestId = "cur", ids, silent });

    private static List<int> Ids(JObject search) =>
        search["candidates"]!.Select(c => (int)c["id"]!).ToList();

    /// <summary>The id of the candidate whose summary mentions <paramref name="word"/>.</summary>
    private static int IdOf(JObject search, string word)
    {
        var c = search["candidates"]!.FirstOrDefault(c => ((string)c["summary"]!).Contains(word));
        Assert.True(c != null, $"no candidate mentioning '{word}' in the header:\n{search["header"]}");
        return (int)c!["id"]!;
    }

    private async Task<JObject> MetaOf(string requestId) =>
        JObject.Parse((await _db!.GetFeedbackDataByRequestIdAsync(requestId))?.MetaData ?? "{}");

    // ── Tests ───────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Search_ShowsEverySummary_AndNoFullText_ThenRetrieveReturnsOnlyTheChosenEntry()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        var search = await Search();

        // Phase 1: every summary, behind the untrusted-data notice, in the header text.
        var header = (string)search["header"]!;
        Assert.Contains(EERagPipeline.UntrustedNotice, header);
        Assert.Contains("Kubernetes", header);
        Assert.Contains("PostgreSQL", header);
        Assert.Contains("Git tracks", header);
        Assert.Contains("respond with EXACTLY: MORE", header);
        Assert.True((bool)search["moreAvailable"]!);

        // Phase 2: only the chosen entry, in full; no summaries, no other entries.
        var kubeId = IdOf(search, "Kubernetes");
        var got = await Retrieve([kubeId]);
        var context = (string)got["context"]!;
        Assert.Contains(Kube, context);
        Assert.Contains("Retrieved Context", context);
        Assert.Contains(EERagPipeline.UntrustedNotice, context);
        Assert.DoesNotContain("Potentially Relevant Context", context);
        Assert.DoesNotContain("PostgreSQL", context);
        Assert.DoesNotContain("Git tracks", context);

        Assert.Equal(new[] { kubeId }, got["electedIds"]!.Select(t => (int)t));
        var hash = (string)got["hashes"]![0]!;
        Assert.Equal(8, hash.Length);
        Assert.Contains($"[RAG:{hash}]", (string)got["tags"]!);
        Assert.Equal(kubeId, (int)got["chunks"]![0]!["id"]!);
        Assert.Equal(hash, (string)got["chunks"]![0]!["hash"]!);
        Assert.Contains(Kube, (string)got["chunks"]![0]!["request"]!);
    }

    [SkippableFact]
    public async Task Retrieve_AcceptsTheCallersRawRetrieveReply()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var search = await Search();
        var kubeId = IdOf(search, "Kubernetes");

        var got = await Call(new { op = "retrieve", requestId = "cur", reply = $"RETRIEVE {kubeId}" });

        Assert.Equal(new[] { kubeId }, got["electedIds"]!.Select(t => (int)t));
        Assert.Contains(Kube, (string)got["context"]!);
    }

    [SkippableFact]
    public async Task NoRetrieval_ReturnsNothing_AndLogsAnsweredDirectly()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        await Search();

        var got = await Retrieve([]);

        Assert.Empty(got["electedIds"]!);
        Assert.Empty(got["chunks"]!);
        Assert.Equal("", (string)got["context"]!);
        Assert.Null(got["note"]);
        Assert.Equal("answered-directly", (string)(await MetaOf("cur"))["RagElectionLog"]!["Outcome"]!);
    }

    [SkippableFact]
    public async Task RejectedElection_ServesNothing_AndTellsTheCallerToAnswerDirectly()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        await Search();

        // An id that was never offered must be refused, however it was asked for.
        var got = await Retrieve([987654]);

        Assert.Empty(got["electedIds"]!);
        Assert.Equal(new[] { 987654 }, got["rejectedIds"]!.Select(t => (int)t));
        Assert.Empty(got["chunks"]!);
        Assert.Contains("Answer directly", (string)got["note"]!);
    }

    [SkippableFact]
    public async Task Retrieve_RefusesEntriesThatWereNeverSurfacedForThatRequest()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        // No search yet: nothing has been surfaced for "cur", so nothing may be served.
        var kubeId = (await _db!.GetFeedbackDataByRequestIdAsync("kube"))!.Id;

        var got = await Retrieve([kubeId]);

        Assert.Empty(got["electedIds"]!);
        Assert.Equal(new[] { kubeId }, got["rejectedIds"]!.Select(t => (int)t));
    }

    [SkippableFact]
    public async Task BadRequests_ReturnAnErrorObject()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        Assert.NotNull((await Call(new { op = "nope" }))["error"]);
        Assert.NotNull((await Call(new { op = "search", requestId = "cur" }))["error"]);          // no query
        Assert.NotNull((await Call(new { op = "search", requestId = "missing", query = "x" }))["error"]);
        Assert.NotNull((await Call(new { op = "retrieve", requestId = "cur" }))["error"]);        // no ids/reply
        Assert.NotNull(JObject.Parse(await EERagPipeline.HandleAsync("{not json", _db!))["error"]);
    }

    [SkippableFact]
    public async Task Store_AddsAnEntryThatSearchCanFind()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        var stored = await Call(new
        {
            op = "store", requestId = "mem1", request = "Redis is an in-memory key value store.",
            response = "It is often used as a cache.", embed = true
        });
        Assert.True((bool)stored["stored"]!);

        var search = await Search("What is an in-memory key value store?");
        Assert.Contains(search["candidates"]!, c => (string)c["requestId"]! == "mem1");
    }

    // ── MORE: the next set of candidates, once ───────────────────────────────

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
    public async Task More_ShowsTheNextSet_ThenElectsFromIt_OnlyOnce()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        var first = await Search(topK: 2);
        Assert.Equal(2, Ids(first).Count);
        Assert.Contains("respond with EXACTLY: MORE", (string)first["header"]!);

        // The same two plus the next two; no second MORE offered.
        var more = await Search(topK: 2, more: true);
        var ids = Ids(more);
        Assert.Equal(4, ids.Count);
        Assert.Contains("Further candidates have been added below.", (string)more["header"]!);
        Assert.DoesNotContain("respond with EXACTLY: MORE", (string)more["header"]!);
        Assert.False((bool)more["moreAvailable"]!);
        Assert.Equal(2, more["candidates"]!.Count(c => (int)c["batch"]! == 1));
        Assert.Equal(2, more["candidates"]!.Count(c => (int)c["batch"]! == 2));

        // An entry from the added set can be elected.
        var added = more["candidates"]!.First(c => (int)c["batch"]! == 2)["id"]!;
        var got = await Retrieve([(int)added]);
        Assert.Equal(new[] { (int)added }, got["electedIds"]!.Select(t => (int)t));

        // The audit lists all four surfaced ids, including the added ones.
        var surfaced = (await MetaOf("cur"))["RagCandidatesSurfaced"]!.Select(t => (int)t).ToList();
        Assert.Equal(ids.OrderBy(x => x), surfaced.OrderBy(x => x));

        // A second MORE for the same request is refused.
        Assert.NotNull((await Search(topK: 2, more: true))["error"]);

        // A reply of MORE is not an election; it points back at search.
        Assert.NotNull((await Call(new { op = "retrieve", requestId = "cur", reply = "MORE" }))["error"]);

        // A plain search afterwards is normal again and offers MORE.
        var again = await Search("What does git do?", topK: 2);
        Assert.Equal(2, Ids(again).Count);
        Assert.True((bool)again["moreAvailable"]!);
    }

    // ── Election log: enough for a reviewer to judge the choice ──────────────

    [SkippableFact]
    public async Task ElectionLog_RecordsProposedInFull_Reply_Choice_AndInjection()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        await _db!.AddRequestToEmbeddingDatabaseAsync("long", string.Concat(Enumerable.Repeat("alpha beta ", 1000)), embed: true);
        var search = await Search();
        var kubeId = IdOf(search, "Kubernetes");

        await Call(new { op = "retrieve", requestId = "cur", reply = $"RETRIEVE {kubeId}" });

        var log = (await MetaOf("cur"))["RagElectionLog"]!;
        Assert.Equal("elected", (string)log["Outcome"]!);
        Assert.Equal(Question, (string)log["UserMessage"]!);
        Assert.Equal("full", (string)log["Injected"]!);
        Assert.True((int)log["InjectedChars"]! > Kube.Length);
        Assert.Equal($"RETRIEVE {kubeId}", (string)log["ModelReplies"]![0]!);
        Assert.Equal(kubeId, (int)log["ElectedIds"]![0]!);

        // Every proposed entry is there with its whole text, not just the summary that was shown.
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
    }

    [SkippableFact]
    public async Task ElectionLog_RecordsRejectedOutcome()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        await Search();

        await Call(new { op = "retrieve", requestId = "cur", reply = "RETRIEVE 987654" });

        var log = (await MetaOf("cur"))["RagElectionLog"]!;
        Assert.Equal("rejected-retry", (string)log["Outcome"]!);
        Assert.Equal(new[] { 987654 }, log["RejectedIds"]!.Select(t => (int)t));
        Assert.Equal("none", (string)log["Injected"]!);
        Assert.Equal("RETRIEVE 987654", (string)log["ModelReplies"]![0]!);
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

        var plain = await Search();
        var withDcs = await Search(dcs: dcs);

        // Same candidates as without DCS; only the order may differ.
        Assert.Equal(Ids(plain).OrderBy(x => x), Ids(withDcs).OrderBy(x => x));

        // Nothing from DCS itself reaches the caller.
        Assert.DoesNotContain("GHOST-SECRET", withDcs.ToString());

        // The one boosted candidate led the list.
        var pg = withDcs["candidates"]!.Single(c => (string)c["requestId"]! == "pg");
        Assert.Equal((int)pg["id"]!, Ids(withDcs)[0]);
        Assert.True((bool)pg["dcsBoosted"]!);
        Assert.Single(withDcs["candidates"]!, c => (bool)c["dcsBoosted"]!);
        Assert.Equal(thread.DcnId, (int)withDcs["dcsQueryDcnId"]!);

        // Electing something else does not bring the boosted entry along.
        var got = await Retrieve([IdOf(withDcs, "Kubernetes")]);
        Assert.DoesNotContain(Pg, (string)got["context"]!);
        Assert.Contains(Kube, (string)got["context"]!);

        // Querying left DCS state exactly as it was.
        Assert.Equal(dcnCount, dcs.DcnStore.Count);
        Assert.Equal(msgCount, dcs.MessageStore.Count);
        Assert.Equal(stamp, thread.LastReferencedUtc);
    }

    [SkippableFact]
    public async Task Dcs_AbsentOrUnmatched_ChangesNothing()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");

        var none = await Search();
        Assert.Equal(JTokenType.Null, none["dcsQueryDcnId"]!.Type);
        Assert.DoesNotContain(none["candidates"]!, c => (bool)c["dcsBoosted"]!);

        var empty = new DcsContextAssembler();
        var r = await Search(dcs: empty);
        Assert.DoesNotContain(r["candidates"]!, c => (bool)c["dcsBoosted"]!);
        Assert.Equal(Ids(none), Ids(r));                // identical order too
        Assert.Empty(empty.DcnStore);                   // the query created nothing
    }

    [SkippableFact]
    public async Task Audit_RecordsSurfacedAndElected_OnTheCurrentRow()
    {
        Skip.If(_db == null, "BERT model not configured. Set BERT_MODEL_PATH.");
        var search = await Search();

        var got = await Retrieve([IdOf(search, "Kubernetes")]);

        var meta = (await _db!.GetFeedbackDataByRequestIdAsync("cur"))?.MetaData ?? "";
        Assert.Contains("RagCandidatesSurfaced", meta);
        Assert.Contains("RagEntriesElected", meta);
        Assert.Contains((string)got["hashes"]![0]!, meta);
    }
}
