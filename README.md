# DCS-EE-RAG

A retrieval-augmented generation system combining two complementary architectures:

- **EE-RAG** (Elective Ephemeral RAG) — A five-layer pipeline where the model decides what to retrieve, and all retrieved content remains transient — never entering conversation history.
- **DCS** (Dynamic Context Selection) — A routing layer that structures context using hierarchical identifiers (intent, domain, continuity nodes) rather than embedding similarity alone.

Together they attempt solve the core problem of long-context reasoning: selecting relevant information from extended interactions without flooding the context, contaminating history, or introducing noise.

---

## Architecture

<img width="1526" height="1024" alt="image" src="https://github.com/user-attachments/assets/bb9f2da7-5f8e-44b5-a2a0-fbf5f4046093" />



## Intergrated Benchmarking and Auto Tuning 
<img width="1366" height="727" alt="image" src="https://github.com/user-attachments/assets/d7fad94d-2f1c-4bbc-9a1b-eedf80101bd0" />



### EE-RAG: The Five Layers

```
Layer 1  Summary-first retrieval      Top-k candidates presented as ~25-token summaries
Layer 2  Model-elected expansion      Model issues RETRIEVE <id> for what it actually needs
Layer 3  Transient injection          Full chunks injected ephemerally; never stored in history
Layer 4  Conversation isolation       RAG content stays out of persistent message history
Layer 5  Silent digestion (optional)  Chunks distilled to synthesis before injection
```

**Key idea:** The model sees summaries first and elects what to retrieve. If nothing is relevant, the pipeline ends after one inference call. Retrieval artifacts never pollute future turns.

Each elected chunk is tagged with an 8-hex embedding hash (`[RAG:a3f9b2c1]`) for deterministic audit without semantic pollution.

**More candidates, once.** If none of the first `topK` summaries look relevant the model may reply with the bare word `MORE`. The system then repeats the same search one set deeper and shows the next `topK` entries. The model cannot choose the number or change any setting (`MORE 50` is not a request), it can ask only once, and the next turn is back to normal.

**Election log.** Every search and retrieve stores an `ElectionTrace` under `MetaData["RagElectionLog"]` on the request's row,: every proposed entry in full (capped at 8,000 characters, flagged if cut) beside the summary that was shown, the caller's raw reply, the ids chosen or refused, what was injected, and what DCS did (query thread, boosted ids). It is written so a human or an AI reviewer can judge the choice from the whole retrieval. DCS only reorders the search's own candidates; it never adds an entry or any text of its own.

**Hybrid search.** Each query runs two channels and fuses them by rank (reciprocal rank fusion, so cosine and BM25 scales never have to agree): exact cosine search over every stored vector, and BM25 over a stemmed (porter) full-text index. `minimumSimilarity` is a cosine floor: every result's `Similarity` is its cosine against the query, and a keyword-only hit must clear the same floor. Fuzzy mode keeps its token-level scorer for typo tolerance. A third hash-bucket "memory" channel exists but is off by default (`UseMemoryChannel`): on 5,000 documents it returned about one result per query. Older databases get their full-text index rebuilt with stemming the first time they are opened.

On BEIR SciFact (300 queries, MiniLM-L12 int8, chunks off; the 0.646 baseline is checked in under `BenchmarkResults/`, the hybrid run is not) this took nDCG@10 from 0.646 to 0.723, Recall@5 from 0.714 to 0.808, Recall@10 from 0.798 to 0.869 and Recall@100 from 0.932 to 0.953. The keyword channel on its own went from 0.505 to 0.681 nDCG@10 (published BM25 is about 0.665).

### DCS: Structured Context Routing

Instead of embedding similarity alone, DCS routes context through a three-level identifier hierarchy:

| Level | Identifiers | Weight | Role |
|-------|-------------|--------|------|
| **Intent** | QUERY, INSTRUCTION, IMPLEMENTATION, CLARIFICATION, CORRECTION, CHAT, DESIGN, FIX | 0.5 | Primary gating — what is this message trying to do? |
| **DCN** | Dynamic Context Node IDs | 0.3 | Continuity — what line of work does this belong to? |
| **Domain** | BACKEND, FRONTEND, INFRASTRUCTURE, RESEARCH | 0.2 | Refinement — what topic area? |

