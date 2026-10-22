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

            await db.UpdateRagCandidatesSurfacedAsync("req1", new List<int> { 1, 2, 3 });
            await db.UpdateRagEntriesElectedAsync("req1", new List<string> { "abcd1234" });

            var after = await db.GetFeedbackDataByRequestIdAsync("req1");
            Assert.Contains("RagCandidatesSurfaced", after?.MetaData ?? "");
            Assert.Contains("RagEntriesElected", after?.MetaData ?? "");
            Assert.Contains("abcd1234", after?.MetaData ?? "");

            // A request id with no row must not throw; the audit is just not recorded.
            await db.UpdateRagCandidatesSurfacedAsync("nope", new List<int> { 1 });
            await db.UpdateRagEntriesElectedAsync("nope", new List<string> { "deadbeef" });
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
