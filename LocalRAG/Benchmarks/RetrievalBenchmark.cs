using System.Diagnostics;

namespace LocalRAG.Benchmarks
{
    /// <summary>
    /// LLM-free, deterministic retrieval-quality benchmark. It scores the ranked
    /// candidate list produced by <see cref="EmbeddingDatabaseNew.SearchEmbeddingsAsync"/>
    /// — the exact surface being tuned — against the ground-truth slugs in a
    /// <see cref="BenchmarkDataset"/>, using ranking-aware metrics (Recall@k,
    /// Precision@k, MRR, nDCG@k). Mirrors the structure of <see cref="EERagBenchmark"/>
    /// and reuses its dataset model and seeding helper.
    /// </summary>
    public static class RetrievalBenchmark
    {
        // ── Single run over an already-seeded database ───────────────────────────

        public static async Task<RetrievalReport> RunAsync(
            BenchmarkDataset dataset,
            Dictionary<string, int> slugToId,
            EmbeddingDatabaseNew db,
            RetrievalBenchmarkOptions? options = null,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            options ??= new RetrievalBenchmarkOptions();
            var sw = Stopwatch.StartNew();
            var caseResults = new List<RetrievalCaseResult>();

            for (int i = 0; i < dataset.Cases.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var c = dataset.Cases[i];
                progress?.Report($"[{i + 1}/{dataset.Cases.Count}] {c.Id}: {c.Query}");

                var expectedIds = c.ExpectedSlugs
                    .Where(slugToId.ContainsKey)
                    .Select(s => slugToId[s])
                    .ToList();

                // The thing under test — no LLM, just the ranked candidate list.
                var hits = await db.SearchEmbeddingsAsync(
                    c.Query, options.TopK, options.MinimumSimilarity, options.SearchLevel);

                var rankedIds = hits.Select(h => h.Id).ToList();
                double topSim = hits.Count > 0 ? (hits[0].Similarity ?? 0.0) : 0.0;

                var cr = new RetrievalCaseResult
                {
                    CaseId      = c.Id,
                    Query       = c.Query,
                    ExpectedIds = expectedIds,
                    RankedIds   = rankedIds,
                    IsNegative  = expectedIds.Count == 0
                };

                if (cr.IsNegative)
                {
                    // No relevant doc exists; the only signal is whether retrieval
                    // wrongly surfaced something with high confidence.
                    cr.SurfacedAboveFloor = topSim >= options.NegativeSurfaceFloor;
                }
                else
                {
                    cr.ReciprocalRank = ReciprocalRank(expectedIds, rankedIds);
                    foreach (var k in options.Ks)
                    {
                        cr.RecallAtK[k]    = RecallAtK(expectedIds, rankedIds, k);
                        cr.PrecisionAtK[k] = PrecisionAtK(expectedIds, rankedIds, k);
                        cr.NdcgAtK[k]      = NdcgAtK(expectedIds, rankedIds, k);
                    }
                }

                caseResults.Add(cr);
            }

            sw.Stop();
            return BuildReport(dataset.Name, caseResults, options, sw.Elapsed);
        }

        // ── Config sweep: fresh seeded DB per variant ────────────────────────────

        /// <summary>
        /// Runs the benchmark once per named <see cref="RAGConfiguration"/> variant.
        /// Each variant gets its own temp-DB-backed <see cref="EmbeddingDatabaseNew"/>,
        /// freshly seeded (embeddings regenerated under that config), so knobs that
        /// affect embedding (pooling, MaxSequenceLength, stop-word removal) are
        /// honoured. Callers must set ModelPath/VocabularyPath on each config;
        /// DatabasePath is overridden here.
        /// </summary>
        public static async Task<List<RetrievalReport>> SweepAsync(
            BenchmarkDataset dataset,
            IEnumerable<(string Label, RAGConfiguration Config)> variants,
            RetrievalBenchmarkOptions baseOptions,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var reports = new List<RetrievalReport>();

            foreach (var (label, config) in variants)
            {
                ct.ThrowIfCancellationRequested();

                var tempDb = Path.Combine(Path.GetTempPath(), $"retr_bench_{Guid.NewGuid():N}.db");
                config.DatabasePath = tempDb;

                EmbeddingDatabaseNew? db = null;
                try
                {
                    progress?.Report($"=== Variant '{label}' — seeding ===");
                    db = new EmbeddingDatabaseNew(config);
                    await Task.Delay(500, ct); // let async init/queue settle (mirrors existing tests)

                    var slugToId = await EERagBenchmark.SeedDatabaseAsync(
                        dataset, db, generateEmbeddings: true, progress, ct);

                    var opts = baseOptions.Clone();
                    opts.Label = label;
                    opts.ConfigSummary = SummarizeConfig(config);

                    var report = await RunAsync(dataset, slugToId, db, opts, progress, ct);
                    reports.Add(report);
                }
                finally
                {
                    if (db != null) await db.DisposeAsync();
                    await Task.Delay(200, ct);
                    TryDelete(tempDb);
                }
            }

            return reports;
        }

