using Newtonsoft.Json;
using System.Diagnostics;
using System.Reflection;

namespace LocalRAG.Benchmarks
{
    public static class EERagBenchmark
    {
        private const string EmbeddedResourceName =
            "LocalRAG.BenchmarkData.eedag_benchmark_v1.json";

        // ── Dataset loading ───────────────────────────────────────────────────

        public static BenchmarkDataset LoadEmbeddedDataset(string resourceName = EmbeddedResourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' not found. " +
                    $"Ensure the file is marked as EmbeddedResource in LocalRAG.csproj.");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<BenchmarkDataset>(json)
                ?? throw new InvalidOperationException("Dataset JSON deserialized to null.");
        }

        public static BenchmarkDataset LoadDataset(string path)
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<BenchmarkDataset>(json)
                ?? throw new InvalidOperationException("Dataset JSON deserialized to null.");
        }

        // ── Database seeding ──────────────────────────────────────────────────

        public static async Task<Dictionary<string, int>> SeedDatabaseAsync(
            BenchmarkDataset dataset,
            EmbeddingDatabaseNew db,
            bool generateEmbeddings = true,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var slugToId = new Dictionary<string, int>();

            for (int i = 0; i < dataset.KnowledgeEntries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var entry = dataset.KnowledgeEntries[i];
                var requestId = $"benchmark:{entry.Slug}";

                progress?.Report(
                    $"[{i + 1}/{dataset.KnowledgeEntries.Count}] Seeding '{entry.Slug}'...");

                await db.AddRequestToEmbeddingDatabaseAsync(
                    requestId, entry.Request, embed: generateEmbeddings);

                await db.UpdateTextResponse(
                    requestId, entry.TextResponse, embed: false);

                if (!string.IsNullOrWhiteSpace(entry.Summary))
                    await db.UpdateSummary(
                        requestId, entry.Summary, embed: generateEmbeddings);

                var record = await db.GetFeedbackDataByRequestIdAsync(requestId);
                if (record != null)
                    slugToId[entry.Slug] = record.Id;
                else
                    progress?.Report(
                        $"  WARNING: Could not resolve Id for slug '{entry.Slug}' after seeding.");
            }

            progress?.Report(
                $"Seeding complete. {slugToId.Count}/{dataset.KnowledgeEntries.Count} entries mapped.");
            return slugToId;
        }

        // ── Benchmark runner ──────────────────────────────────────────────────

        private sealed class CaseOutcome
        {
            public string FinalResponse { get; init; } = string.Empty;
            public string FirstResponse { get; init; } = string.Empty;
            public List<int> CandidateIdsSurfaced { get; init; } = [];
            public List<int> ElectedIds { get; init; } = [];
            public bool RetrievalOccurred => ElectedIds.Count > 0;
        }

        /// <summary>
        /// One EE-RAG turn over the JSON interface: store the query row, search, ask the model
        /// (<paramref name="askModel"/> takes a prompt and returns the reply), and if it elected
        /// entries, retrieve them and ask once more with the retrieved text.
        /// </summary>
        private static async Task<CaseOutcome> RunCaseAsync(
            string query, string requestId, Func<string, Task<string>> askModel,
            EmbeddingDatabaseNew db, int topK, bool silent)
        {
            await db.AddRequestToEmbeddingDatabaseAsync(requestId, query, embed: false);

            async Task<Newtonsoft.Json.Linq.JObject> Call(object request) =>
                Newtonsoft.Json.Linq.JObject.Parse(
                    await EERagPipeline.HandleAsync(JsonConvert.SerializeObject(request), db));

            var search = await Call(new { op = "search", requestId, query, topK });
            var surfaced = search["candidates"]!.Select(c => (int)c["id"]!).ToList();
            var reply = await askModel($"{query}\n\n{(string)search["header"]!}");

            if (EERagPipeline.IsMoreRequest(reply))
            {
                search = await Call(new { op = "search", requestId, query, topK, more = true });
                surfaced = search["candidates"]!.Select(c => (int)c["id"]!).ToList();
                reply = await askModel($"{query}\n\n{(string)search["header"]!}");
            }

            var first = reply;
            bool refusedMore = EERagPipeline.IsMoreRequest(reply); // a second MORE is refused
            var ids = refusedMore ? [] : EERagPipeline.ParseRetrieveIds(reply);

            var retrieve = await Call(new { op = "retrieve", requestId, ids, silent });
            var elected = retrieve["electedIds"]!.Select(t => (int)t).ToList();

            string final = first;
            if (elected.Count > 0)
                final = await askModel($"{query}\n\n{(string)retrieve["context"]!}") + (string)retrieve["tags"]!;
            else if (ids.Count > 0 || refusedMore)
                final = await askModel(
                    $"{query}\n\nNo additional context is available for this request. Answer directly; do not ask to retrieve anything.");

            return new CaseOutcome
            {
                FinalResponse = final,
                FirstResponse = first,
                CandidateIdsSurfaced = surfaced,
                ElectedIds = elected
            };
        }

        public static async Task<BenchmarkReport> RunAsync(
            BenchmarkDataset dataset,
            Dictionary<string, int> slugToId,
            Func<string, Task<string>> askModel,
            EmbeddingDatabaseNew db,
            BenchmarkOptions? options = null,
            IProgress<string>? progress = null,
            IProgress<CaseResult>? caseProgress = null,
            CancellationToken ct = default)
        {
            options ??= new BenchmarkOptions();
            var totalSw = Stopwatch.StartNew();
            var caseResults = new List<CaseResult>();

            for (int i = 0; i < dataset.Cases.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var benchCase = dataset.Cases[i];
                progress?.Report(
                    $"[{i + 1}/{dataset.Cases.Count}] Case {benchCase.Id}: {benchCase.Query}");

                var caseSw = Stopwatch.StartNew();
                var requestId = $"bench-run:{benchCase.Id}:{Guid.NewGuid():N}";

                // askModel is the calling AI. Each case is independent: askModel must not carry
                // conversation history from one case into the next.
                var result = await RunCaseAsync(
                    benchCase.Query, requestId, askModel, db, options.TopK, options.SilentMode);

                caseSw.Stop();

                var expectedIds = benchCase.ExpectedSlugs
                    .Where(s => slugToId.ContainsKey(s))
                    .Select(s => slugToId[s])
                    .ToList();

                var surfacedIds = result.CandidateIdsSurfaced;
                var electedIds = result.ElectedIds;

                double candidateRecall = ComputeCandidateRecall(expectedIds, surfacedIds);
                double precision       = ComputePrecision(expectedIds, electedIds);
                double recall          = ComputeRecall(expectedIds, electedIds);
                double f1              = ComputeF1(precision, recall);

                bool hintMatched = benchCase.ExpectedAnswerHint != null &&
                    result.FinalResponse.Contains(
                        benchCase.ExpectedAnswerHint,
                        StringComparison.OrdinalIgnoreCase);

                var caseResult = new CaseResult
                {
                    CaseId            = benchCase.Id,
                    Query             = benchCase.Query,
                    ExpectedIds       = expectedIds,
                    SurfacedIds       = surfacedIds,
                    ElectedIds        = electedIds,
                    CandidateRecall   = candidateRecall,
                    Precision         = precision,
                    Recall            = recall,
                    F1                = f1,
                    ExpectedRetrieval = benchCase.ExpectRetrieval,
                    ActualRetrieval   = result.RetrievalOccurred,
                    Duration          = caseSw.Elapsed,
                    AnswerHint        = benchCase.ExpectedAnswerHint,
                    AnswerHintMatched = hintMatched,
                    FirstResponse     = result.FirstResponse
                };

                caseResults.Add(caseResult);
                caseProgress?.Report(caseResult);
            }

            totalSw.Stop();
            return BuildReport(dataset.Name, caseResults, totalSw.Elapsed);
        }

        // ── Scoring helpers ───────────────────────────────────────────────────

        private static double ComputeCandidateRecall(List<int> expected, List<int> surfaced)
        {
            if (expected.Count == 0) return 1.0;
            var intersection = expected.Intersect(surfaced).Count();
            return (double)intersection / expected.Count;
        }

        private static double ComputePrecision(List<int> expected, List<int> elected)
        {
            if (elected.Count == 0) return expected.Count == 0 ? 1.0 : 0.0;
            var intersection = expected.Intersect(elected).Count();
            return (double)intersection / elected.Count;
        }

        private static double ComputeRecall(List<int> expected, List<int> elected)
        {
            if (expected.Count == 0) return 1.0;
            var intersection = expected.Intersect(elected).Count();
            return (double)intersection / expected.Count;
        }

        private static double ComputeF1(double precision, double recall)
        {
            if (precision + recall == 0.0) return 0.0;
            return 2.0 * precision * recall / (precision + recall);
        }

        private static BenchmarkReport BuildReport(
            string datasetName,
            List<CaseResult> results,
            TimeSpan totalDuration)
        {
            return new BenchmarkReport
            {
                RunAt                  = DateTime.UtcNow,
                TotalDuration          = totalDuration,
                TotalCases             = results.Count,
                DatasetName            = datasetName,
                CaseResults            = results,
                MeanCandidateRecall    = results.Count > 0
                    ? results.Average(r => r.CandidateRecall) : 0,
                MeanRetrievalPrecision = results.Count > 0
                    ? results.Average(r => r.Precision) : 0,
                MeanRetrievalRecall    = results.Count > 0
                    ? results.Average(r => r.Recall) : 0,
                MeanF1                 = results.Count > 0
                    ? results.Average(r => r.F1) : 0,
                RetrievalRateActual    = results.Count > 0
                    ? (double)results.Count(r => r.ActualRetrieval) / results.Count : 0,
                RetrievalRateExpected  = results.Count > 0
                    ? (double)results.Count(r => r.ExpectedRetrieval) / results.Count : 0
            };
        }
    }
}
