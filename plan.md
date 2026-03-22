# Embedding Tuning Settings — Implementation Plan

## Goal
Add adjustable embedding parameters to the benchmark UI so settings can be fine-tuned during development. New controls go in a new **"Embedding Settings"** group between the Backfill and Run Benchmark sections.

---

## New Settings (UI Controls)

### Requested
1. **Remove Stop Words** — `CheckBox` (default: unchecked)
   Toggles whether stop words are stripped before BERT embedding. The method already exists in `EmbedderClass.cs` (`RemoveStopWords`) but is currently unused.

2. **Sliding Window Overlap %** — `NumericUpDown` (0–75, default: 25)
   Maps to `RAGConfiguration.OverlapPercentage`. Already exists in config but not exposed in UI.

3. **Overwrite Existing Embeddings** — `CheckBox` (default: unchecked)
   When checked, backfill re-embeds all items (not just unembedded ones). Useful after changing embedding parameters.

### Suggested Additional Options
4. **Words Per Chunk** — `NumericUpDown` (10–200, default: 40)
   Maps to `RAGConfiguration.WordsPerString`. Controls chunk granularity.

5. **Max Sequence Length** — `ComboBox` with values 128, 256, 512 (default: 256)
   Maps to `RAGConfiguration.MaxSequenceLength`. BERT token limit per input — shorter is faster, longer captures more context.

6. **Lowercase Input** — `CheckBox` (default: checked)
   Controls whether input text is lowercased before tokenization. BERT uncased models expect lowercase; cased models don't.

---

## Files to Modify

### 1. `LocalRAG/RAGConfiguration.cs`
- Add `bool RemoveStopWords` property (default: `false`)
- Add `bool LowercaseInput` property (default: `true`)

### 2. `LocalRAG/EmbedderClass.cs`
- Read `_config.RemoveStopWords` in `GetEmbeddingsInternalAsync` — when true, call existing `RemoveStopWords()` before `NormalizeWhitespace()`
- Read `_config.LowercaseInput` and pass to `_tokenizer.LoadVocabulary()` in constructor

### 3. `DemoApp/BenchmarkSettings.cs`
- Add properties: `RemoveStopWords`, `OverlapPercentage`, `WordsPerChunk`, `MaxSequenceLength`, `LowercaseInput`, `OverwriteEmbeddings`
- With matching defaults

### 4. `DemoApp/FormQaBenchmark.Designer.cs`
- Add new `GroupBox` "Embedding Settings" with 6 controls:
  - `chkRemoveStopWords` (CheckBox)
  - `numOverlapPct` (NumericUpDown, 0–75)
  - `numWordsPerChunk` (NumericUpDown, 10–200)
  - `cmbMaxSeqLen` (ComboBox: 128, 256, 512)
  - `chkLowercase` (CheckBox)
  - `chkOverwriteEmbeddings` (CheckBox)

### 5. `DemoApp/FormQaBenchmark.cs`
- **LoadSettings / SaveCurrentSettings** — wire new controls to `BenchmarkSettings`
- **btnStartBackfill_Click** — build `RAGConfiguration` from UI values instead of using bare defaults; pass `overwrite` flag to backfiller
- Add helper `BuildRagConfigFromUI()` that creates a `RAGConfiguration` with the UI-selected embedding parameters

### 6. `LocalRAG/QaDataset/QaEmbeddingBackfiller.cs`
- Add `overwriteExisting` parameter to `Start()`
- When true: call a new `ResetEmbeddingsAsync()` on the database before running, or fetch ALL items instead of just unembedded ones
- Report correct total counts

### 7. `LocalRAG/QaDataset/QaDatasetDatabase.cs`
- Add `ResetAllEmbeddingsAsync()` — sets `IsEmbedded = 0` and clears `QuestionEmbedding` for all rows
- Add `GetAllItemsForEmbeddingAsync(int batchSize)` — like `GetUnembeddedItemsAsync` but returns all items

---

## Implementation Order
1. `RAGConfiguration.cs` — add new properties
2. `EmbedderClass.cs` — use new config flags
3. `BenchmarkSettings.cs` — add persistence fields
4. `QaDatasetDatabase.cs` — add reset/get-all methods
5. `QaEmbeddingBackfiller.cs` — add overwrite support
6. `FormQaBenchmark.Designer.cs` — add UI controls
7. `FormQaBenchmark.cs` — wire everything together
