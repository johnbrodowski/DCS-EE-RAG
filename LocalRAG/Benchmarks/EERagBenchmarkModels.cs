using Newtonsoft.Json;
using System.Text;

namespace LocalRAG.Benchmarks
{
    // ── Dataset models ────────────────────────────────────────────────────────

    public class BenchmarkDataset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("knowledge_entries")]
        public List<KnowledgeEntry> KnowledgeEntries { get; set; } = [];

        [JsonProperty("benchmark_cases")]
        public List<BenchmarkCase> Cases { get; set; } = [];
    }

    public class KnowledgeEntry
    {
        [JsonProperty("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonProperty("request")]
        public string Request { get; set; } = string.Empty;

        [JsonProperty("text_response")]
        public string TextResponse { get; set; } = string.Empty;

        [JsonProperty("summary")]
        public string? Summary { get; set; }
    }

    public class BenchmarkCase
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("query")]
        public string Query { get; set; } = string.Empty;

        [JsonProperty("expected_slugs")]
        public List<string> ExpectedSlugs { get; set; } = [];

        [JsonProperty("expect_retrieval")]
        public bool ExpectRetrieval { get; set; }

        [JsonProperty("expected_answer_hint")]
        public string? ExpectedAnswerHint { get; set; }
    }

    // ── Result models ─────────────────────────────────────────────────────────

    public class BenchmarkOptions
    {
        public int TopK { get; set; } = 5;
        public bool SilentMode { get; set; } = false;
        public bool SeedFreshDatabase { get; set; } = true;
    }

    public class CaseResult
    {
        public string CaseId { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public List<int> ExpectedIds { get; set; } = [];
        public List<int> SurfacedIds { get; set; } = [];
        public List<int> ElectedIds { get; set; } = [];
        public double CandidateRecall { get; set; }
        public double Precision { get; set; }
        public double Recall { get; set; }
        public double F1 { get; set; }
        public bool ExpectedRetrieval { get; set; }
        public bool ActualRetrieval { get; set; }
        public TimeSpan Duration { get; set; }
        public string? AnswerHint { get; set; }
        public bool AnswerHintMatched { get; set; }
        public string FirstResponse { get; set; } = string.Empty;
    }

    public class BenchmarkReport
    {
        public DateTime RunAt { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public int TotalCases { get; set; }
        public double MeanCandidateRecall { get; set; }
        public double MeanRetrievalPrecision { get; set; }
        public double MeanRetrievalRecall { get; set; }
        public double MeanF1 { get; set; }
        public double RetrievalRateActual { get; set; }
        public double RetrievalRateExpected { get; set; }
        public List<CaseResult> CaseResults { get; set; } = [];
        public string DatasetName { get; set; } = string.Empty;

        public string FormatSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== EE-RAG Benchmark Report ===");
            sb.AppendLine($"Dataset   : {DatasetName}");
            sb.AppendLine($"Run At    : {RunAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Duration  : {TotalDuration.TotalSeconds:F1}s over {TotalCases} cases");
            sb.AppendLine();
            sb.AppendLine($"Mean Candidate Recall    : {MeanCandidateRecall:P1}");
            sb.AppendLine($"Mean Retrieval Precision : {MeanRetrievalPrecision:P1}");
            sb.AppendLine($"Mean Retrieval Recall    : {MeanRetrievalRecall:P1}");
            sb.AppendLine($"Mean F1                  : {MeanF1:P1}");
            sb.AppendLine($"Retrieval Rate (Actual)  : {RetrievalRateActual:P1}  (Expected: {RetrievalRateExpected:P1})");
            sb.AppendLine();
            sb.AppendLine("--- Per-Case Results ---");
            foreach (var r in CaseResults)
            {
                var hint = r.AnswerHint != null
                    ? (r.AnswerHintMatched ? "[hint ok]" : "[hint MISS]")
                    : "";
                sb.AppendLine(
                    $"  {r.CaseId,-8} F1={r.F1:F2}  P={r.Precision:F2}  R={r.Recall:F2}" +
                    $"  CandRec={r.CandidateRecall:F2}  Ret={r.ActualRetrieval}  {hint}");
                sb.AppendLine($"           Query: {r.Query}");
                sb.AppendLine($"           Expected IDs: [{string.Join(", ", r.ExpectedIds)}]" +
                    $"  Surfaced: [{string.Join(", ", r.SurfacedIds)}]" +
                    $"  Elected: [{string.Join(", ", r.ElectedIds)}]");
                if (!string.IsNullOrEmpty(r.FirstResponse))
                {
                    var preview = r.FirstResponse.Length > 120
                        ? r.FirstResponse[..120] + "..."
                        : r.FirstResponse;
                    sb.AppendLine($"           Model said: {preview.Replace("\n", " ").Replace("\r", "")}");
                }
            }
            return sb.ToString();
        }
    }
}
