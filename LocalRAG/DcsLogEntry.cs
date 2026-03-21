namespace LocalRAG
{
    /// <summary>
    /// Structured log record for DCS context selection decisions.
    /// Used for tuning thresholds, debugging misclassification, and validating behavior.
    /// </summary>
    public class DcsLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>The DCN selected as primary context anchor (null if no match).</summary>
        public int? SelectedDcnId { get; set; }

        /// <summary>Number of candidate messages before filtering.</summary>
        public int CandidateMessageCount { get; set; }

        /// <summary>Number of messages remaining after filtering.</summary>
        public int FilteredMessageCount { get; set; }

        /// <summary>Top similarity scores (up to 5) with their message IDs.</summary>
        public List<(string MessageId, double Score)> TopScores { get; set; } = [];

        /// <summary>"Full" when all messages are scanned; "Selective" when index-based.</summary>
        public string Strategy { get; set; } = "Selective";

        public override string ToString()
        {
            var scores = TopScores.Count > 0
                ? string.Join(", ", TopScores.Select(t => $"{t.MessageId}={t.Score:F3}"))
                : "none";

            return $"[DCS {Timestamp:HH:mm:ss.fff}] DCN={SelectedDcnId?.ToString() ?? "null"} " +
                   $"Candidates={CandidateMessageCount} Filtered={FilteredMessageCount} " +
                   $"Strategy={Strategy} TopScores=[{scores}]";
        }
    }
}
