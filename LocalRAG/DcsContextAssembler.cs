using System.Diagnostics;
using System.Text;

namespace LocalRAG
{
    /// <summary>
    /// Main orchestrator for Dynamic Context Selection.
    /// Manages DCN lifecycle, message registration, identifier indexing,
    /// and context assembly under context pressure constraints.
    /// </summary>
    public class DcsContextAssembler
    {
        /// <summary>Maximum messages per DCN during context assembly.</summary>
        public const int MAX_PER_DCN = 20;

        /// <summary>Similarity threshold for merging two DCNs.</summary>
        public const double MERGE_THRESHOLD = 0.75;

        /// <summary>Minimum similarity for a message to be eligible for context.</summary>
        public const double ELIGIBILITY_THRESHOLD = 0.1;

        private readonly Dictionary<int, DcnNode> _dcnStore = new();
        private readonly Dictionary<int, List<string>> _identifierIndex = new();
        private readonly Dictionary<string, DcsMessageRecord> _messageStore = new();
        private readonly List<DcsLogEntry> _log = [];
        private int _nextDcnId = 1;

        /// <summary>Read-only access to the selection log for diagnostics.</summary>
        public IReadOnlyList<DcsLogEntry> Log => _log.AsReadOnly();

        /// <summary>Read-only access to registered DCNs.</summary>
        public IReadOnlyDictionary<int, DcnNode> DcnStore => _dcnStore;

        /// <summary>Read-only access to registered messages.</summary>
        public IReadOnlyDictionary<string, DcsMessageRecord> MessageStore => _messageStore;

        // ── Message Registration ────────────────────────────────────────────

        /// <summary>
        /// Registers a message and updates the identifier index.
        /// Touches referenced DCNs to keep recency weights current.
        /// </summary>
        public void RegisterMessage(DcsMessageRecord msg)
        {
            _messageStore[msg.MessageId] = msg;
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Register > Message \"{msg.MessageId}\" key={msg.Key.Serialize()} dcns=[{string.Join(",", msg.HardLinkedDcns)}]");

            // Update identifier index for O(1) candidate lookup
            foreach (var id in msg.Key.AllIdentifiers())
            {
                if (!_identifierIndex.ContainsKey(id))
                    _identifierIndex[id] = new List<string>();

                _identifierIndex[id].Add(msg.MessageId);
                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Register >   Index updated: id {id} -> {_identifierIndex[id].Count} messages");
            }

            // Touch referenced DCNs
            foreach (var dcnId in msg.HardLinkedDcns)
            {
                if (_dcnStore.TryGetValue(dcnId, out var dcn))
                {
                    dcn.TouchReference();
                    if (!dcn.LinkedMessageIds.Contains(msg.MessageId))
                        dcn.LinkedMessageIds.Add(msg.MessageId);
                    Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Register >   Linked to DCN {dcnId} (recency={dcn.ComputeRecencyWeight():F3}, now {dcn.LinkedMessageIds.Count} messages)");
                }
                else
                {
                    Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Register >   DCN {dcnId} not found in store (orphaned reference)");
                }
            }

            // Retroactively compute influence on recent prior messages
            UpdateInfluenceRetroactively(msg);
        }

        // ── Context Assembly ────────────────────────────────────────────────

