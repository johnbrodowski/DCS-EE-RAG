# DCS-EE-RAG

A retrieval-augmented generation system combining two complementary architectures:

- **EE-RAG** (Elective Ephemeral RAG) — A five-layer pipeline where the model decides what to retrieve, and all retrieved content remains transient — never entering conversation history.
- **DCS** (Dynamic Context Selection) — A deterministic routing layer that structures context using hierarchical identifiers (intent, domain, continuity nodes) rather than embedding similarity alone.

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

**Election log.** Every run stores an `ElectionTrace` under `MetaData["RagElectionLog"]` on the request's row, and returns it as `EERagResult.Election`: every proposed entry in full (capped at 8,000 characters, flagged if cut) beside the summary the model saw, the model's raw replies, the ids chosen or refused, what was injected, and what DCS did (query thread, boosted ids). It is written so a human or an AI reviewer can judge the choice from the whole retrieval. DCS only reorders the search's own candidates; it never adds an entry or any text of its own.

**Hybrid search.** Each query runs two channels and fuses them by rank (reciprocal rank fusion, so cosine and BM25 scales never have to agree): exact cosine search over every stored vector, and BM25 over a stemmed (porter) full-text index. `minimumSimilarity` is a cosine floor: every result's `Similarity` is its cosine against the query, and a keyword-only hit must clear the same floor. Fuzzy mode keeps its token-level scorer for typo tolerance. A third hash-bucket "memory" channel exists but is off by default (`UseMemoryChannel`): on 5,000 documents it returned about one result per query. Older databases get their full-text index rebuilt with stemming the first time they are opened.

On BEIR SciFact (300 queries, MiniLM-L12 int8, chunks off) this took nDCG@10 from 0.646 to 0.723, Recall@5 from 0.714 to 0.808, Recall@10 from 0.798 to 0.869 and Recall@100 from 0.932 to 0.953. The keyword channel on its own went from 0.505 to 0.681 nDCG@10 (published BM25 is about 0.665).

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

Retrieval accuracy is measured against [BEIR-SciFact](https://github.com/beir-cellar/beir) (scientific-claim verification) and an internal generated benchmark. Each query's relevant documents are known, so we report standard IR metrics at several cutoffs `k`.

These results come from a compact **~33M-parameter** embedding model running locally on CPU — not a billion-parameter hosted retriever. Reaching near-perfect top-100 recall and ~97% top-5 recall at that size is what makes the approach practical to self-host.

**Latest & best run — `scifact-chunks-lsh` (BEIR-SciFact test, 2026-06-15):**

| Metric | @1 | @5 | @10 | @100 |
|--------|----|----|-----|------|
| **Recall** | 0.773 | 0.967 | 0.967 | **1.000** |
| **Precision** | 0.833 | 0.233 | 0.117 | 0.012 |
| **nDCG** | 0.833 | 0.898 | 0.898 | 0.905 |

**MRR 0.876** · **Candidate recall 1.000** · **Negative false-positive rate 0.000** · 30 queries · ~21 s

This run retrieves the correct supporting document within the top 5 for **96.7%** of queries, and within the top 100 for **100%** — with zero false positives on negative cases. Chunk-level indexing with LSH (`htables=10`, `hfuncs=8`, 40-word chunks, 25% overlap) drove the largest gains over the unchunked baseline.

**Full-scale run — `scifact-gte-int8-item2` (300 queries):**

| Metric | @1 | @5 | @10 | @100 |
|--------|----|----|-----|------|
| **Recall** | 0.583 | 0.746 | 0.806 | 0.930 |
| **nDCG** | 0.610 | 0.684 | 0.705 | 0.733 |

**MRR 0.682** · **Candidate recall 0.930** · 300 queries. At full corpus scale the system still surfaces the correct document in the top 100 for **93%** of queries.

Raw per-run JSON and a consolidated `retrieval.csv` summary live in [`BenchmarkResults/`](BenchmarkResults/). See [`BENCHMARKING_EXPLAINED.md`](BENCHMARKING_EXPLAINED.md) for metric definitions and how to reproduce.

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

### EE-RAG Without DCS

```csharp
var config  = new RAGConfiguration();
var db      = new EmbeddingDatabaseNew(config);
var session = /* create an IChatSession */;

var result = await EERagPipeline.RunPipelineAsync(
    userMessage : "How do I fix the authentication bug?",
    requestId   : Guid.NewGuid().ToString(),
    session     : session,
    db          : db,
    topK        : 5,
    silentMode  : false
);

Console.WriteLine(result.FinalResponse);
```

### EE-RAG With DCS Pre-Filtering

```csharp
var dcs = new DcsContextAssembler();

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

// Run pipeline with DCS
var result = await EERagPipeline.RunPipelineAsync(
    userMessage  : userMessage,
    requestId    : requestId,
    session      : session,
    db           : db,
    topK         : 5,
    dcsAssembler : dcs      // Enables DCS pre-filtering and candidate boosting
);
```

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
[DCS 14:23:01.334] Pipeline > === EE-RAG Pipeline Start ===
[DCS 14:23:01.335] Pipeline > User message: "How do I fix the auth bug?" (42 chars)
[DCS 14:23:01.336] Classify > Intents: [QUERY(1), FIX(8)] from "How do I fix the auth bug?"
[DCS 14:23:01.337] Classify > Domains: [BACKEND(1)] from "How do I fix the auth bug?"
[DCS 14:23:01.338] Assemble > Step 1: Index lookup found 8 candidates from 3 identifiers (45 total messages)
[DCS 14:23:01.340] Assemble >   msg-003: similarity=0.847 x decay=0.990 -> score=0.838
[DCS 14:23:01.341] Assemble >   msg-007: similarity=0.210 < threshold 0.100, skipped
[DCS 14:23:01.342] Assemble > Result: 6 messages selected (max 50)
[DCS 14:23:01.350] Pipeline > Step 1: Found 5 candidates: [ID:12 sim=0.873, ID:7 sim=0.720, ...]
[DCS 14:23:01.351] Pipeline > ---- CANDIDATE HEADER SENT TO MODEL ----
[DCS 14:23:01.351] Pipeline > [ID:12] Auth flow uses JWT tokens with 1-hour expiry...
[DCS 14:23:01.352] Pipeline > ---- END CANDIDATE HEADER ----
[DCS 14:23:01.890] Pipeline > ---- MODEL FIRST RESPONSE ----
[DCS 14:23:01.890] Pipeline > RETRIEVE 12
[DCS 14:23:01.890] Pipeline > ---- END MODEL FIRST RESPONSE ----
[DCS 14:23:02.100] Pipeline > ---- MODEL FINAL RESPONSE ----
[DCS 14:23:02.100] Pipeline > To fix the authentication bug, update the token refresh logic...
[DCS 14:23:02.101] Pipeline > ---- END MODEL FINAL RESPONSE ----
[DCS 14:23:02.102] Pipeline > === EE-RAG Pipeline Complete ===
[DCS 14:23:02.102] Pipeline > Candidates surfaced: 5, Elected: 1, Retrieval: yes
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

**Elective retrieval** — The model decides what to retrieve based on summaries, not the retrieval system. If nothing is relevant, no second inference call is made.

**Deterministic routing** — Context selection via structured identifiers is reproducible and debuggable. Threshold decisions can be traced in logs.

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