**Dynamic Context Nodes (DCNs)** are continuity anchors. Unlike static topic labels, they evolve as work progresses — absorbing related messages, merging when similar enough, and decaying in influence when stale. A single DCN can span a problem definition through its refinement, implementation, and sub-problems.

**Similarity** between two messages uses weighted Jaccard across all three levels:
```
similarity = intent_jaccard × 0.5 + dcn_jaccard × 0.3 + domain_jaccard × 0.2
```

**Influence scoring** captures causal relationships between sequential messages:
- Shared DCN → `+0.6`
- Domain overlap → `+0.2`
- Intent escalation (e.g. CHAT → DESIGN) → `+0.3`

---

## Benchmark Results

Retrieval accuracy is measured against [BEIR-SciFact](https://github.com/beir-cellar/beir) (scientific-claim verification). Each query's relevant documents are known, so we report standard IR metrics at several cutoffs `k`. There is also a small internal generated benchmark (26 knowledge entries); its queries are copies or near-copies of the indexed text, so it checks that the plumbing works, not how well retrieval generalises, and a perfect score on it says little.

These results come from a compact **~33M-parameter** embedding model running locally on CPU — not a billion-parameter hosted retriever.

**How far to trust the figures below.** The tuning loop (keep a change only if it helps) was run on the same 300 SciFact test queries the numbers are reported on, and there is no held-out set, so treat them as development-set numbers. Only the MiniLM SciFact run (nDCG@10 0.646, Recall@100 0.932) is checked in under [`BenchmarkResults/`](BenchmarkResults/); the runs below are not, so none of their numbers can be traced to raw output from this repository. The two tables use different models and settings and are not directly comparable.

**Small run — `scifact-chunks-lsh` (BEIR-SciFact test, 2026-06-15, 30 queries; corpus size not recorded):**

| Metric | @1 | @5 | @10 | @100 |
|--------|----|----|-----|------|
| **Recall** | 0.773 | 0.967 | 0.967 | **1.000** |
| **Precision** | 0.833 | 0.233 | 0.117 | 0.012 |
| **nDCG** | 0.833 | 0.898 | 0.898 | 0.905 |

**MRR 0.876** · **Candidate recall 1.000** · 30 queries · ~21 s

This run retrieves the correct supporting document within the top 5 for **96.7%** of queries (29 of 30) and within the top 100 for all of them. With only 30 queries and an unrecorded corpus size, that is a smoke result, not a benchmark: the 300-query run below is the one to quote. A BEIR run has no negative cases (queries with no relevant document are skipped), so there is no false-positive rate to report. "Candidate recall" here is simply Recall at the search depth (`TopK`) of the final ranked list, which is why it equals Recall@100 in both tables. Chunk-level indexing with LSH (`htables=10`, `hfuncs=8`, 40-word chunks, 25% overlap) drove the largest gains over the unchunked baseline.

**Full-scale run — `scifact-gte-int8-item2` (300 queries):**

| Metric | @1 | @5 | @10 | @100 |
|--------|----|----|-----|------|
| **Recall** | 0.583 | 0.746 | 0.806 | 0.930 |
| **nDCG** | 0.610 | 0.684 | 0.705 | 0.733 |

**MRR 0.682** · **Candidate recall 0.930** · 300 queries. At full corpus scale the system surfaces the correct document in the top 100 for **93%** of queries.

`BenchmarkResults/` holds the raw per-run JSON and a consolidated `retrieval.csv` for the runs that were saved there (the MiniLM SciFact run and runs of the internal generated set), not for every run quoted in this README. See [`BENCHMARKING_EXPLAINED.md`](BENCHMARKING_EXPLAINED.md) for metric definitions and how to reproduce.

---

## Project Structure

```
DCS-EE-RAG/
├── LocalRAG/                       Core library (.NET 10)
│   ├── EERagPipeline.cs            Five-layer pipeline orchestrator
│   ├── DcsContextAssembler.cs      DCS engine — DCN lifecycle, message registration, context assembly
│   ├── DcsIntentClassifier.cs      Pattern-based intent and domain classification
│   ├── DcsKey.cs                   Structured identifier with weighted Jaccard similarity
│   ├── DcnNode.cs                  Dynamic Context Node — continuity anchor with recency decay
│   ├── DcsMessageRecord.cs         Message wrapper with DCS metadata
│   ├── DcsLogEntry.cs              Structured selection decision log record
│   ├── EmbeddingDatabaseNew.cs     SQLite knowledge base with BERT embeddings and LSH indexing
│   ├── EmbedderClass.cs            ONNX BERT embedding generation
│   ├── MemoryHashIndex.cs          In-memory LSH vector index
│   ├── FeedbackDatabaseValues.cs   Database row model
│   └── RAGConfiguration.cs         Centralized configuration
├── LocalRAG.Tests/                 xUnit test project (.NET 10)
│   ├── DcsTests.cs                 Unit tests for all DCS components
│   ├── EERagBenchmarkTests.cs      Pipeline and benchmark tests
│   ├── IntegrationTests.cs         End-to-end tests requiring BERT model
│   ├── CosineSimilarityTests.cs
│   ├── LSHTests.cs
│   ├── MemoryHashIndexTests.cs
│   └── WordMatchScoreTests.cs
├── About_DynamicContextSelection.md    DCS architectural overview
├── Implement_DynamicContextSelection.md 10 production improvements
├── EE-RAG-Rev4.md                  EE-RAG architecture paper
└── LocalRAG.sln
```

---

## Getting Started

### Requirements

- .NET 10 SDK
- An ONNX sentence-embedding model with a BERT-WordPiece tokenizer and mean pooling. The default is **GTE-small** (int8 / AVX512-VNNI, ~33M params, 384-dim) — a drop-in upgrade over MiniLM that is markedly more accurate at roughly the same ingestion speed. Any MiniLM variant works too.
- A matching vocabulary file (`vocab.txt`)

### Default Paths

The system looks for model files relative to the application base directory:

```
gte-small/model_qint8_avx512_vnni.onnx
gte-small/vocab.txt
Database/Memory/FeedbackEmbeddings512.db   (created on first run)
```

Override any path via `RAGConfiguration`:

```csharp
var config = new RAGConfiguration
{
    ModelPath      = "/path/to/model.onnx",
    VocabularyPath = "/path/to/vocab.txt",
    DatabasePath   = "/path/to/embeddings.db"
};
```

### Embeddings on a GPU machine (optional)

Embeddings can come from an OpenAI-compatible endpoint instead of the local ONNX model, for example LM Studio on a machine with a GPU. Load an embedding model there (a GGUF build of the same GTE-small gives vectors close to the local ones; a larger model such as `gte-base` or `bge-base-en-v1.5` also works) and point the system at it:

```csharp
var config = new RAGConfiguration
{
    RemoteEmbeddingUrl   = "http://gpu-box:1234/v1",
    RemoteEmbeddingModel = "text-embedding-gte-large"   // the id LM Studio shows; null or "auto" picks the first embedding model it lists
};
```

Or set `DCS_EMBEDDING_URL`, `DCS_EMBEDDING_MODEL` and `DCS_EMBEDDING_API_KEY` (only if the endpoint needs one).

- The endpoint is tried once when the embedder is created (`RemoteEmbeddingProbeTimeoutMs`, 3 s). If it answers, it makes every vector and the local model is not loaded. If it does not, the local ONNX model is used. The choice is not revisited mid-run, so one run never mixes two models.
- `RemoteEmbeddingClient.ListEmbeddingModelsAsync(url, apiKey)` lists the embedding models the server has loaded. LM Studio with authentication on needs the token in `DCS_EMBEDDING_API_KEY`.
- To compare every loaded embedding model on a small BEIR SciFact set (nDCG@10, Recall@10, MRR, ms per embed): set `BEIR_SCIFACT_DIR`, `REMOTE_EMBEDDING_URL` (and `DCS_EMBEDDING_API_KEY`), optionally `REMOTE_MODELS` (semicolon-separated ids), then `dotnet test LocalRAG.Tests -c Release --filter Category=ModelSweep`.
- **Prompts.** Many embedding models are trained with a fixed prefix for queries and another for documents and score noticeably lower without them (embeddinggemma: `task: search result | query: ` and `title: none | text: `; bge: `Represent this sentence for searching relevant passages: ` for queries; e5: `query: ` / `passage: `; nomic: `search_query: ` / `search_document: `). Set `EmbeddingQueryPrefix` and `EmbeddingDocumentPrefix` (or the env vars). They apply to the local model too. A different prefix makes different vectors, so the database model check asks for a re-embed.
- Set `RemoteEmbeddingRequired` (or `DCS_EMBEDDING_REQUIRED=1`) for benchmarks: an endpoint that does not answer then throws instead of quietly using the local model, so a run cannot report the wrong model's numbers.
- The vector size is read from the first reply, so any dimension works.
- A database remembers which model made its vectors. Moving between the endpoint and the local model (or between two endpoint models) sets `EmbeddingModelMismatch` until `BackfillEmbeddingsAsync("all")` re-embeds everything.
- Models that expect a query/document prefix (`nomic-embed-text`, `e5`) are not given one.

### Build

```bash
dotnet build LocalRAG.sln
```

### Run Tests

```bash
# Unit tests only (no BERT model required)
dotnet test LocalRAG.Tests/ --filter "Category!=Integration"

# All tests including integration (requires BERT model at default path or via env vars)
export BERT_MODEL_PATH=/path/to/model.onnx
export BERT_VOCAB_PATH=/path/to/vocab.txt
dotnet test LocalRAG.Tests/
```

---

## Usage

No AI provider is built in. The calling AI (any model, any language) is the "model" in EE-RAG: it sends one JSON request and gets one JSON reply back. `EERagPipeline.HandleAsync(json, db, dcs?)` is the entry point; a failure comes back as `{"error":"..."}`.

```csharp
var db  = new EmbeddingDatabaseNew(new RAGConfiguration());
var dcs = new DcsContextAssembler();   // optional; pass null for EE-RAG without DCS

// 1. Store an entry (a row the audit can be written to; embed:true makes it searchable)
await EERagPipeline.HandleAsync("""
  {"op":"store","requestId":"turn-42","request":"How do I fix the authentication bug?","embed":false}
  """, db);

// 2. Search: summaries only (Layer 1)
var search = await EERagPipeline.HandleAsync("""
  {"op":"search","requestId":"turn-42","query":"How do I fix the authentication bug?","topK":5}
  """, db, dcs);
// {"requestId":"turn-42",
//  "candidates":[{"id":12,"requestId":"...","similarity":0.87,"summary":"Auth flow uses JWT...","dcsBoosted":true,"batch":1}, ...],
//  "header":"--- Potentially Relevant Context --- ...",   <- text to put in front of the AI
//  "moreAvailable":true, "dcsQueryDcnId":3}

// 3. The AI elects ids from the summaries (Layer 2), by id or by its raw reply
var got = await EERagPipeline.HandleAsync("""
  {"op":"retrieve","requestId":"turn-42","ids":[12]}
  """, db);                                                // or "reply":"RETRIEVE 12"
// {"electedIds":[12],"rejectedIds":[],"hashes":["a3f9b2c1"],
//  "chunks":[{"id":12,"hash":"a3f9b2c1","request":"...","response":"...","toolResponse":null}],
//  "context":"--- Retrieved Context ... ---",             <- use for this turn only (Layer 3)
//  "tags":" [RAG:a3f9b2c1]"}
```

| Request | Fields |
|---------|--------|
| `store` | `requestId`, `request`, optional `response`, `embed` (default `true`) |
| `search` | `requestId` (must be stored), `query`, `topK` (default 5), `more` (the next `topK` entries, once per request) |
| `retrieve` | `requestId`, and `ids` or `reply` (a raw `RETRIEVE 3 7` reply); optional `silent` (Layer 5: condensed context) |

Only ids that the `search` for that `requestId` surfaced are served; anything else comes back in `rejectedIds`. The audit and election log are written to the row's `MetaData` as before.

### DCS

```csharp
// Register prior messages to build context
dcs.RegisterMessage(new DcsMessageRecord
{
    MessageId     = "msg-001",
    Key           = DcsIntentClassifier.Classify("Let's design the auth system"),
    HardLinkedDcns = new[] { dcnNode.DcnId },
    Content       = "Let's design the auth system",
    TimestampUtc  = DateTime.UtcNow.AddMinutes(-10)
});

// Optionally manage DCNs
var dcnNode = dcs.FindOrCreateDcn(DcsIntentClassifier.Classify(userMessage));
```

Passing `dcs` to `HandleAsync` makes `search` re-rank its own candidates (`"dcsBoosted": true`); DCS never adds an entry or any text of its own.

### Inspecting DCS Decisions

```csharp
// After AssembleContext, examine the selection log
foreach (var entry in dcs.Log)
{
    Console.WriteLine(entry);
    // [DCS 14:23:01.442] DCN=3 Candidates=12 Filtered=7 Strategy=Selective TopScores=[msg1=0.847, ...]
}

// Trigger DCN merge pass (after accumulating many sessions)
dcs.MergeSimilarDcns();
```

---

## Configuration Reference

```csharp
public class RAGConfiguration
{
    // Paths
    string DatabasePath          // SQLite DB path (created if missing)
    string ModelPath             // ONNX BERT model (.onnx)
    string VocabularyPath        // BERT vocab file (vocab.txt)

    // Remote embeddings (optional; see "Embeddings on a GPU machine")
    string? RemoteEmbeddingUrl            // OpenAI-compatible base URL; null = local model (env DCS_EMBEDDING_URL)
    string? RemoteEmbeddingModel         // Model id sent to the endpoint; null/"auto" = first embedding model listed
    string? RemoteEmbeddingApiKey         // Bearer token, if needed
    int     RemoteEmbeddingProbeTimeoutMs // Start-up check before falling back (default: 3000)
    string? EmbeddingQueryPrefix          // Text put before each search query (env DCS_EMBEDDING_QUERY_PREFIX)
    string? EmbeddingDocumentPrefix       // Text put before each stored document (env DCS_EMBEDDING_DOCUMENT_PREFIX)
    bool    RemoteEmbeddingRequired       // True = never fall back to the local model; throw instead (env DCS_EMBEDDING_REQUIRED=1)
    int     RemoteEmbeddingTimeoutSeconds // Per-request timeout (default: 60)

    // Chunking
    int    MaxSequenceLength     // BERT max input length (default: 128)
    int    WordsPerString        // Words per chunk (default: 40)
    double OverlapPercentage     // Chunk overlap % (default: 25)

    // LSH Index
    int    NumberOfHashFunctions // Hash functions per table (default: 8)
    int    NumberOfHashTables    // Number of hash tables (default: 10)

    // Queue & Retry
    int    MaxQueueSize          // Async update queue depth (default: 1000)
    int    MaxRetryAttempts      // Retry attempts on failure (default: 3)
    int    RetryDelayMs          // Delay between retries ms (default: 1000)

    // ONNX Threading
    int    InterOpNumThreads     // Inter-op parallelism (default: 32)
    int    IntraOpNumThreads     // Intra-op parallelism (default: 2)

    // Cache
    int      MaxCacheItems            // Max cached embeddings (default: 10000)
    long     CacheItemSizeThreshold   // Max item size in bytes (default: 1MB)
    TimeSpan CacheExpiry              // Cache TTL (default: 15 minutes)
}
```

### Database safety

- **Schema.** The database carries `PRAGMA user_version`. Opening an older file adds the columns it lacks; a file from a newer version is refused.
- **Embedding model.** The database remembers which model and vector-changing settings made its embeddings. If you load a different model, `EmbeddingModelMismatch` is set and searching, embedding new text and `BackfillEmbeddingsAsync("missing")` throw until `BackfillEmbeddingsAsync("all")` has re-embedded every record. A cancelled or partly failed full re-embed leaves the flag set.
- **Failures are reported, not hidden.** A row stored without some of its embeddings (failure or the 30 s wait) is counted in `EmbeddingFailures` and raised through `EmbeddingFailed`. A corrupt stored embedding is skipped at start-up and counted in `WarmUpSkippedRecords`. The RAG audit writes return whether a row was updated, and `search` / `retrieve` results carry `auditRecorded`.
- **Shutdown.** `EmbeddingDatabaseNew` is `IAsyncDisposable`; disposing cancels queued updates (their callers get `OperationCanceledException`) and later updates throw `ObjectDisposedException`.
- **Retrieved text.** Candidate summaries are flattened to one line (300 characters at most) and full entries are quoted line by line, so stored text cannot forge `[ID:n]`, `---`, `Entry n:` or `NOTE:` lines. A `RETRIEVE` command must be the first line of the reply.

### DCS Thresholds (in `DcsContextAssembler`)

| Constant | Default | Meaning |
|----------|---------|---------|
| `MAX_PER_DCN` | 20 | Max messages from a single DCN in assembled context |
| `ELIGIBILITY_THRESHOLD` | 0.1 | Minimum similarity score to be included |
| `MERGE_THRESHOLD` | 0.75 | Minimum topic similarity to merge two DCNs |

### DCN Recency Decay

DCN influence decays exponentially: `weight = exp(-age_hours / 24)`

| Age | Weight |
|-----|--------|
| 0 hours (just used) | 1.000 |
| 24 hours | ~0.368 |
| 48 hours | ~0.135 |
| 72 hours | ~0.050 |

---

## Debug Logging

All DCS and pipeline steps emit structured `Debug.WriteLine` output (filtered from Release builds) using the `[DCS]` prefix. View in the Visual Studio Output window or any debug listener.

```
[DCS 14:23:01.334] Pipeline > search "How do I fix the auth bug?" requestId=turn-42 topK=5 more=False dcs=on
[DCS 14:23:01.336] Classify > Intents: [QUERY(1), FIX(8)] from "How do I fix the auth bug?"
[DCS 14:23:01.337] Classify > Domains: [BACKEND(1)] from "How do I fix the auth bug?"
[DCS 14:23:01.338] Assemble > Step 1: Index lookup found 8 candidates from 3 identifiers (45 total messages)
[DCS 14:23:01.340] Assemble >   msg-003: similarity=0.847 x decay=0.990 -> score=0.838
[DCS 14:23:01.341] Assemble >   msg-007: similarity=0.210 < threshold 0.100, skipped
[DCS 14:23:01.890] Pipeline > retrieve requestId=turn-42 elected=[12] rejected=[] outcome=elected
```

**Section labels for filtering:**

| Label | Where |
|-------|-------|
| `Pipeline` | EE-RAG pipeline steps |
| `Classify` | Intent and domain classification |
| `Assemble` | DCS context assembly |
| `DCN` | DCN find/create/touch operations |
| `Merge` | DCN merge pass |
| `Influence` | Behavioral influence scoring |
| `Register` | Message registration |
| `FunctionHash` | Function match surfacing |

---

## Key Design Properties

**Invisible infrastructure** — Retrieved content never appears in the model's conversation history. Future turns see clean dialogue.

**Elective retrieval** — The calling AI decides what to retrieve based on summaries, not the retrieval system. If nothing is relevant it simply never sends a `retrieve` request.

**Traceable routing** — Context selection via structured identifiers is debuggable: threshold decisions can be traced in logs. It is not strictly reproducible: DCN recency decay uses the wall clock, and the DCS state (the nodes and message history) is whatever the caller feeds the assembler in that process; nothing is persisted, so a restart starts empty.

**Temporal awareness** — DCN recency decay ensures stale topics lose influence automatically without manual cleanup.

**Security constraint** — The pipeline only allows the model to elect IDs from the candidates it was shown, preventing prompt-injection-driven retrieval of arbitrary records.

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.ML.OnnxRuntime` | 1.23.2 | BERT inference |
| `Microsoft.Data.Sqlite` | 10.0.1 | Embedding storage |
| `FastBertTokenizer` | 1.0.28 | BERT tokenization |
| `Newtonsoft.Json` | 13.0.4 | JSON serialization |
| `Microsoft.Extensions.Caching.Memory` | 10.0.1 | Embedding cache |

---

## Further Reading

- [`About_DynamicContextSelection.md`](About_DynamicContextSelection.md) — Full DCS architectural overview
- [`Implement_DynamicContextSelection.md`](Implement_DynamicContextSelection.md) — The 10 production improvements with implementation details
- [`EE-RAG-Rev4.md`](EE-RAG-Rev4.md) — EE-RAG architecture paper
