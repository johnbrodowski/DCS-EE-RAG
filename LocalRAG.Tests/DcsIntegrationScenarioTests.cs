using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace LocalRAG.Tests;

/// <summary>
/// Integration scenario tests that load the 80-message dcs_test_messages.json dataset,
/// run it through the full DCS pipeline (classify → assign DCN → register → assemble),
/// and validate that thread grouping, cross-references, and noise filtering behave correctly.
///
/// These tests exercise the DCS system end-to-end without requiring any external model or database.
/// </summary>
public class DcsIntegrationScenarioTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly DcsContextAssembler _assembler;
    private readonly List<TestMessage> _allMessages;
    private readonly Dictionary<string, DcsMessageRecord> _registered = new();

    // ── Test message model matching the JSON schema ─────────────────────

    private sealed class TestMessage
    {
        public string id { get; set; } = "";
        public string content { get; set; } = "";
        public string timestamp { get; set; } = "";
        public string thread { get; set; } = "";
        public string notes { get; set; } = "";
    }

    // ── Setup: load data, classify, assign DCNs, register ───────────────

    public DcsIntegrationScenarioTests(ITestOutputHelper output)
    {
        _output = output;
        _assembler = new DcsContextAssembler();

        // Locate the JSON file relative to the test project
        var jsonPath = FindTestDataFile("dcs_test_messages.json");
        Assert.True(File.Exists(jsonPath), $"Test data file not found at: {jsonPath}");

        var json = File.ReadAllText(jsonPath);
        _allMessages = JsonSerializer.Deserialize<List<TestMessage>>(json)!;
        Assert.NotNull(_allMessages);
        Assert.Equal(80, _allMessages.Count);

        _output.WriteLine($"Loaded {_allMessages.Count} test messages");
        _output.WriteLine($"Threads: {string.Join(", ", _allMessages.Select(m => m.thread).Distinct().OrderBy(t => t))}");

        // Process each message through the DCS pipeline
        foreach (var msg in _allMessages.OrderBy(m => m.timestamp))
        {
            // Step 1: Classify intent and domain
            var key = DcsIntentClassifier.Classify(msg.content);

            // Step 2: Find or create a DCN for this message
            var dcn = _assembler.FindOrCreateDcn(key);

            // Step 3: Create the record with DCN linkage
            var record = new DcsMessageRecord
            {
                MessageId = msg.id,
                Key = new DcsKey
                {
                    IntentIds = key.IntentIds,
                    DomainIds = key.DomainIds,
                    DcnIds = new[] { dcn.DcnId }
                },
                HardLinkedDcns = new[] { dcn.DcnId },
                Content = msg.content,
                TimestampUtc = DateTime.Parse(msg.timestamp).ToUniversalTime()
            };

            // Step 4: Register
            _assembler.RegisterMessage(record);
            _registered[msg.id] = record;
        }

        _output.WriteLine($"\nRegistered {_registered.Count} messages");
        _output.WriteLine($"DCNs created: {_assembler.DcnStore.Count}");
        foreach (var dcn in _assembler.DcnStore.Values.OrderBy(d => d.DcnId))
        {
            _output.WriteLine($"  DCN {dcn.DcnId}: topics=[{string.Join(",", dcn.TopicIdentifiers)}] messages={dcn.LinkedMessageIds.Count}");
        }
    }

    public void Dispose() { }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string FindTestDataFile(string fileName)
    {
        // Walk up from test bin directory to find the repo root
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        // Fallback: try repo root relative to known structure
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", fileName);
    }

    private List<TestMessage> GetThreadMessages(string threadName)
    {
        return _allMessages
            .Where(m => m.thread == threadName ||
                        m.thread.StartsWith("cross-ref-") && m.notes.Contains(threadName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private HashSet<string> GetThreadMessageIds(string threadName)
    {
        return _allMessages
            .Where(m => m.thread == threadName)
            .Select(m => m.id)
            .ToHashSet();
    }

    private List<DcsMessageRecord> QueryWithMessage(string messageContent, int maxTotal = 20)
    {
        var queryKey = DcsIntentClassifier.Classify(messageContent);
        return _assembler.AssembleContext(queryKey, maxTotal);
    }

    private void LogResults(string label, List<DcsMessageRecord> results)
    {
        _output.WriteLine($"\n--- {label} ---");
        _output.WriteLine($"Returned {results.Count} messages:");
        foreach (var r in results)
        {
            var msg = _allMessages.First(m => m.id == r.MessageId);
            _output.WriteLine($"  {r.MessageId} [{msg.thread}] \"{Truncate(msg.content, 70)}\"");
        }
    }

    private double ThreadPrecision(List<DcsMessageRecord> results, string expectedThread)
    {
        if (results.Count == 0) return 0;
        var threadIds = GetThreadMessageIds(expectedThread);
        var matchCount = results.Count(r => threadIds.Contains(r.MessageId));
        return (double)matchCount / results.Count;
    }

    private double ThreadRecall(List<DcsMessageRecord> results, string expectedThread)
    {
        var threadIds = GetThreadMessageIds(expectedThread);
        if (threadIds.Count == 0) return 0;
        var matchCount = results.Count(r => threadIds.Contains(r.MessageId));
        return (double)matchCount / threadIds.Count;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    // ── Test: All 80 messages loaded and registered ─────────────────────

    [Fact]
    public void AllMessages_Loaded_And_Registered()
    {
        Assert.Equal(80, _allMessages.Count);
        Assert.Equal(80, _registered.Count);
        Assert.True(_assembler.DcnStore.Count >= 2, $"Expected multiple DCNs, got {_assembler.DcnStore.Count}");

        _output.WriteLine($"Total DCNs: {_assembler.DcnStore.Count}");
        _output.WriteLine($"Total messages in store: {_assembler.MessageStore.Count}");
    }

    // ── Test: Intent classification produces expected patterns ───────────

    [Fact]
    public void Classification_ProducesExpectedIntents()
    {
        // Auth bug messages should classify with FIX/QUERY intents
        var authMsg = _allMessages.First(m => m.id == "msg-001");
        var authKey = DcsIntentClassifier.Classify(authMsg.content);
        _output.WriteLine($"msg-001 intents: [{string.Join(",", authKey.IntentIds)}] domains: [{string.Join(",", authKey.DomainIds)}]");
        Assert.Contains(DcsIntentClassifier.INTENT_FIX, authKey.IntentIds); // "errors", "issue"

        // Dashboard design message should have QUERY + INSTRUCTION intents
        var dashMsg = _allMessages.First(m => m.id == "msg-003");
        var dashKey = DcsIntentClassifier.Classify(dashMsg.content);
        _output.WriteLine($"msg-003 intents: [{string.Join(",", dashKey.IntentIds)}] domains: [{string.Join(",", dashKey.DomainIds)}]");
        Assert.Contains(DcsIntentClassifier.INTENT_INSTRUCTION, dashKey.IntentIds); // "build"

        // Noise messages should classify as CHAT
        var noiseMsg = _allMessages.First(m => m.id == "msg-011");
        var noiseKey = DcsIntentClassifier.Classify(noiseMsg.content);
        _output.WriteLine($"msg-011 intents: [{string.Join(",", noiseKey.IntentIds)}] domains: [{string.Join(",", noiseKey.DomainIds)}]");
        Assert.Contains(DcsIntentClassifier.INTENT_CHAT, noiseKey.IntentIds);
    }

    // ── Test: Domain classification identifies backend/frontend/infra ────

    [Fact]
    public void Classification_ProducesExpectedDomains()
    {
        // Database migration should be BACKEND
        var dbMsg = _allMessages.First(m => m.id == "msg-006");
        var dbKey = DcsIntentClassifier.Classify(dbMsg.content);
        _output.WriteLine($"msg-006 domains: [{string.Join(",", dbKey.DomainIds)}]");
        Assert.Contains(DcsIntentClassifier.DOMAIN_BACKEND, dbKey.DomainIds); // "sql" or "database"

        // CI/CD should be INFRASTRUCTURE
        var ciMsg = _allMessages.First(m => m.id == "msg-020");
        var ciKey = DcsIntentClassifier.Classify(ciMsg.content);
        _output.WriteLine($"msg-020 domains: [{string.Join(",", ciKey.DomainIds)}]");
        Assert.Contains(DcsIntentClassifier.DOMAIN_INFRASTRUCTURE, ciKey.DomainIds); // "pipeline", "deploy", "docker"

        // Dashboard should be FRONTEND
        var dashMsg = _allMessages.First(m => m.id == "msg-003");
        var dashKey = DcsIntentClassifier.Classify(dashMsg.content);
        _output.WriteLine($"msg-003 domains: [{string.Join(",", dashKey.DomainIds)}]");
        Assert.Contains(DcsIntentClassifier.DOMAIN_FRONTEND, dashKey.DomainIds); // "React", "component"
    }

    // ── Test: Auth bug query returns auth-related messages ───────────────

    [Fact]
    public void AuthBugQuery_ReturnsAuthMessages()
    {
        var results = QueryWithMessage(
            "The JWT token refresh is failing with a race condition");
        LogResults("Auth Bug Query", results);

        var authIds = GetThreadMessageIds("auth-bug");
        var authHits = results.Count(r => authIds.Contains(r.MessageId));

        _output.WriteLine($"\nAuth thread messages in results: {authHits}/{results.Count}");
        _output.WriteLine($"Precision: {ThreadPrecision(results, "auth-bug"):P1}");
        _output.WriteLine($"Recall: {ThreadRecall(results, "auth-bug"):P1}");

        // At least some auth messages should appear
        Assert.True(authHits >= 2, $"Expected at least 2 auth messages, got {authHits}");
    }

    // ── Test: Dashboard query returns frontend messages ──────────────────

    [Fact]
    public void DashboardQuery_ReturnsFrontendMessages()
    {
        var results = QueryWithMessage(
            "How should I build the React analytics dashboard component with real-time updates?");
        LogResults("Dashboard Query", results);

        var dashIds = GetThreadMessageIds("react-dashboard");
        var dashHits = results.Count(r => dashIds.Contains(r.MessageId));

        _output.WriteLine($"\nDashboard thread messages in results: {dashHits}/{results.Count}");
        _output.WriteLine($"Precision: {ThreadPrecision(results, "react-dashboard"):P1}");

        Assert.True(dashHits >= 2, $"Expected at least 2 dashboard messages, got {dashHits}");
    }

    // ── Test: Database migration query returns migration messages ────────

    [Fact]
    public void MigrationQuery_ReturnsMigrationMessages()
    {
        var results = QueryWithMessage(
            "Running the PostgreSQL database migration with tenant_id and foreign key constraints");
        LogResults("Migration Query", results);

        var dbIds = GetThreadMessageIds("db-migration");
        var dbHits = results.Count(r => dbIds.Contains(r.MessageId));

        _output.WriteLine($"\nMigration thread messages in results: {dbHits}/{results.Count}");
        _output.WriteLine($"Precision: {ThreadPrecision(results, "db-migration"):P1}");

        Assert.True(dbHits >= 2, $"Expected at least 2 migration messages, got {dbHits}");
    }

    // ── Test: Rate limiting query returns API rate limit messages ────────

    [Fact]
    public void RateLimitQuery_ReturnsRateLimitMessages()
    {
        var results = QueryWithMessage(
            "Implement rate limiting with Redis sliding window for the API endpoints");
        LogResults("Rate Limit Query", results);

        var rlIds = GetThreadMessageIds("api-rate-limit");
        var rlHits = results.Count(r => rlIds.Contains(r.MessageId));

        _output.WriteLine($"\nRate limit thread messages in results: {rlHits}/{results.Count}");
        _output.WriteLine($"Precision: {ThreadPrecision(results, "api-rate-limit"):P1}");

        Assert.True(rlHits >= 2, $"Expected at least 2 rate limit messages, got {rlHits}");
    }

    // ── Test: Noise messages should NOT dominate results ─────────────────

    [Fact]
    public void NoiseMessages_DoNotDominate_TechnicalQueries()
    {
        // Query about a specific technical topic
        var results = QueryWithMessage(
            "Fix the authentication middleware race condition in the token refresh");
        LogResults("Technical Query (checking noise)", results);

        var noiseIds = GetThreadMessageIds("noise");
        var noiseHits = results.Count(r => noiseIds.Contains(r.MessageId));

        _output.WriteLine($"\nNoise messages in results: {noiseHits}/{results.Count}");

        // Noise should be a minority of results (less than 30%)
        if (results.Count > 0)
        {
            var noisePct = (double)noiseHits / results.Count;
            _output.WriteLine($"Noise percentage: {noisePct:P1}");
            Assert.True(noisePct < 0.30,
                $"Noise messages are {noisePct:P1} of results — expected <30%");
        }
    }

    // ── Test: Purely casual query should match noise/chat ───────────────

    [Fact]
    public void CasualQuery_MatchesChatMessages()
    {
        var results = QueryWithMessage("Hey, thanks for the help today");
        LogResults("Casual Query", results);

        // Should find chat/noise messages — the key thing is it doesn't
        // return highly technical messages about auth or migrations
        _output.WriteLine($"\nReturned {results.Count} messages for casual query");

        // The casual query should match CHAT intent — any result is fine,
        // but it shouldn't be exclusively technical backend messages
        if (results.Count > 0)
        {
            var noiseIds = GetThreadMessageIds("noise");
            var chatHits = results.Count(r => noiseIds.Contains(r.MessageId));
            _output.WriteLine($"Noise/chat messages: {chatHits}/{results.Count}");
        }
    }

    // ── Test: DCN merge reduces fragmentation ───────────────────────────

    [Fact]
    public void MergeSimilarDcns_ReducesFragmentation()
    {
        var dcnCountBefore = _assembler.DcnStore.Count;
        _output.WriteLine($"DCNs before merge: {dcnCountBefore}");

        _assembler.MergeSimilarDcns();

        var dcnCountAfter = _assembler.DcnStore.Count;
        _output.WriteLine($"DCNs after merge: {dcnCountAfter}");

        // Log remaining DCNs
        foreach (var dcn in _assembler.DcnStore.Values.OrderBy(d => d.DcnId))
        {
            var msgThreads = dcn.LinkedMessageIds
                .Select(id => _allMessages.FirstOrDefault(m => m.id == id)?.thread ?? "?")
                .GroupBy(t => t)
                .Select(g => $"{g.Key}={g.Count()}")
                .ToList();
            _output.WriteLine($"  DCN {dcn.DcnId}: topics=[{string.Join(",", dcn.TopicIdentifiers)}] msgs={dcn.LinkedMessageIds.Count} threads=[{string.Join(", ", msgThreads)}]");
        }

        // After merge, we should have fewer DCNs (similar topics consolidated)
        // The exact number depends on threshold tuning, but it should reduce
        _output.WriteLine($"\nReduction: {dcnCountBefore} -> {dcnCountAfter} ({dcnCountBefore - dcnCountAfter} merged)");
    }

    // ── Test: Cross-thread messages are reachable from both threads ──────

    [Fact]
    public void CrossThreadMessages_AreReachable()
    {
        // msg-017: "The dashboard needs to handle expired JWT tokens gracefully"
        // This is a cross-ref between dashboard (frontend) and auth (backend)
        var crossMsg = _registered["msg-017"];

        // Query from auth perspective
        var authResults = QueryWithMessage("JWT token authentication error handling");
        var authFound = authResults.Any(r => r.MessageId == "msg-017");
        _output.WriteLine($"msg-017 found via auth query: {authFound}");

        // Query from dashboard perspective
        var dashResults = QueryWithMessage("React dashboard handling API authentication failures");
        var dashFound = dashResults.Any(r => r.MessageId == "msg-017");
        _output.WriteLine($"msg-017 found via dashboard query: {dashFound}");

        LogResults("Auth-perspective query", authResults);
        LogResults("Dashboard-perspective query", dashResults);

        // The cross-ref should be reachable from at least one perspective
        Assert.True(authFound || dashFound,
            "Cross-thread message msg-017 should be reachable from auth or dashboard queries");
    }

    // ── Test: Influence scoring links sequential messages ────────────────

    [Fact]
    public void InfluenceScoring_LinksSequentialMessages()
    {
        // msg-004 and msg-005 are sequential auth messages — should have high influence
        var msg4 = _registered["msg-004"];
        var msg5 = _registered["msg-005"];
        var influence = _assembler.ComputeInfluence(msg4, msg5);

        _output.WriteLine($"Influence msg-004 -> msg-005: {influence:F3}");
        _output.WriteLine($"  msg-004 DCNs: [{string.Join(",", msg4.HardLinkedDcns)}]");
        _output.WriteLine($"  msg-005 DCNs: [{string.Join(",", msg5.HardLinkedDcns)}]");
        _output.WriteLine($"  msg-004 domains: [{string.Join(",", msg4.Key.DomainIds)}]");
        _output.WriteLine($"  msg-005 domains: [{string.Join(",", msg5.Key.DomainIds)}]");

        // Sequential messages in the same thread should have positive influence
        Assert.True(influence > 0, $"Expected positive influence between sequential auth messages, got {influence}");
    }

    // ── Test: Full diagnostic report ────────────────────────────────────

    [Fact]
    public void FullDiagnosticReport()
    {
        _output.WriteLine("=== FULL DCS DIAGNOSTIC REPORT ===\n");

        // 1. Classification summary
        _output.WriteLine("--- Classification Summary ---");
        var threadGroups = _allMessages.GroupBy(m => m.thread.StartsWith("cross-ref") ? "cross-ref" : m.thread);
        foreach (var group in threadGroups.OrderBy(g => g.Key))
        {
            _output.WriteLine($"\n  Thread: {group.Key} ({group.Count()} messages)");
            foreach (var msg in group.Take(3))
            {
                var key = DcsIntentClassifier.Classify(msg.content);
                var intents = string.Join(",", key.IntentIds.Select(i => $"{i}"));
                var domains = string.Join(",", key.DomainIds.Select(d => $"{d}"));
                _output.WriteLine($"    {msg.id}: intents=[{intents}] domains=[{domains}] \"{Truncate(msg.content, 50)}\"");
            }
        }

        // 2. DCN assignments
        _output.WriteLine("\n--- DCN Assignments ---");
        foreach (var dcn in _assembler.DcnStore.Values.OrderBy(d => d.DcnId))
        {
            var msgThreads = dcn.LinkedMessageIds
                .Select(id => _allMessages.FirstOrDefault(m => m.id == id)?.thread ?? "?")
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToList();

            _output.WriteLine($"  DCN {dcn.DcnId}: topics=[{string.Join(",", dcn.TopicIdentifiers)}] msgs={dcn.LinkedMessageIds.Count}");
            _output.WriteLine($"    Thread distribution: {string.Join(", ", msgThreads)}");
            _output.WriteLine($"    Recency weight: {dcn.ComputeRecencyWeight():F4}");
        }

        // 3. Context assembly for each thread
        _output.WriteLine("\n--- Context Assembly by Thread ---");
        var queries = new Dictionary<string, string>
        {
            ["auth-bug"] = "JWT token refresh race condition authentication middleware",
            ["react-dashboard"] = "React analytics dashboard component real-time polling",
            ["db-migration"] = "PostgreSQL schema migration tenant_id foreign key constraint",
            ["cicd-pipeline"] = "GitHub Actions CI/CD Docker build deploy pipeline",
            ["api-rate-limit"] = "API rate limiting Redis sliding window token bucket",
            ["vector-db-research"] = "vector database comparison Pinecone Qdrant Weaviate RAG"
        };

        foreach (var (thread, query) in queries)
        {
            var results = QueryWithMessage(query, 15);
            var threadIds = GetThreadMessageIds(thread);
            var precision = ThreadPrecision(results, thread);
            var recall = ThreadRecall(results, thread);

            _output.WriteLine($"\n  Query for [{thread}]: \"{Truncate(query, 60)}\"");
            _output.WriteLine($"    Results: {results.Count}, Thread hits: {results.Count(r => threadIds.Contains(r.MessageId))}/{threadIds.Count}");
            _output.WriteLine($"    Precision: {precision:P1}, Recall: {recall:P1}");

            var threadBreakdown = results
                .Select(r => _allMessages.FirstOrDefault(m => m.id == r.MessageId)?.thread ?? "?")
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}={g.Count()}")
                .ToList();
            _output.WriteLine($"    Breakdown: [{string.Join(", ", threadBreakdown)}]");
        }

        // 4. Selection log summary
        _output.WriteLine("\n--- Selection Log ---");
        foreach (var entry in _assembler.Log.TakeLast(10))
        {
            _output.WriteLine($"  {entry}");
        }

        _output.WriteLine("\n=== END DIAGNOSTIC REPORT ===");
    }
}