        /// <summary>
        /// Assembles a bounded context set for a query key.
        /// Uses the identifier index for candidate lookup, applies weighted similarity
        /// with recency decay, and enforces per-DCN caps.
        /// </summary>
        public List<DcsMessageRecord> AssembleContext(DcsKey queryKey, int maxTotal = 50)
        {
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble > Query key: intents=[{string.Join(",", queryKey.IntentIds)}] domains=[{string.Join(",", queryKey.DomainIds)}] dcns=[{string.Join(",", queryKey.DcnIds)}]");

            // Step 1: Find candidates via identifier index (not full scan)
            var candidateIds = new HashSet<string>();
            var allIds = queryKey.AllIdentifiers();
            foreach (var id in allIds)
            {
                if (_identifierIndex.TryGetValue(id, out var ids))
                {
                    foreach (var msgId in ids)
                        candidateIds.Add(msgId);
                }
            }

            var candidateMessages = candidateIds
                .Where(id => _messageStore.ContainsKey(id))
                .Select(id => _messageStore[id])
                .ToList();

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble > Step 1: Index lookup found {candidateMessages.Count} candidates from {allIds.Length} identifiers ({_messageStore.Count} total messages)");

            // Step 2: Score each candidate (similarity × recency decay)
            var scored = new List<(DcsMessageRecord Message, double Score)>();
            int skippedCount = 0;

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble > Step 2: Scoring {candidateMessages.Count} candidates...");
            foreach (var msg in candidateMessages)
            {
                double similarity = msg.Key.Similarity(queryKey);

                if (similarity < ELIGIBILITY_THRESHOLD)
                {
                    Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble >   {msg.MessageId}: similarity={similarity:F3} < threshold {ELIGIBILITY_THRESHOLD}, skipped");
                    skippedCount++;
                    continue;
                }

                // Apply DCN recency decay
                double decayMultiplier = 1.0;
                foreach (var dcnId in msg.HardLinkedDcns)
                {
                    if (_dcnStore.TryGetValue(dcnId, out var dcn))
                    {
                        decayMultiplier = Math.Max(decayMultiplier, dcn.ComputeRecencyWeight());
                    }
                }

                var finalScore = similarity * decayMultiplier;
                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble >   {msg.MessageId}: similarity={similarity:F3} x decay={decayMultiplier:F3} -> score={finalScore:F3}");
                scored.Add((msg, finalScore));
            }

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble > Step 2: {scored.Count} candidates passed threshold ({skippedCount} filtered out)");

            // Step 3: Group by DCN and apply per-DCN cap
            var result = new List<DcsMessageRecord>();
            var dcnCounts = new Dictionary<int, int>();
            int rejectedByCap = 0;

            foreach (var (msg, _) in scored.OrderByDescending(s => s.Score))
            {
                if (result.Count >= maxTotal)
                    break;

                // Check per-DCN cap
                bool withinCap = true;
                foreach (var dcnId in msg.HardLinkedDcns)
                {
                    dcnCounts.TryGetValue(dcnId, out int count);
                    if (count >= MAX_PER_DCN)
                    {
                        Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble >   {msg.MessageId} rejected: DCN {dcnId} at cap ({count}/{MAX_PER_DCN})");
                        withinCap = false;
                        rejectedByCap++;
                        break;
                    }
                }

                if (!withinCap)
                    continue;

                result.Add(msg);
                foreach (var dcnId in msg.HardLinkedDcns)
                {
                    dcnCounts.TryGetValue(dcnId, out int count);
                    dcnCounts[dcnId] = count + 1;
                }
            }

            if (dcnCounts.Count > 0)
            {
                var dcnSummary = string.Join(", ", dcnCounts.Select(kv => $"DCN {kv.Key}={kv.Value} msgs"));
                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble > Step 3: Per-DCN cap (max {MAX_PER_DCN}): {dcnSummary}{(rejectedByCap > 0 ? $", {rejectedByCap} rejected by cap" : "")}");
            }

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Assemble > Result: {result.Count} messages selected (max {maxTotal})");

            // Step 4: Log the selection decision
            bool usedIndex = allIds.Length > 0;
            LogSelectionDecision(queryKey, candidateMessages.Count, result.Count, scored, usedIndex);

            return result;
        }

        // ── DCN Management ──────────────────────────────────────────────────

        /// <summary>
        /// Finds an existing DCN that matches the key or creates a new one.
        /// </summary>
        public DcnNode FindOrCreateDcn(DcsKey key)
        {
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] DCN > Searching {_dcnStore.Count} existing DCNs for match...");

            // Try to find a matching DCN
            DcnNode? bestMatch = null;
            double bestScore = 0;

            foreach (var dcn in _dcnStore.Values)
            {
                // Compare topic identifiers directly using Jaccard similarity.
                // TopicIdentifiers stores IntentIds ∪ DomainIds as a flat set,
                // and since intent/domain ID ranges overlap (both start at 1),
                // we compare the combined sets rather than trying to decompose.
                var dcnTopics = new HashSet<int>(dcn.TopicIdentifiers);
                var msgTopics = new HashSet<int>(key.IntentIds.Concat(key.DomainIds).Distinct());
                var similarity = DcsKey.Jaccard(dcnTopics.ToArray(), msgTopics.ToArray());
                var recency = dcn.ComputeRecencyWeight();
                var score = similarity * recency;

                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] DCN >   DCN {dcn.DcnId}: similarity={similarity:F3} x recency={recency:F3} -> score={score:F3}{(score > bestScore && score >= ELIGIBILITY_THRESHOLD ? " <- best" : "")}");