        public static string SummarizeConfig(RAGConfiguration c) =>
            $"seq={c.MaxSequenceLength};stop={c.RemoveStopWords};lower={c.LowercaseInput};" +
            $"htables={c.NumberOfHashTables};hfuncs={c.NumberOfHashFunctions};" +
            $"wps={c.WordsPerString};overlap={c.OverlapPercentage}";

        // ── Metric helpers (binary relevance) ────────────────────────────────────

        private static double RecallAtK(List<int> expected, List<int> ranked, int k)
        {
            if (expected.Count == 0) return 1.0;
            int hit = ranked.Take(k).Count(expected.Contains);
            return (double)hit / expected.Count;
        }

        private static double PrecisionAtK(List<int> expected, List<int> ranked, int k)
        {
            if (k <= 0) return 0.0;
            int hit = ranked.Take(k).Count(expected.Contains);
            return (double)hit / k;
        }

        private static double ReciprocalRank(List<int> expected, List<int> ranked)
        {
            for (int i = 0; i < ranked.Count; i++)
                if (expected.Contains(ranked[i]))
                    return 1.0 / (i + 1);
            return 0.0;
        }

        private static double NdcgAtK(List<int> expected, List<int> ranked, int k)
        {
            double dcg = 0.0;
            int limit = Math.Min(k, ranked.Count);
            for (int i = 0; i < limit; i++)
                if (expected.Contains(ranked[i]))
                    dcg += 1.0 / Math.Log2(i + 2);

            double idcg = 0.0;
            int ideal = Math.Min(k, expected.Count);
            for (int i = 0; i < ideal; i++)
                idcg += 1.0 / Math.Log2(i + 2);

            return idcg == 0.0 ? 0.0 : dcg / idcg;
        }

        // ── Aggregation ──────────────────────────────────────────────────────────

        private static RetrievalReport BuildReport(
            string datasetName,
            List<RetrievalCaseResult> results,
            RetrievalBenchmarkOptions opts,
            TimeSpan duration)
        {
            var positives = results.Where(r => !r.IsNegative).ToList();
            var negatives = results.Where(r => r.IsNegative).ToList();

            var report = new RetrievalReport
            {
                RunAt         = DateTime.UtcNow,
                DatasetName   = datasetName,
                Label         = opts.Label,
                ConfigSummary = opts.ConfigSummary,
                TotalCases    = results.Count,
                PositiveCases = positives.Count,
                NegativeCases = negatives.Count,
                Duration      = duration,
                Ks            = opts.Ks,
                CaseResults   = results,
                Mrr           = positives.Count > 0 ? positives.Average(r => r.ReciprocalRank) : 0.0,
                NegativeFalsePositiveRate = negatives.Count > 0
                    ? (double)negatives.Count(n => n.SurfacedAboveFloor) / negatives.Count
                    : 0.0
            };

            foreach (var k in opts.Ks)
            {
                report.MeanRecallAtK[k]    = positives.Count > 0 ? positives.Average(r => r.RecallAtK[k]) : 0.0;
                report.MeanPrecisionAtK[k] = positives.Count > 0 ? positives.Average(r => r.PrecisionAtK[k]) : 0.0;
                report.MeanNdcgAtK[k]      = positives.Count > 0 ? positives.Average(r => r.NdcgAtK[k]) : 0.0;
            }

            report.MeanCandidateRecall = positives.Count > 0
                ? positives.Average(r => RecallAtK(r.ExpectedIds, r.RankedIds, opts.TopK))
                : 0.0;

            return report;
        }

        private static void TryDelete(string path)
        {
            for (int i = 0; i < 3; i++)
            {
                try { if (File.Exists(path)) File.Delete(path); return; }
                catch (IOException) { Thread.Sleep(100); }
            }
        }
    }
}
