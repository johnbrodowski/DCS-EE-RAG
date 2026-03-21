using System.Text;

namespace LocalRAG.QaDataset
{
    public class QaBenchmarkReport
    {
        public string RunId { get; set; } = "";
        public int TotalRun { get; set; }
        public int Correct { get; set; }
        public int PossiblyCorrect { get; set; }
        public int DefinitelyWrong { get; set; }
        public int Indeterminate { get; set; }
        public TimeSpan Duration { get; set; }
        public List<QaRunResult> Results { get; set; } = new();

        // Run configuration
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public double? Temperature { get; set; }

        // Score thresholds (null = not set, no pass/fail displayed)
        public double? CorrectThreshold { get; set; }
        public double? PossiblyCorrectThreshold { get; set; }
        public double? DefinitelyWrongThreshold { get; set; }
        public double? IndeterminateThreshold { get; set; }

        public double CorrectRate => TotalRun == 0 ? 0 : (double)Correct / TotalRun * 100;
        public double PossiblyCorrectRate => TotalRun == 0 ? 0 : (double)PossiblyCorrect / TotalRun * 100;
        public double DefinitelyWrongRate => TotalRun == 0 ? 0 : (double)DefinitelyWrong / TotalRun * 100;
        public double IndeterminateRate => TotalRun == 0 ? 0 : (double)Indeterminate / TotalRun * 100;

        public string FormatSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== QA Benchmark Report (RunId: {RunId}) ===");
            sb.AppendLine($"Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Duration  : {Duration:mm\\:ss\\.fff}");

            if (!string.IsNullOrEmpty(Provider))
                sb.AppendLine($"Provider  : {Provider}");
            if (!string.IsNullOrEmpty(Model))
                sb.AppendLine($"Model     : {Model}");
            if (Temperature.HasValue)
                sb.AppendLine($"Temperature: {Temperature.Value:F2}");

            sb.AppendLine();
            sb.AppendLine($"Questions tested  : {TotalRun}");
            sb.AppendLine(FormatScoreLine("Correct          ", Correct, CorrectRate,
                CorrectThreshold, isAtLeast: true));
            sb.AppendLine(FormatScoreLine("Possibly Correct ", PossiblyCorrect, PossiblyCorrectRate,
                PossiblyCorrectThreshold, isAtLeast: true));
            sb.AppendLine(FormatScoreLine("Definitely Wrong ", DefinitelyWrong, DefinitelyWrongRate,
                DefinitelyWrongThreshold, isAtLeast: false));
            sb.AppendLine(FormatScoreLine("Indeterminate    ", Indeterminate, IndeterminateRate,
                IndeterminateThreshold, isAtLeast: false));

            sb.AppendLine();
            sb.AppendLine("─── Per-Question Results ───────────────────────────────────────────");

            foreach (var r in Results)
            {
                var icon = r.Outcome switch
                {
                    QaOutcome.Correct          => "[+]",
                    QaOutcome.PossiblyCorrect  => "[?]",
                    QaOutcome.DefinitelyWrong  => "[X]",
                    _                          => "[-]"
                };
                var q = r.Question.Length > 70 ? r.Question[..70] + "…" : r.Question;
                sb.AppendLine($"{icon} {q}");

                if (!string.IsNullOrEmpty(r.MatchedAnswer))
                    sb.AppendLine($"    Matched : \"{r.MatchedAnswer}\"");

                var resp = r.ModelResponse ?? "(no response)";
                if (resp.Length > 140) resp = resp[..140] + "…";
                sb.AppendLine($"    Response: {resp}");
            }

            return sb.ToString();
        }

        private static string FormatScoreLine(
            string label, int count, double rate, double? threshold, bool isAtLeast)
        {
            var line = $"{label} : {count,5} ({rate:F1}%)";

            if (threshold.HasValue)
            {
                bool pass = isAtLeast ? rate >= threshold.Value : rate <= threshold.Value;
                var symbol = isAtLeast ? "≥" : "≤";
                var status = pass ? "PASS" : "FAIL";
                line += $"  [{status} {symbol}{threshold.Value:F0}%]";
            }

            return line;
        }
    }
}