                if (score > bestScore && score >= ELIGIBILITY_THRESHOLD)
                {
                    bestScore = score;
                    bestMatch = dcn;
                }
            }

            if (bestMatch != null)
            {
                bestMatch.TouchReference();
                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] DCN > Matched existing DCN {bestMatch.DcnId} (score={bestScore:F3})");
                return bestMatch;
            }

            // Create new DCN
            var newDcn = new DcnNode
            {
                DcnId = _nextDcnId++,
                TopicIdentifiers = key.IntentIds.Concat(key.DomainIds).Distinct().ToArray()
            };

            _dcnStore[newDcn.DcnId] = newDcn;
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] DCN > No match above threshold {ELIGIBILITY_THRESHOLD}, creating new DCN {newDcn.DcnId} with topics=[{string.Join(",", newDcn.TopicIdentifiers)}]");
            return newDcn;
        }

        /// <summary>
        /// Merges DCNs whose topic similarity exceeds the merge threshold.
        /// Combines TopicIdentifiers, reassigns messages, keeps the older DCN ID.
        /// </summary>
        public void MergeSimilarDcns()
        {
            var dcns = _dcnStore.Values.ToList();
            var toRemove = new HashSet<int>();
            int mergeCount = 0;

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Merge > Checking {dcns.Count} DCNs for merge candidates (threshold={MERGE_THRESHOLD})...");

            for (int i = 0; i < dcns.Count; i++)
            {
                if (toRemove.Contains(dcns[i].DcnId))
                    continue;

                for (int j = i + 1; j < dcns.Count; j++)
                {
                    if (toRemove.Contains(dcns[j].DcnId))
                        continue;

                    var sim = ComputeTopicSimilarity(dcns[i], dcns[j]);

                    if (sim >= MERGE_THRESHOLD)
                    {
                        // Keep older DCN (lower ID = created first)
                        var keeper = dcns[i].DcnId < dcns[j].DcnId ? dcns[i] : dcns[j];
                        var merged = dcns[i].DcnId < dcns[j].DcnId ? dcns[j] : dcns[i];

                        Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Merge >   DCN {dcns[i].DcnId} x DCN {dcns[j].DcnId}: similarity={sim:F3} >= {MERGE_THRESHOLD} -> MERGING (keeper={keeper.DcnId}, absorbed={merged.DcnId})");

                        keeper.Absorb(merged);

                        // Reassign messages from merged to keeper
                        int reassigned = 0;
                        foreach (var msgId in merged.LinkedMessageIds)
                        {
                            if (_messageStore.TryGetValue(msgId, out var msg))
                            {
                                msg.HardLinkedDcns = msg.HardLinkedDcns
                                    .Select(id => id == merged.DcnId ? keeper.DcnId : id)
                                    .Distinct()
                                    .ToArray();
                                reassigned++;
                            }
                        }

                        Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Merge >   Reassigned {reassigned} messages from DCN {merged.DcnId} -> DCN {keeper.DcnId}");
                        toRemove.Add(merged.DcnId);
                        mergeCount++;
                    }
                    else
                    {
                        Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Merge >   DCN {dcns[i].DcnId} x DCN {dcns[j].DcnId}: similarity={sim:F3} < {MERGE_THRESHOLD}");
                    }
                }
            }

            foreach (var id in toRemove)
                _dcnStore.Remove(id);

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Merge > Complete: {mergeCount} merge(s) performed, {_dcnStore.Count} DCNs remain");
        }

        /// <summary>
        /// Computes topic similarity between two DCNs using their topic identifiers.
        /// </summary>
        public double ComputeTopicSimilarity(DcnNode a, DcnNode b)
        {
            return DcsKey.Jaccard(a.TopicIdentifiers, b.TopicIdentifiers);
        }

        // ── Influence Scoring ───────────────────────────────────────────────

        /// <summary>
        /// Computes behavioral influence between two sequential messages.
        /// Based on DCN overlap, domain propagation, and intent escalation.
        /// </summary>
        public double ComputeInfluence(DcsMessageRecord current, DcsMessageRecord next)
        {
            double score = 0.0;

            // Shared DCN → strong influence
            bool sharedDcn = current.HardLinkedDcns.Intersect(next.HardLinkedDcns).Any();
            if (sharedDcn) score += 0.6;

            // Domain identifier propagation
            bool domainOverlap = current.Key.DomainIds.Intersect(next.Key.DomainIds).Any();
            if (domainOverlap) score += 0.2;

            // Intent escalation (CHAT → DESIGN/FIX)
            bool escalation = DcsIntentClassifier.IsEscalation(current.Key.IntentIds, next.Key.IntentIds);
            if (escalation) score += 0.3;

            var result = Math.Min(score, 1.0);

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence > {current.MessageId} -> {next.MessageId}:");
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence >   Shared DCN: {(sharedDcn ? "yes (+0.6)" : "no (+0.0)")}");
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence >   Domain overlap: {(domainOverlap ? "yes (+0.2)" : "no (+0.0)")}");
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence >   Intent escalation: {(escalation ? "yes (+0.3)" : "no (+0.0)")}");
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence >   Total: {result:F3}{(score > 1.0 ? " (capped at 1.0)" : "")}");

            return result;
        }

        // ── Function Hash Suggestion ────────────────────────────────────────

        /// <summary>
        /// Surfaces a previously implemented function as a suggestion in context.
        /// Does NOT skip generation — the model sees both the suggestion and generates fresh output.
        /// </summary>
        public static void SurfaceFunctionMatch(string code, StringBuilder context)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] FunctionHash > Surfacing previously implemented function ({code.Length} chars)");
                context.AppendLine("--- Previously Implemented Function (suggestion only — do not skip generation) ---");
                context.AppendLine(code);
                context.AppendLine("---");
            }
        }

        // ── Logging ─────────────────────────────────────────────────────────

        private void LogSelectionDecision(
            DcsKey queryKey,
            int candidateCount,
            int filteredCount,
            List<(DcsMessageRecord Message, double Score)> scored,
            bool usedIndex)
        {
            // Determine primary selected DCN
            int? selectedDcnId = null;
            if (queryKey.DcnIds.Length > 0)
                selectedDcnId = queryKey.DcnIds[0];

            var topScores = scored
                .OrderByDescending(s => s.Score)
                .Take(5)
                .Select(s => (s.Message.MessageId, s.Score))
                .ToList();

            var entry = new DcsLogEntry
            {
                SelectedDcnId = selectedDcnId,
                CandidateMessageCount = candidateCount,
                FilteredMessageCount = filteredCount,
                TopScores = topScores,
                Strategy = usedIndex ? "Selective" : "Full"
            };

            _log.Add(entry);
        }

        // ── Private Helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Retroactively computes influence between the new message and the 1–2 prior messages.
        /// </summary>
        private void UpdateInfluenceRetroactively(DcsMessageRecord newMsg)
        {
            var recentMessages = _messageStore.Values
                .Where(m => m.MessageId != newMsg.MessageId)
                .OrderByDescending(m => m.TimestampUtc)
                .Take(2)
                .ToList();

            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence > Retroactive check for {newMsg.MessageId} against {recentMessages.Count} prior messages");

            // Influence is computed but not stored in a separate structure —
            // it feeds into the context assembly scoring through DCN linkage.
            // Future enhancement: persist influence scores for weighted retrieval.
            foreach (var prior in recentMessages)
            {
                var influence = ComputeInfluence(prior, newMsg);

                // If strong influence, ensure they share DCN linkage
                if (influence >= 0.6)
                {
                    var sharedDcns = prior.HardLinkedDcns
                        .Except(newMsg.HardLinkedDcns)
                        .ToArray();

                    if (sharedDcns.Length > 0 && newMsg.HardLinkedDcns.Length == 0)
                    {
                        newMsg.HardLinkedDcns = newMsg.HardLinkedDcns
                            .Concat(sharedDcns)
                            .Distinct()
                            .ToArray();
                        Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence >   vs {prior.MessageId}: score={influence:F3} >= 0.6 -> propagating DCN links [{string.Join(",", sharedDcns)}] to {newMsg.MessageId}");
                    }
                    else
                    {
                        Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence >   vs {prior.MessageId}: score={influence:F3} >= 0.6 but no new DCN links to propagate");
                    }
                }
                else
                {
                    Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Influence >   vs {prior.MessageId}: score={influence:F3} < 0.6 -> no propagation");
                }
            }
        }
    }
}
