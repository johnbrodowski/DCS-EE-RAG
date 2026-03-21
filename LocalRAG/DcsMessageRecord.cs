namespace LocalRAG
{
    /// <summary>
    /// A message record enriched with DCS metadata.
    /// Wraps the core message content with structured identifiers
    /// and DCN linkage for context routing.
    /// </summary>
    public class DcsMessageRecord
    {
        /// <summary>Unique identifier for this message (maps to RequestID in the database).</summary>
        public string MessageId { get; set; } = string.Empty;

        /// <summary>Structured identifier key (intent, domain, DCN).</summary>
        public DcsKey Key { get; set; } = new();

        /// <summary>DCN IDs this message is hard-linked to (primary continuity).</summary>
        public int[] HardLinkedDcns { get; set; } = Array.Empty<int>();

        /// <summary>The message text content.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>When this message was created.</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}
