using LocalRAG;
using Xunit;

namespace LocalRAG.Tests;

/// <summary>
/// The EE-RAG two-phase audit lives in the MetaData column. Rows inserted without
/// metadata have a NULL there, so the audit writers must handle that (and a missing row).
/// Gated on the model like the other integration tests.
/// </summary>
[Trait("Category", "Integration")]
public class RagAuditTests
{
    [SkippableFact]
    public async Task Audit_WritesToRowWithNullMetaData_AndIgnoresMissingRow()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"audit_{Guid.NewGuid():N}.db");
        var db = new EmbeddingDatabaseNew(new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = dbPath,
            IndexChunkEmbeddings = false
        });

        try
        {
            await db.EnsureReadyAsync();
            await db.AddRequestToEmbeddingDatabaseAsync("req1", "Kubernetes orchestrates containers.", embed: true);

            var before = await db.GetFeedbackDataByRequestIdAsync("req1");
            Assert.True(string.IsNullOrEmpty(before?.MetaData), "precondition: a fresh row has no MetaData");

            Assert.True(await db.UpdateRagCandidatesSurfacedAsync("req1", new List<int> { 1, 2, 3 }));
            Assert.True(await db.UpdateRagEntriesElectedAsync("req1", new List<string> { "abcd1234" }));

            var after = await db.GetFeedbackDataByRequestIdAsync("req1");
            Assert.Contains("RagCandidatesSurfaced", after?.MetaData ?? "");
            Assert.Contains("RagEntriesElected", after?.MetaData ?? "");
            Assert.Contains("abcd1234", after?.MetaData ?? "");

            // A request id with no row must not throw; the audit is just not recorded.
            Assert.False(await db.UpdateRagCandidatesSurfacedAsync("nope", new List<int> { 1 }));
            Assert.False(await db.UpdateRagEntriesElectedAsync("nope", new List<string> { "deadbeef" }));
        }
        finally
        {
            await db.DisposeAsync();
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); break; }
                catch (IOException) { await Task.Delay(100); }
            }
        }
    }

    [SkippableFact]
    public async Task Audit_ConcurrentWritesToOneRow_KeepEveryKey()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"audit2_{Guid.NewGuid():N}.db");
        var db = new EmbeddingDatabaseNew(new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = dbPath,
            IndexChunkEmbeddings = false
        });

        try
        {
            await db.EnsureReadyAsync();
            await db.AddRequestToEmbeddingDatabaseAsync("req1", "Kubernetes orchestrates containers.", embed: false);

            var writes = await Task.WhenAll(
                db.UpdateRagCandidatesSurfacedAsync("req1", new List<int> { 1 }),
                db.UpdateRagEntriesElectedAsync("req1", new List<string> { "abcd1234" }),
                db.UpdateRagElectionLogAsync("req1", new ElectionTrace { RequestId = "req1" }));
            Assert.All(writes, Assert.True);

            var meta = (await db.GetFeedbackDataByRequestIdAsync("req1"))?.MetaData ?? "";
            Assert.Contains("RagCandidatesSurfaced", meta);
            Assert.Contains("RagEntriesElected", meta);
            Assert.Contains("RagElectionLog", meta);
        }
        finally
        {
            await db.DisposeAsync();
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); break; }
                catch (IOException) { await Task.Delay(100); }
            }
        }
    }

    [SkippableFact]
    public async Task Pipeline_SearchReportsTheAuditWasRecorded_AndFuzzyQuoteDoesNotThrow()
    {
        var defaults = new RAGConfiguration();
        var modelPath = Environment.GetEnvironmentVariable("BERT_MODEL_PATH") ?? defaults.ModelPath;
        var vocabPath = Environment.GetEnvironmentVariable("BERT_VOCAB_PATH") ?? defaults.VocabularyPath;
        Skip.IfNot(File.Exists(modelPath), "BERT model not configured. Set BERT_MODEL_PATH.");

        var dbPath = Path.Combine(Path.GetTempPath(), $"audit3_{Guid.NewGuid():N}.db");
        var db = new EmbeddingDatabaseNew(new RAGConfiguration
        {
            ModelPath = modelPath,
            VocabularyPath = vocabPath,
            DatabasePath = dbPath,
            IndexChunkEmbeddings = false
        });

        try
        {
            await db.EnsureReadyAsync();
            await db.AddRequestToEmbeddingDatabaseAsync("kube", "Kubernetes orchestrates containers.", embed: true);

            var json = await EERagPipeline.HandleAsync(
                "{\"op\":\"search\",\"requestId\":\"kube\",\"query\":\"containers\",\"topK\":3}", db);
            Assert.True((bool)Newtonsoft.Json.Linq.JObject.Parse(json)["auditRecorded"]!);

            // A double quote in a query word used to produce an invalid FTS5 MATCH in the fuzzy path.
            var hits = await db.SearchEmbeddingsAsync("say \"hello kube\" containers", topK: 3, minimumSimilarity: 0.0f, searchLevel: 1, forceFuzzy: true);
            Assert.NotNull(hits);
        }
        finally
        {
            await db.DisposeAsync();
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); break; }
                catch (IOException) { await Task.Delay(100); }
            }
        }
    }
}
