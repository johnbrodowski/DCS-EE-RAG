using System.Diagnostics;

namespace LocalRAG
{
    /// <summary>
    /// Dynamic Context Node — a continuity anchor representing an evolving line of work.
    /// Unlike static topic labels, DCNs evolve over time and absorb related events.
    /// They represent continuity of intent rather than strict semantic boundaries.
    /// </summary>
    public class DcnNode
    {
        /// <summary>Unique identifier for this DCN.</summary>
        public int DcnId { get; set; }

        /// <summary>
        /// Topic identifiers that define this node's scope.
        /// Combined from all messages assigned to this DCN.
        /// </summary>
        public int[] TopicIdentifiers { get; set; } = Array.Empty<int>();

        /// <summary>When this DCN was first created.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>When this DCN was last referenced by a message.</summary>
        public DateTime LastReferencedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>IDs of all messages linked to this DCN.</summary>
        public List<string> LinkedMessageIds { get; set; } = [];

        /// <summary>
        /// Computes time-based recency weight using exponential decay.
        /// 24-hour half-life: a DCN referenced now scores 1.0;
        /// one referenced 24 hours ago scores ~0.37; 48 hours ago ~0.14.
        /// </summary>
        public double ComputeRecencyWeight()
        {
            var age = DateTime.UtcNow - LastReferencedUtc;
            return Math.Exp(-age.TotalHours / 24.0);
        }

        /// <summary>
        /// Updates the last-referenced timestamp to now.
        /// Call when a new message references this DCN.
        /// </summary>
        public void TouchReference()
        {
            LastReferencedUtc = DateTime.UtcNow;
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] DCN > TouchReference: DCN {DcnId} updated to {LastReferencedUtc:HH:mm:ss.fff}");
        }

        /// <summary>
        /// Absorbs another DCN's topic identifiers and message links.
        /// Used during merge operations to prevent topic fragmentation.
        /// </summary>
        public void Absorb(DcnNode other)
        {
            var topicsBefore = TopicIdentifiers.Length;
            var msgsBefore = LinkedMessageIds.Count;

            TopicIdentifiers = TopicIdentifiers
                .Union(other.TopicIdentifiers)
                .ToArray();

            foreach (var msgId in other.LinkedMessageIds)
            {
                if (!LinkedMessageIds.Contains(msgId))
                    LinkedMessageIds.Add(msgId);
            }

            // Keep the earlier creation time
            if (other.CreatedUtc < CreatedUtc)
                CreatedUtc = other.CreatedUtc;

            // Keep the more recent reference time
            if (other.LastReferencedUtc > LastReferencedUtc)
                LastReferencedUtc = other.LastReferencedUtc;

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] DCN > Absorb: DCN {DcnId} absorbing DCN {other.DcnId} — topics before: [{string.Join(",", TopicIdentifiers.Take(topicsBefore))}], after: [{string.Join(",", TopicIdentifiers)}], messages before: {msgsBefore}, after: {LinkedMessageIds.Count}");
        }
    }
}
