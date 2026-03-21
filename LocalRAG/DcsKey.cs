namespace LocalRAG
{
    /// <summary>
    /// Structured identifier for Dynamic Context Selection.
    /// Combines intent, domain, and DCN (Dynamic Context Node) identifiers
    /// with weighted Jaccard similarity scoring.
    ///
    /// Weights: Intent = 0.5 (gating), DCN = 0.3 (continuity), Domain = 0.2 (refinement).
    /// Supports multi-intent classification and compact serialization.
    /// </summary>
    public class DcsKey
    {
        public int[] IntentIds { get; set; } = Array.Empty<int>();
        public int[] DomainIds { get; set; } = Array.Empty<int>();
        public int[] DcnIds { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Computes weighted similarity against another key.
        /// Intent dominates (0.5), DCN is critical for continuity (0.3),
        /// domain is a secondary signal (0.2).
        /// </summary>
        public double Similarity(DcsKey other)
        {
            double intentScore = Jaccard(IntentIds, other.IntentIds);
            double domainScore = Jaccard(DomainIds, other.DomainIds);
            double dcnScore = Jaccard(DcnIds, other.DcnIds);

            return (intentScore * 0.5) +
                   (dcnScore * 0.3) +
                   (domainScore * 0.2);
        }

        /// <summary>
        /// Jaccard similarity between two integer sets.
        /// Both empty returns 1.0 (identical empties); one empty returns 0.0.
        /// </summary>
        public static double Jaccard(int[] a, int[] b)
        {
            if (a.Length == 0 && b.Length == 0) return 1.0;
            if (a.Length == 0 || b.Length == 0) return 0.0;

            var setA = new HashSet<int>(a);
            var setB = new HashSet<int>(b);

            int intersection = setA.Count(x => setB.Contains(x));
            int union = setA.Count + setB.Count - intersection;

            return union == 0 ? 0.0 : (double)intersection / union;
        }

        /// <summary>
        /// Returns all identifier values for index lookups.
        /// Used by the identifier index to map IDs → message references.
        /// </summary>
        public int[] AllIdentifiers()
        {
            return IntentIds.Concat(DomainIds).Concat(DcnIds).ToArray();
        }

        /// <summary>
        /// Serializes to compact pipe-delimited, comma-separated format.
        /// Format: "intentIds|domainIds|dcnIds" e.g. "1,2|3,4|5,6"
        /// </summary>
        public string Serialize()
        {
            return string.Join("|",
                string.Join(",", IntentIds),
                string.Join(",", DomainIds),
                string.Join(",", DcnIds));
        }

        /// <summary>
        /// Deserializes from compact format produced by <see cref="Serialize"/>.
        /// </summary>
        public static DcsKey Deserialize(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new DcsKey();

            var parts = s.Split('|');
            return new DcsKey
            {
                IntentIds = ParseIds(parts.Length > 0 ? parts[0] : ""),
                DomainIds = ParseIds(parts.Length > 1 ? parts[1] : ""),
                DcnIds = ParseIds(parts.Length > 2 ? parts[2] : "")
            };
        }

        private static int[] ParseIds(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return Array.Empty<int>();

            return s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse)
                    .ToArray();
        }
    }
}
