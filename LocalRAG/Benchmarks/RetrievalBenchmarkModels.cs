using System.Globalization;
using System.Text;

namespace LocalRAG.Benchmarks
{
    // ── Options ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures a single retrieval-quality benchmark run. This benchmark is
    /// LLM-free: it calls <c>EmbeddingDatabaseNew.SearchEmbeddingsAsync</c>
    /// directly and scores the ranked candidate list against ground-truth slugs,
    /// so it is deterministic and free to run between every tuning change.
    /// </summary>
    public class RetrievalBenchmarkOptions
    {
        /// <summary>Cut-off depths at which Recall/Precision/nDCG are reported.</summary>
        public int[] Ks { get; set; } = { 1, 3, 5, 10 };

        /// <summary>How many ranked results to request (should be >= max(Ks)).</summary>
        public int TopK { get; set; } = 20;

        /// <summary>
        /// Per-channel similarity floor passed to SearchEmbeddingsAsync. Kept at 0
        /// for benchmarking so the full ranked list is scored rather than pre-cut.
        /// </summary>
        public float MinimumSimilarity { get; set; } = 0.0f;

        /// <summary>Search level forwarded to SearchEmbeddingsAsync (2 = request+response).</summary>
        public int SearchLevel { get; set; } = 2;

        /// <summary>
        /// Free-text tag written into every output row (e.g. "baseline",
        /// "item1-meanpool", "item3-rrf") so a tuning progression is comparable.
        /// </summary>
        public string Label { get; set; } = "default";

        /// <summary>Human-readable summary of the config knobs in effect (for CSV).</summary>
        public string ConfigSummary { get; set; } = "";

        /// <summary>
        /// For "hard negative" cases (no expected slugs), the top-1 similarity at or
        /// above this floor counts as a false positive.
        /// </summary>
        public double NegativeSurfaceFloor { get; set; } = 0.5;

        public RetrievalBenchmarkOptions Clone() => new()
        {
            Ks = (int[])Ks.Clone(),
            TopK = TopK,
            MinimumSimilarity = MinimumSimilarity,
            SearchLevel = SearchLevel,
            Label = Label,
            ConfigSummary = ConfigSummary,
            NegativeSurfaceFloor = NegativeSurfaceFloor
        };
    }

    // ── Per-case result ─────────────────────────────────────────────────────────

    public class RetrievalCaseResult
    {
        public string CaseId { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public List<int> ExpectedIds { get; set; } = new();
        public List<int> RankedIds { get; set; } = new();

        /// <summary>True when the case has no expected slugs (a hard negative).</summary>
        public bool IsNegative { get; set; }

        /// <summary>For negatives: did anything surface at/above the similarity floor?</summary>
        public bool SurfacedAboveFloor { get; set; }

        public double ReciprocalRank { get; set; }
        public Dictionary<int, double> RecallAtK { get; set; } = new();
        public Dictionary<int, double> PrecisionAtK { get; set; } = new();
        public Dictionary<int, double> NdcgAtK { get; set; } = new();
    }

    // ── Aggregated report ───────────────────────────────────────────────────────

    public class RetrievalReport
    {
        public DateTime RunAt { get; set; }
        public string DatasetName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ConfigSummary { get; set; } = string.Empty;
        public int TotalCases { get; set; }
        public int PositiveCases { get; set; }
        public int NegativeCases { get; set; }
        public TimeSpan Duration { get; set; }
        public int[] Ks { get; set; } = Array.Empty<int>();

        public Dictionary<int, double> MeanRecallAtK { get; set; } = new();
        public Dictionary<int, double> MeanPrecisionAtK { get; set; } = new();
        public Dictionary<int, double> MeanNdcgAtK { get; set; } = new();
        public double Mrr { get; set; }

        /// <summary>Mean Recall at the full TopK depth (comparable to EERagBenchmark's CandidateRecall).</summary>
        public double MeanCandidateRecall { get; set; }

        /// <summary>Fraction of negative cases that wrongly surfaced something above the floor.</summary>
        public double NegativeFalsePositiveRate { get; set; }

        public List<RetrievalCaseResult> CaseResults { get; set; } = new();

        public double MeanRecallAt(int k) => MeanRecallAtK.TryGetValue(k, out var v) ? v : double.NaN;
        public double MeanNdcgAt(int k) => MeanNdcgAtK.TryGetValue(k, out var v) ? v : double.NaN;
        public double MeanPrecisionAt(int k) => MeanPrecisionAtK.TryGetValue(k, out var v) ? v : double.NaN;

        public string FormatSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Retrieval Benchmark Report ===");
            sb.AppendLine($"Dataset : {DatasetName}");
            sb.AppendLine($"Label   : {Label}");
            sb.AppendLine($"Config  : {ConfigSummary}");
            sb.AppendLine($"Run At  : {RunAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Cases   : {TotalCases} ({PositiveCases} positive, {NegativeCases} negative) in {Duration.TotalSeconds:F2}s");
            sb.AppendLine();
            sb.AppendLine($"MRR                       : {Mrr:F4}");
            sb.AppendLine($"Mean Candidate Recall@{TopKLabel()} : {MeanCandidateRecall:P1}");
            sb.AppendLine($"Negative False-Positive   : {NegativeFalsePositiveRate:P1}");
            sb.AppendLine();
            sb.AppendLine($"  {"k",4} | {"Recall@k",10} | {"Precision@k",12} | {"nDCG@k",8}");
            sb.AppendLine($"  {new string('-', 4)}-+-{new string('-', 10)}-+-{new string('-', 12)}-+-{new string('-', 8)}");
            foreach (var k in Ks)
            {
                sb.AppendLine(
                    $"  {k,4} | {MeanRecallAt(k),10:F4} | {MeanPrecisionAt(k),12:F4} | {MeanNdcgAt(k),8:F4}");
            }
            return sb.ToString();
        }

        private string TopKLabel()
        {
            // The candidate-recall depth equals TopK, but TopK isn't stored on the
            // report; infer it from the largest k requested as a display hint.
            return Ks.Length > 0 ? "TopK" : "TopK";
        }

        /// <summary>CSV header matching <see cref="ToCsvRows"/>.</summary>
        public static string CsvHeader() =>
            "timestamp,label,dataset,config,k,recall,precision,ndcg,mrr,mean_candidate_recall,neg_fp_rate,positive_cases,negative_cases,duration_ms";

        /// <summary>One row per cut-off depth k, so results pivot cleanly in a spreadsheet.</summary>
        public IEnumerable<string> ToCsvRows()
        {
            var ts = RunAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            foreach (var k in Ks)
            {
                yield return string.Join(",",
                    ts,
                    Csv(Label),
                    Csv(DatasetName),
                    Csv(ConfigSummary),
                    k.ToString(CultureInfo.InvariantCulture),
                    F(MeanRecallAt(k)),
                    F(MeanPrecisionAt(k)),
                    F(MeanNdcgAt(k)),
                    F(Mrr),
                    F(MeanCandidateRecall),
                    F(NegativeFalsePositiveRate),
                    PositiveCases.ToString(CultureInfo.InvariantCulture),
                    NegativeCases.ToString(CultureInfo.InvariantCulture),
                    ((long)Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
            }
        }

        private static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

        private static string Csv(string s)
        {
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
