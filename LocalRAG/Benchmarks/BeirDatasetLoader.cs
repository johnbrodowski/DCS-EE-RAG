using Newtonsoft.Json.Linq;

namespace LocalRAG.Benchmarks
{
    /// <summary>
    /// Converts a downloaded BEIR dataset (the standard retrieval benchmark format:
    /// <c>corpus.jsonl</c>, <c>queries.jsonl</c>, <c>qrels/{split}.tsv</c>) into the
    /// repo's <see cref="BenchmarkDataset"/> schema so it runs through the existing
    /// <see cref="RetrievalBenchmark"/> unchanged.
    ///
    /// Mapping: each corpus doc → a <see cref="KnowledgeEntry"/> (slug = BEIR _id,
    /// request = title + text); each judged query → a <see cref="BenchmarkCase"/>
    /// (expected_slugs = corpus ids with qrel score &gt; 0). Relevance is treated as
    /// binary, matching the benchmark's metric helpers.
    ///
    /// BEIR docs are single passages, so the text lands only in <c>Request</c>;
    /// run the benchmark at <c>SearchLevel = 1</c> for these datasets.
    ///
    /// NOTE: BEIR datasets carry their own (often non-commercial) licenses. They are
    /// a development-time measuring tool only and are never shipped — keep the data
    /// out of the product and out of version control.
    /// </summary>
    public static class BeirDatasetLoader
    {
        public static BenchmarkDataset Load(
            string datasetDir,
            string datasetName = "BEIR",
            string split = "test",
            int? maxQueries = null,
            int? maxCorpus = null,
            bool includeTitle = true)
        {
            var corpusPath  = Path.Combine(datasetDir, "corpus.jsonl");
            var queriesPath = Path.Combine(datasetDir, "queries.jsonl");
            var qrelsPath   = Path.Combine(datasetDir, "qrels", $"{split}.tsv");

            foreach (var p in new[] { corpusPath, queriesPath, qrelsPath })
                if (!File.Exists(p))
                    throw new FileNotFoundException($"Expected BEIR file not found: {p}");

            // 1. qrels: query-id -> relevant corpus-ids (score > 0).
            var qrels = new Dictionary<string, List<string>>();
            bool headerSkipped = false;
            foreach (var line in File.ReadLines(qrelsPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!headerSkipped) { headerSkipped = true; if (line.StartsWith("query-id")) continue; }

                var parts = line.Split('\t');
                if (parts.Length < 3) continue;
                if (!int.TryParse(parts[2].Trim(), out var score) || score <= 0) continue;

                var qid = parts[0].Trim();
                if (!qrels.TryGetValue(qid, out var list)) qrels[qid] = list = new List<string>();
                list.Add(parts[1].Trim());
            }

            // 2. queries: id -> text.
            var queryText = new Dictionary<string, string>();
            foreach (var line in File.ReadLines(queriesPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var o = JObject.Parse(line);
                queryText[(string)o["_id"]!] = (string?)o["text"] ?? "";
            }

            // Deterministic query order (numeric when ids are numeric); optional cap.
            var qids = qrels.Keys
                .Where(queryText.ContainsKey)
                .OrderBy(q => int.TryParse(q, out var n) ? n : int.MaxValue)
                .ThenBy(q => q, StringComparer.Ordinal)
                .ToList();
            if (maxQueries is int mq && mq < qids.Count) qids = qids.Take(mq).ToList();

            // Corpus docs that MUST be present so the selected queries are answerable.
            var required = new HashSet<string>(qids.SelectMany(q => qrels[q]));

            // 3. corpus: always include required docs; fill the rest up to maxCorpus.
            var entries = new List<KnowledgeEntry>();
            var includedIds = new HashSet<string>();
            var filler = new List<KnowledgeEntry>();

            foreach (var line in File.ReadLines(corpusPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var o = JObject.Parse(line);
                var id = (string)o["_id"]!;
                var title = (string?)o["title"] ?? "";
                var text = (string?)o["text"] ?? "";
                var request = includeTitle && !string.IsNullOrWhiteSpace(title)
                    ? $"{title.Trim()}. {text.Trim()}"
                    : text.Trim();

                var entry = new KnowledgeEntry { Slug = id, Request = request, Summary = null };

                if (required.Contains(id)) { entries.Add(entry); includedIds.Add(id); }
                else filler.Add(entry);
            }

            if (maxCorpus is int mc)
            {
                int room = mc - entries.Count;
                if (room > 0)
                {
                    entries.AddRange(filler.Take(room));
                    foreach (var e in filler.Take(room)) includedIds.Add(e.Slug);
                }
            }
            else
            {
                entries.AddRange(filler);
                foreach (var e in filler) includedIds.Add(e.Slug);
            }

            // 4. cases: only keep expected docs that actually made it into the corpus.
            var cases = new List<BenchmarkCase>();
            foreach (var qid in qids)
            {
                var expected = qrels[qid].Where(includedIds.Contains).ToList();
                if (expected.Count == 0) continue; // skip if its relevant docs were trimmed away
                cases.Add(new BenchmarkCase
                {
                    Id              = $"{split}-{qid}",
                    Query           = queryText[qid],
                    ExpectedSlugs   = expected,
                    ExpectRetrieval = true
                });
            }

            return new BenchmarkDataset
            {
                Name             = $"{datasetName} ({split})",
                Version          = "beir",
                Description      = $"BEIR {datasetName} {split} split: {entries.Count} docs, {cases.Count} queries.",
                KnowledgeEntries = entries,
                Cases            = cases
            };
        }
    }
}
