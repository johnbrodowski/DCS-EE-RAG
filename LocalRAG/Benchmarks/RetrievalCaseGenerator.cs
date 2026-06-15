using AiMessagingCore.Abstractions;
using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalRAG.Benchmarks
{
    /// <summary>
    /// Generates retrieval benchmark cases (query → expected slug) from a
    /// <see cref="BenchmarkDataset"/>'s knowledge entries. Two modes:
    /// a deterministic offline derivation (no model, no API) and an optional
    /// LLM-assisted paraphrase generator. Output uses the same
    /// <see cref="BenchmarkDataset"/> schema so it is consumed by both this
    /// benchmark and <see cref="EERagBenchmark"/>. Generation is a one-time /
    /// on-demand step; the committed JSON keeps the benchmark deterministic.
    /// </summary>
    public static class RetrievalCaseGenerator
    {
        // ── Deterministic, offline ───────────────────────────────────────────────

        /// <summary>
        /// Derives cases from each knowledge entry without any model: a
        /// summary/first-sentence query plus a keyword-only lexical variant.
        /// The original curated cases (if any) are preserved and merged, deduped
        /// by normalized query text.
        /// </summary>
        public static BenchmarkDataset GenerateDeterministic(
            BenchmarkDataset source,
            bool keepOriginalCases = true,
            string? name = null)
        {
            var cases = new List<BenchmarkCase>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;

            void TryAdd(string query, string slug)
            {
                query = CollapseWhitespace(query);
                if (string.IsNullOrWhiteSpace(query)) return;
                if (!seen.Add(Normalize(query))) return;
                cases.Add(new BenchmarkCase
                {
                    Id             = $"gen-{++n:D3}",
                    Query          = query,
                    ExpectedSlugs  = new List<string> { slug },
                    ExpectRetrieval = true
                });
            }

            if (keepOriginalCases)
            {
                foreach (var c in source.Cases)
                {
                    var key = Normalize(c.Query);
                    if (seen.Add(key)) cases.Add(c);
                }
            }

            foreach (var e in source.KnowledgeEntries)
            {
                var primary = !string.IsNullOrWhiteSpace(e.Summary)
                    ? e.Summary!
                    : FirstSentence(e.Request);
                TryAdd(primary, e.Slug);

                var keywords = KeywordQuery(string.IsNullOrWhiteSpace(e.Summary) ? e.Request : e.Summary!);
                TryAdd(keywords, e.Slug);
            }

            return new BenchmarkDataset
            {
                Name             = name ?? $"{source.Name} (generated)",
                Version          = "v2-generated",
                Description      = "Auto-generated retrieval cases derived from knowledge entries (deterministic).",
                KnowledgeEntries = source.KnowledgeEntries,
                Cases            = cases
            };
        }

        // ── LLM-assisted (optional) ──────────────────────────────────────────────

        /// <summary>
        /// Asks a chat model for natural-language paraphrase queries per knowledge
        /// entry, each tagged with that entry's slug. Best-effort: failures or
        /// unparseable replies for one entry are skipped, not fatal. The produced
        /// dataset should be saved and committed for deterministic re-use.
        /// </summary>
        public static async Task<BenchmarkDataset> GenerateWithLlmAsync(
            BenchmarkDataset source,
            Func<IChatSession> sessionFactory,
            int paraphrasesPerEntry = 3,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var cases = new List<BenchmarkCase>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;

            for (int i = 0; i < source.KnowledgeEntries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var e = source.KnowledgeEntries[i];
                progress?.Report($"[{i + 1}/{source.KnowledgeEntries.Count}] paraphrasing '{e.Slug}'...");

                var prompt =
                    $"You write evaluation queries for a retrieval system.\n" +
                    $"Given the knowledge entry below, write {paraphrasesPerEntry} distinct, natural user " +
                    $"questions whose answer is THIS entry. Vary phrasing and specificity.\n" +
                    $"Return ONLY a JSON array of strings, nothing else.\n\n" +
                    $"--- ENTRY ---\n{Truncate(e.Request, 600)}\n" +
                    (string.IsNullOrWhiteSpace(e.Summary) ? "" : $"Summary: {Truncate(e.Summary!, 300)}\n");

                string content;
                try
                {
                    var session = sessionFactory();
                    var reply = await session.SendAsync(prompt, cancellationToken: ct);
                    content = reply.Content ?? "";
                }
                catch (Exception ex)
                {
                    progress?.Report($"  WARNING: model call failed for '{e.Slug}': {ex.Message}");
                    continue;
                }

                foreach (var q in ParseQueries(content))
                {
                    var query = CollapseWhitespace(q);
                    if (string.IsNullOrWhiteSpace(query)) continue;
                    if (!seen.Add(Normalize(query))) continue;
                    cases.Add(new BenchmarkCase
                    {
                        Id              = $"llm-{++n:D3}",
                        Query           = query,
                        ExpectedSlugs   = new List<string> { e.Slug },
                        ExpectRetrieval = true
                    });
                }
            }

            return new BenchmarkDataset
            {
                Name             = $"{source.Name} (llm-generated)",
                Version          = "v2-llm",
                Description      = "Auto-generated retrieval cases (LLM paraphrases of knowledge entries).",
                KnowledgeEntries = source.KnowledgeEntries,
                Cases            = cases
            };
        }

        // ── Persistence ──────────────────────────────────────────────────────────

        public static void SaveDataset(BenchmarkDataset dataset, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(dataset, Formatting.Indented));
        }

        // ── Text helpers ─────────────────────────────────────────────────────────

        private static IEnumerable<string> ParseQueries(string content)
        {
            content = content.Trim();

            // Preferred: a JSON array of strings (tolerate code fences / leading prose).
            int start = content.IndexOf('[');
            int end = content.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                var json = content.Substring(start, end - start + 1);
                List<string>? parsed = null;
                try { parsed = JsonConvert.DeserializeObject<List<string>>(json); }
                catch { /* fall through to line splitting */ }
                if (parsed != null)
                {
                    foreach (var s in parsed) yield return s;
                    yield break;
                }
            }

            // Fallback: one query per line, stripping list/bullet/quote noise.
            foreach (var raw in content.Split('\n'))
            {
                var line = raw.Trim().Trim('-', '*', '"', '\'', ' ');
                line = Regex.Replace(line, @"^\d+[\.\)]\s*", ""); // ""1. "" / ""1) ""
                if (!string.IsNullOrWhiteSpace(line)) yield return line;
            }
        }

        private static string FirstSentence(string text)
        {
            text = CollapseWhitespace(text);
            if (string.IsNullOrEmpty(text)) return "";
            int idx = text.IndexOfAny(new[] { '.', '?', '!' });
            return idx > 0 ? text[..(idx + 1)] : text;
        }

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a","an","the","and","or","but","if","of","to","in","on","for","with","as","by",
            "is","are","was","were","be","been","being","at","it","its","this","that","these",
            "those","from","into","how","do","does","did","what","when","which","you","your",
            "can","could","should","would","will","i","we","they","he","she","my","our"
        };

        private static string KeywordQuery(string text)
        {
            var words = Regex.Split(CollapseWhitespace(text).ToLowerInvariant(), @"[^a-z0-9]+")
                .Where(w => w.Length > 2 && !StopWords.Contains(w))
                .Distinct()
                .Take(8);
            return string.Join(' ', words);
        }

        private static string CollapseWhitespace(string s) =>
            string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, @"\s+", " ").Trim();

        private static string Normalize(string s) =>
            Regex.Replace(CollapseWhitespace(s).ToLowerInvariant(), @"[^a-z0-9 ]", "");

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
    }
}
