using AiMessagingCore.Abstractions;
using System.Text;

namespace LocalRAG.QaDataset
{
    /// <summary>
    /// Runs QA benchmark questions through an AI session and evaluates answers
    /// against the correct/incorrect prediction lists in the dataset.
    /// </summary>
    public static class QaBenchmarkRunner
    {
        public static async Task<QaBenchmarkReport> RunAsync(
            QaBenchmarkOptions opts,
            QaDatasetDatabase db,
            IChatSession session,
            IProgress<BenchmarkProgressUpdate>? progress = null,
            CancellationToken ct = default)
        {
            var runId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var report = new QaBenchmarkReport { RunId = runId };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Prefer embedded items; fall back to unembedded if needed to reach MaxQuestions
            var items = await db.GetItemsForRunAsync(opts.MaxQuestions, onlyEmbedded: true);
            if (items.Count < opts.MaxQuestions)
            {
                var deficit = opts.MaxQuestions - items.Count;
                var extras = await db.GetItemsForRunAsync(deficit, onlyEmbedded: false);
                var existingIds = new HashSet<int>(items.Select(x => x.Id));
                items.AddRange(extras.Where(x => !existingIds.Contains(x.Id)));
            }

            // Load all embeddings once for RAG context lookup
            List<(int Id, float[] Embedding)>? allEmbeddings = null;
            if (opts.UseRagContext)
                allEmbeddings = await db.GetAllEmbeddingsAsync();

            // Build a lookup of all loaded items for context injection
            var itemLookup = items.ToDictionary(x => x.Id);

            report.TotalRun = items.Count;
            progress?.Report(new BenchmarkProgressUpdate
            {
                Message = $"Starting run {runId} — {items.Count} questions.",
                Total = items.Count
            });

            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[i];

                var preview = item.Question.Length > 60
                    ? item.Question[..60] + "…"
                    : item.Question;

                string? modelResponse = null;
                QaOutcome outcome = QaOutcome.Indeterminate;
                string? matchedAnswer = null;

                try
                {
                    var prompt = BuildPrompt(item, allEmbeddings, itemLookup, opts);
                    var reply = await session.SendAsync(prompt, cancellationToken: ct);
                    modelResponse = reply.Content ?? "";
                    (outcome, matchedAnswer) = EvaluateAnswer(modelResponse, item);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    modelResponse = $"ERROR: {ex.Message}";
                }

                var result = new QaRunResult
                {
                    RunId = runId,
                    QaItemId = item.Id,
                    Question = item.Question,
                    ModelResponse = modelResponse,
                    Outcome = outcome,
                    MatchedAnswer = matchedAnswer,
                    RunAt = DateTime.UtcNow
                };

                await db.SaveRunResultAsync(result);
                report.Results.Add(result);

                switch (outcome)
                {
                    case QaOutcome.Correct:         report.Correct++;         break;
                    case QaOutcome.PossiblyCorrect: report.PossiblyCorrect++; break;
                    case QaOutcome.DefinitelyWrong: report.DefinitelyWrong++; break;
                    default:                        report.Indeterminate++;   break;
                }

                // Emit structured progress with running counts after every answer
                progress?.Report(new BenchmarkProgressUpdate
                {
                    Message          = $"[{i + 1}/{items.Count}] {preview}",
                    Done             = i + 1,
                    Total            = items.Count,
                    Correct          = report.Correct,
                    PossiblyCorrect  = report.PossiblyCorrect,
                    DefinitelyWrong  = report.DefinitelyWrong,
                    Indeterminate    = report.Indeterminate
                });
            }

            sw.Stop();
            report.Duration = sw.Elapsed;
            return report;
        }

        // ── Answer evaluation ─────────────────────────────────────────────────

        /// <summary>
        /// Checks the model response against the three prediction lists in priority order:
        /// 1. answer_and_def_correct_predictions  → Correct
        /// 2. def_incorrect_predictions           → DefinitelyWrong
        /// 3. poss_correct_predictions            → PossiblyCorrect
        /// 4. no match                            → Indeterminate
        /// </summary>
        public static (QaOutcome Outcome, string? MatchedAnswer) EvaluateAnswer(
            string response, QaDatasetItem item)
        {
            var lower = response.ToLowerInvariant();

            foreach (var ans in item.AnswerAndDefCorrectPredictions)
                if (!string.IsNullOrWhiteSpace(ans) && lower.Contains(ans.ToLowerInvariant()))
                    return (QaOutcome.Correct, ans);

            foreach (var ans in item.DefIncorrectPredictions)
                if (!string.IsNullOrWhiteSpace(ans) && lower.Contains(ans.ToLowerInvariant()))
                    return (QaOutcome.DefinitelyWrong, ans);

            foreach (var ans in item.PossCorrectPredictions)
                if (!string.IsNullOrWhiteSpace(ans) && lower.Contains(ans.ToLowerInvariant()))
                    return (QaOutcome.PossiblyCorrect, ans);

            return (QaOutcome.Indeterminate, null);
        }

        // ── Prompt construction ───────────────────────────────────────────────

        private static string BuildPrompt(
            QaDatasetItem item,
            List<(int Id, float[] Embedding)>? allEmbeddings,
            Dictionary<int, QaDatasetItem> itemLookup,
            QaBenchmarkOptions opts)
        {
            var sb = new StringBuilder();

            if (opts.UseRagContext && allEmbeddings != null && item.QuestionEmbedding != null)
            {
                var similarIds = FindSimilarIds(item.QuestionEmbedding, allEmbeddings, item.Id, opts.SimilarContextCount);
                var contextItems = similarIds
                    .Where(id => itemLookup.ContainsKey(id))
                    .Select(id => itemLookup[id])
                    .ToList();

                if (contextItems.Count > 0)
                {
                    sb.AppendLine("Here are some relevant examples:");
                    foreach (var ctx in contextItems)
                    {
                        sb.AppendLine($"Q: {ctx.Question}");
                        sb.AppendLine($"A: {string.Join(", ", ctx.Answers)}");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"Question: {item.Question}");
            sb.Append("Answer:");
            return sb.ToString();
        }

        private static List<int> FindSimilarIds(
            float[] query,
            List<(int Id, float[] Embedding)> all,
            int excludeId,
            int topK)
        {
            return all
                .Where(x => x.Id != excludeId)
                .Select(x => (x.Id, Score: DotProduct(query, x.Embedding)))
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => x.Id)
                .ToList();
        }

        // Both vectors are L2-normalized, so dot product equals cosine similarity.
        private static float DotProduct(float[] a, float[] b)
        {
            var len = Math.Min(a.Length, b.Length);
            float dot = 0f;
            for (int i = 0; i < len; i++) dot += a[i] * b[i];
            return dot;
        }
    }
}
