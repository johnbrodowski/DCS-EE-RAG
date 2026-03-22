using System.Text.Json;

namespace DemoApp
{
    public class BenchmarkSettings
    {
        // ── Run settings ──────────────────────────────────────────────────────
        public string Provider { get; set; } = "Anthropic";
        public string Model { get; set; } = "claude-haiku-4-5-20251001";
        public int MaxQuestions { get; set; } = 50;
        public bool UseRag { get; set; } = true;
        public double Temperature { get; set; } = 0.7;

        // ── Embedding settings ─────────────────────────────────────────────────
        public bool RemoveStopWords { get; set; } = false;
        public double OverlapPercentage { get; set; } = 25.0;
        public int WordsPerChunk { get; set; } = 40;
        public int MaxSequenceLength { get; set; } = 256;
        public bool LowercaseInput { get; set; } = true;
        public bool OverwriteEmbeddings { get; set; } = false;

        // ── DCS / EE-RAG pipeline settings ───────────────────────────────────
        public bool UseDcsPipeline { get; set; } = false;
        public int DcsTopK { get; set; } = 5;
        public bool DcsSilentMode { get; set; } = false;

        // ── Thresholds ────────────────────────────────────────────────────────
        // Correct and PossiblyCorrect are "at least" targets;
        // DefinitelyWrong and Indeterminate are "at most" targets.
        public double CorrectThreshold { get; set; } = 50.0;
        public double PossiblyCorrectThreshold { get; set; } = 10.0;
        public double DefinitelyWrongThreshold { get; set; } = 10.0;
        public double IndeterminateThreshold { get; set; } = 30.0;

        // ── Auto-tune settings ────────────────────────────────────────────────
        /// <summary>Comma-separated list of temperatures to sweep (e.g. "0.0, 0.3, 0.7, 1.0").</summary>
        public string TuneTemperatures { get; set; } = "0.0, 0.3, 0.5, 0.7, 1.0, 1.5";
        /// <summary>Comma-separated models to sweep. Empty means use the current Model only.</summary>
        public string TuneModels { get; set; } = "";
        /// <summary>Questions per mini-run during auto-tuning.</summary>
        public int TuneQuestionsPerRun { get; set; } = 10;
        /// <summary>Scoring formula used to rank configurations: "Correct", "Correct+Possibly", "Composite".</summary>
        public string TuneScoreMetric { get; set; } = "Correct";

        // ── Persistence ───────────────────────────────────────────────────────
        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        private static string SettingsPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "benchmark-settings.json");

        public static BenchmarkSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<BenchmarkSettings>(json) ?? new();
                }
            }
            catch { }
            return new();
        }

        public void Save()
        {
            try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, _jsonOpts)); }
            catch { }
        }
    }
}
