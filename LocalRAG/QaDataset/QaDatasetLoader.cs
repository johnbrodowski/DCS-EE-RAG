using Newtonsoft.Json;

namespace LocalRAG.QaDataset
{
    /// <summary>
    /// Parses JSONL dataset files (one JSON object per line) and loads them into a QaDatasetDatabase.
    /// Each line must contain the fields: question, answer, def_correct_predictions,
    /// poss_correct_predictions, def_incorrect_predictions, answer_and_def_correct_predictions.
    /// </summary>
    public static class QaDatasetLoader
    {
        /// <summary>Lazily parses a JSONL file into QaDatasetItem objects.</summary>
        public static IEnumerable<QaDatasetItem> ParseJsonl(string filePath)
        {
            int lineNum = 0;
            foreach (var line in File.ReadLines(filePath))
            {
                lineNum++;
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                QaDatasetItem? item;
                try
                {
                    item = JsonConvert.DeserializeObject<QaDatasetItem>(trimmed);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"Line {lineNum}: invalid JSON — {ex.Message}", ex);
                }

                if (item != null && !string.IsNullOrWhiteSpace(item.Question))
                    yield return item;
            }
        }

        /// <summary>
        /// Imports all items from a JSONL file into the database.
        /// Returns the number of items imported.
        /// </summary>
        public static async Task<int> ImportFileAsync(
            string filePath,
            QaDatasetDatabase db,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var source = Path.GetFileName(filePath);
            int count = 0, skipped = 0;

            // A bad line mid-file must fail before any row is written; otherwise a retry duplicates what went in.
            var items = ParseJsonl(filePath).ToList();

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                item.DatasetSource = source;
                if (await db.ImportItemAsync(item) == 0) skipped++;
                else count++;

                if ((count + skipped) % 500 == 0)
                    progress?.Report($"Imported {count:N0} items…");
            }

            progress?.Report($"Import complete — {count:N0} items loaded from {source}" +
                             (skipped > 0 ? $", {skipped:N0} duplicate(s) skipped." : "."));
            return count;
        }
    }
}
