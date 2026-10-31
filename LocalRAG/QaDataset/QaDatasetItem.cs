using Newtonsoft.Json;

namespace LocalRAG.QaDataset
{
    /// <summary>
    /// A single Q&amp;A item loaded from a JSONL dataset file.
    /// </summary>
    public class QaDatasetItem
    {
        public int Id { get; set; }

        [JsonProperty("question")]
        public string Question { get; set; } = "";

        [JsonProperty("answer")]
        public List<string> Answers { get; set; } = new();

        [JsonProperty("def_correct_predictions")]
        public List<string> DefCorrectPredictions { get; set; } = new();

        [JsonProperty("poss_correct_predictions")]
        public List<string> PossCorrectPredictions { get; set; } = new();

        [JsonProperty("def_incorrect_predictions")]
        public List<string> DefIncorrectPredictions { get; set; } = new();

        [JsonProperty("answer_and_def_correct_predictions")]
        public List<string> AnswerAndDefCorrectPredictions { get; set; } = new();

        // Set after loading from DB — not in JSON
        public float[]? QuestionEmbedding { get; set; }
        public bool IsEmbedded { get; set; }
        public string? DatasetSource { get; set; }
        public DateTime ImportedAt { get; set; }
    }

    /// <summary>Result of evaluating one question during a benchmark run.</summary>
    public class QaRunResult
    {
        public int Id { get; set; }
        public string RunId { get; set; } = "";
        public int QaItemId { get; set; }
        public string Question { get; set; } = "";
        public string? ModelResponse { get; set; }
        public QaOutcome Outcome { get; set; }
        public string? MatchedAnswer { get; set; }
        public DateTime RunAt { get; set; }
    }

    public enum QaOutcome
    {
        Correct,
        PossiblyCorrect,
        DefinitelyWrong,
        Indeterminate,
        /// <summary>The model call failed; the question was not answered, so it counts toward no accuracy figure.</summary>
        Error
    }

    public class QaBenchmarkOptions
    {
        public int MaxQuestions { get; set; } = 50;
        /// <summary>
        /// Fixes which questions are drawn. Leave null for a random sample. Runs that are compared with each other
        /// (a sweep over models or temperatures) must share one seed, or each is scored on different questions and a
        /// difference may be only the luck of the draw.
        /// </summary>
        public int? Seed { get; set; }
        public string Provider { get; set; } = "Anthropic";
        public string Model { get; set; } = "claude-haiku-4-5-20251001";
        public int MaxTokens { get; set; } = 256;
        public double? Temperature { get; set; }
        /// <summary>If true and embeddings are available, inject similar Q&amp;A pairs as context.</summary>
        public bool UseRagContext { get; set; } = true;
        public int SimilarContextCount { get; set; } = 3;
        /// <summary>The run stops after this many failed model calls in a row (a bad key, a rate limit, a context overflow). 0 never stops it.</summary>
        public int MaxConsecutiveErrors { get; set; } = 5;
    }
}
