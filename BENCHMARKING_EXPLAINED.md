# DCS-EE-RAG — Benchmarking & Terminology, Explained

*A plain-English guide to what the numbers mean, why they matter, and where they come
from in this system. Written for someone who built the system but hasn't lived in the
information-retrieval (IR) vocabulary. Every term is defined the first time it appears,
and every metric is anchored to a real number this project has actually measured.*

---

## 0. The one-paragraph mental model

A RAG ("Retrieval-Augmented Generation") system does two separate jobs, and you must
judge them separately:

1. **Retrieval** — given a question, *find* the right documents out of a big pile.
2. **Generation** — given those documents, *write* a good answer.

Almost all of the benchmarking in this repo is about **job #1, retrieval**, because that
is the part you can measure cleanly and deterministically (no language model, no opinions,
no randomness). If retrieval hands the model the wrong documents, no amount of clever
generation can save the answer. So we measure retrieval in isolation, with math, against a
known-correct answer key. That is what all the terms below are about.

> **Why "measure retrieval alone"?** Generation quality needs a human or another LLM to
> judge "is this answer good?", which is slow, expensive, and subjective. Retrieval
> quality just asks "did the right document show up, and how high in the list?" — that's
> pure arithmetic you can run thousands of times in CI. This repo's benchmark is
> deliberately **LLM-free** for exactly this reason.

---

## 1. The raw ingredients (what a benchmark is made of)

To score retrieval you need three things. In IR they have standard names:

| Term | Plain English | In this repo |
|------|---------------|--------------|
| **Corpus** | The pile of documents you search through. | SciFact: **5,183** scientific abstracts. |
| **Queries** | The questions you ask. | SciFact: **300** test questions. |
| **Qrels** (query relevance judgments) | The answer key: for each query, which documents are *actually* relevant. Hand-labeled by humans. | Ships with SciFact; loaded by `BeirDatasetLoader`. |

A **benchmark** is just: run every query through your system, look at the ranked list of
documents it returns, and compare that list against the qrels answer key. The metrics
below are different ways of scoring "how close was the list to the answer key?"

> **Relevant vs returned.** A document the answer key says is correct = **relevant**. A
> document your system actually put in its results = **returned/retrieved**. Every metric
> is some way of comparing these two sets.

---

## 2. The core metrics (what each number actually means)

These are the numbers you'd ask a bragging stranger for, and the ones this repo tracks.
"**@k**" everywhere means "looking only at the top *k* results" — e.g. `@10` = "judge
only the first 10 documents the system returned." `k` matters because in production you
only feed the model the top few, so quality at small `k` is what counts.

### Recall@k — "did we find the relevant docs at all?"
**Definition:** of all the documents that *should* have been found, what fraction showed
up in your top *k*?

- Recall@10 = 0.685 means: across all queries, the top-10 results contained **68.5%** of
  the documents the answer key marked relevant. (Your real SciFact MiniLM baseline.)
- Recall@100 = 0.834 → the top-100 caught 83.4% of relevant docs.

Recall is a **coverage** metric. It does *not* care about order — only "is it in there
somewhere." High recall, low precision = "the answer is in the pile but buried."

### Precision@k — "how much of what we returned was junk?"
**Definition:** of the *k* documents you returned, what fraction were actually relevant?
The mirror image of recall. High precision = little junk in the list. (Computed by the
harness; less emphasized here because SciFact has few relevant docs per query, which caps
precision by construction.)

### nDCG@k — "did we put the good stuff at the top?" ← **the headline metric**
**Normalized Discounted Cumulative Gain.** This is the one you'll quote most, and the one
this project optimizes. Read it in three pieces:

- **Gain** — you score points for each relevant document in your list.
- **Discounted** — points are *worth less the further down the list* they appear. A
  relevant doc at rank 1 is worth full points; at rank 10 it's worth a fraction. This is
  the key idea: **rank order matters**, because users and models read from the top.
- **Normalized** — the raw score is divided by the best-possible score for that query, so
  the final number always lands between **0.0 (terrible) and 1.0 (perfect ordering)**.
  This makes queries comparable regardless of how many relevant docs they have.

So **nDCG@10 = 0.705** (your current GTE-int8 number) means "across all queries, the
ranking of the top 10 was about 70.5% of the way to a perfect ranking." It rewards both
*finding* relevant docs **and** *ranking them high* — which is why it's the single best
summary number for retrieval quality.

> **Why nDCG is the headline and not the others:** Recall ignores order; MRR (below) only
> looks at the *first* hit. nDCG looks at *all* the hits *and* their positions. It's the
> most complete single number — that's why "what's your nDCG@10?" is the right question to
> ask anyone bragging about their RAG.

### MRR — "how high was the *first* correct answer?"
**Mean Reciprocal Rank.** For each query, find the rank of the *first* relevant document
and score 1/rank (1st = 1.0, 2nd = 0.5, 3rd = 0.33...). Average over all queries.

- MRR is great when you only care about "the one right answer near the top" (lookup-style
  questions).
- **Watch out for saturation:** on your chat benchmark MRR sits at ~0.95 — the first
  relevant message is almost always easy to find, so MRR has no room left to improve. When
  a metric is "saturated" it stops being useful for telling improvements apart, which is
  exactly why the chat work switched to **nDCG@10** as the metric with headroom.

### Candidate Recall@100 — "did the right doc even make it into the room?"
This is a **diagnostic** specific to how this system is built, and it's the most important
one for debugging. Your pipeline has stages: a fast first stage gathers ~100 candidates,
then later stages re-rank them. **Candidate Recall@100 = 83.4%** means the *first stage*
only managed to pull 83.4% of the relevant docs into the candidate pool at all — so
**16.6% of relevant docs never even entered the pipeline**, and no amount of clever
re-ranking downstream can recover them. They're gone before the smart stages run.

> This metric is how you diagnose **where** quality is lost. If candidate recall is low,
> the problem is your *first-stage retrieval* (the plumbing). If candidate recall is high
> but nDCG is low, the problem is your *ranking* (the reranker's job). This distinction
> drove most of the tuning decisions in this repo.

---

## 3. The two ways to find documents (lexical vs semantic)

There are two fundamentally different ways to match a query to a document, and modern
systems ("hybrid") use both. Knowing the difference explains half the components in this
codebase.

### Lexical / keyword matching — "do the *words* match?"
Matches on the actual words. If the query says "authentication" the doc must contain
"authentication" (or a close spelling). Fast, transparent, great for exact terms, names,
and codes — but blind to meaning ("car" won't match "automobile").

- **BM25** is the famous, decades-old lexical scoring formula — the industry-standard
  **baseline** everyone compares against. (More on why it's the baseline in §5.)
- **FTS** (Full-Text Search) in this repo is SQLite's keyword index. The **trigram FTS**
  and **fuzzy token scorer** work you did are lexical: trigrams match on 3-character
  chunks so misspellings still hit ("authentcation" ≈ "authentication").

### Semantic / dense matching — "does the *meaning* match?"
Matches on meaning, not words. This is where embeddings come in.

- **Embedding** — a neural model (here, a small BERT-family model) reads a piece of text
  and turns it into a list of numbers (a **vector**, here **384 numbers long**) that
  captures its meaning. Texts with similar meaning get similar vectors, even if they share
  no words.
- **Cosine similarity** — the math for "how similar are two vectors?" Returns ~1.0 for
  "basically the same meaning," ~0.0 for "unrelated." This is how the system ranks docs by
  meaning.
- **Mean-pooling** — a BERT model outputs one vector per word; mean-pooling averages them
  into a single vector for the whole text. (Your "item 1" fix was switching to proper
  mean-pooling, worth a few points of nDCG. The alternative, **CLS pooling**, uses one
  special token's vector instead — different models want different pooling, which is why
  the granite model in your notes "isn't a drop-in.")

**Dense (semantic) is what your `EmbeddingDatabaseNew.SearchEmbeddingsAsync` does at its
core, and it's where most of your quality comes from.** Lexical is the safety net for exact
terms and typos.

---

## 4. The performance plumbing (and how it costs quality)

Searching 5,000 docs by exact cosine is easy; searching millions isn't. So real systems
approximate, and approximation trades a little quality for a lot of speed. Several repo
components are exactly this trade — and your benchmark exists to *measure* the cost.

- **LSH** (Locality-Sensitive Hashing) — an **approximate nearest-neighbor** index. Instead
  of comparing the query to every document (slow), it hashes similar vectors into the same
  buckets and only compares within buckets (fast). The catch: it sometimes *misses* a
  relevant doc that landed in a different bucket — that's **approximation loss**, and it
  shows up as lower Candidate Recall. Your "item 2" (removing LSH pool truncation) was a
  pure recall win because it stopped throwing away candidates the index had actually found.
- **Exact in-memory search** — the opposite: compare against everything, no misses, but
  doesn't scale. Your benchmark showed exact slightly beats LSH (e.g. chat nDCG 0.781 vs
  0.770) — that gap *is* the approximation cost, now quantified instead of guessed.
- **Chunking** — long documents are split into overlapping ~40-word **chunks**, each
  embedded separately, so a query can match the *relevant paragraph* instead of being
  diluted by the whole doc. Costs ingestion time (~10 embeddings/doc instead of 1). Your
  measured trade: chunks-OFF is **5.2× faster to build** (53 min → 10 min) for **−0.022
  nDCG@10** — so you use chunks-OFF for fast iteration and chunks-ON for the final number.

> **The "plumbing gap" insight from your own notes:** Published `all-MiniLM-L6-v2` scores
> ~0.645 nDCG@10 on SciFact using *exact cosine over whole documents*. Your baseline hit
> 0.552 — a ~9-point gap. That gap wasn't the *model* being bad; it was the *plumbing*
> (LSH approximation + chunk-level embeddings + how their scores were fused). Knowing the
> gap is plumbing, not model, is what told you which fixes would pay off. **This is the
> single most valuable thing a benchmark gives you: it localizes *where* quality is lost.**

---

## 5. Why you always compare against a baseline (BM25)

A retrieval number alone is meaningless — 0.70 nDCG could be amazing or terrible depending
on the dataset. You need an **anchor**: a simple, well-understood system run on the *same*
data. **BM25** (plain keyword search, §3) is the universal anchor because:

- It's simple, free, and decades-proven.
- If your fancy embedding system can't clearly beat BM25 on your data, your embeddings
  aren't earning their cost.

This is also why **"compared to what?" is the killer question** to ask anyone bragging.
Your repo lives this discipline: every change is measured *against the previous state* on
the *same* SciFact set, and **changes that don't help get reverted** — your "item 3" (RRF
fusion) dropped nDCG from 0.544 to 0.405 and was thrown out. That's the benchmark doing its
job: stopping a plausible-sounding idea that actually hurt.

> **RRF** (Reciprocal Rank Fusion) is a standard recipe for *combining* two ranked lists
> (e.g. your dense list + your keyword list) into one. It works when both lists are
> roughly equal quality. It failed here because the keyword channel was much noisier than
> the dense channel, so blending them equally **diluted** the good list with the bad one.
> The open lever your notes flag — *weighted* fusion that down-weights the weak channel —
> is the principled fix.

---

## 6. The reranker (a second, smarter opinion)

A **cross-encoder reranker** is a second-stage model that improves *ordering* after
first-stage retrieval. The distinction matters:

- **First-stage (bi-encoder)** — embeds the query and each doc *separately*, then compares
  vectors. Fast (docs are pre-embedded once), but the model never sees query and doc
  *together*, so it misses subtle relevance.
- **Reranker (cross-encoder)** — takes the top ~20 candidates and runs the query and each
  doc *through the model together*, with full attention between them. Much more accurate
  per pair, but far slower — so you only run it on the ~20 candidates the fast stage
  already shortlisted, never the whole corpus.

Your `RerankerClass` (ms-marco-MiniLM) measured **+0.023 nDCG@10** on the chat benchmark
(0.781 → 0.803), at a cost of ~0.8s/query. It's **opt-in** (`UseReranker`, default off)
precisely because of that latency-vs-quality trade — exactly the kind of knob your design
philosophy wants to be adjustable rather than hard-coded.

> Note MRR went *down* slightly (−0.008) when nDCG went up. That's not a bug — MRR was
> saturated (§2), so the reranker reshuffled lower ranks (helping nDCG) without changing
> the already-perfect first hit. A metric going the "wrong" way only matters if it's the
> metric with headroom.

---

## 7. The model-quality knobs (quantization & hardware)

The embedding model itself is the biggest single lever — your "item 4" swap from MiniLM to
**GTE-small** gained **+0.16 nDCG@10**, bigger than items 1–3 combined. Two terms from that
work:

- **Quantization (int8)** — the model's weights are normally 32-bit floats (fp32);
  quantizing stores them as 8-bit integers (int8). The model gets ~4× smaller and faster,
  for a tiny quality cost (~0.01 nDCG here). **fp16** (16-bit) is a middle option that was
  *useless on your CPU* because the CPU has no native fp16 — it actually ran slower.
- **VNNI / AVX-512** — special CPU instructions that do int8 math extremely fast. Your
  Ice Lake CPU has them, so **int8 is near-free speed** there. This is why "GTE-small
  int8-VNNI" was the pick: **~GTE quality at ~MiniLM speed** — you got the better model
  without paying the speed penalty that normally comes with it.

> Takeaway: "which model" and "which quantization" interact with "which CPU." A format
> that's faster on one machine (fp16, int8) can be slower on another. Your model-sweep
> harness (`ModelComparisonTests`) exists to measure this on *your* hardware rather than
> trusting published numbers.

---

## 8. How the benchmarks are actually wired (so you can run them)

All benchmarks live under `LocalRAG/Benchmarks/` and are exposed as **gated xUnit tests**
— "gated" meaning they only run when you explicitly ask, so normal `dotnet test` stays fast
and offline. You select them by **category**:

| Category | What it does | Needs |
|----------|--------------|-------|
| `RetrievalBenchmark` | Report-only run on the homegrown set; writes `BenchmarkResults/retrieval.csv` (one row per *k*) + per-case JSON. | BERT model env vars |
| `BeirBenchmark` | The real external yardstick: SciFact. | `BEIR_SCIFACT_DIR` + model |
| `RetrievalGate` | **The guardrail.** Fails the build if `MeanRecall@5` drops below `retrieval_baseline.json`. Self-skips if that file is absent. | model |
| `ModelSweep` | Compares several embedding models head-to-head (exact cosine, no plumbing). | `MODEL_PATHS` list |
| `ChatBenchmark` | Retrieval on the *real workload* — 80 chat messages, 6 threads. | model |

Two practical notes from your setup:

- The BERT model lives in the **LocalRAG bin, not the test bin**, so the tests read
  `BERT_MODEL_PATH` / `BERT_VOCAB_PATH` env vars pointing at
  `LocalRAG/bin/Debug/net10.0/all-MiniLM-L6-v2-ONNX/` (now the GTE int8 path).
- **Tag every run with `RETRIEVAL_LABEL`** so the rolling CSV stays comparable across
  experiments. This is how you build a ledger of "what each change did" instead of
  one-off numbers you can't reproduce.

> **The gate is the part most systems don't have.** It turns "we benchmarked once" into
> "we *can't merge a regression*." That's the difference between a system that *was* good
> once and a system that *stays* good. When someone brags about their RAG, "do you have a
> gate that fails CI on a quality regression?" separates the engineered from the demoed.

---

## 9. Your actual numbers, in one place

The honest, current state of this system (SciFact, 5,183 docs / 300 queries), so you can
quote real figures instead of adjectives:

| Stage | nDCG@10 | Recall@10 | Recall@100 | Candidate Recall@100 | MRR |
|-------|---------|-----------|------------|----------------------|-----|
| Baseline (MiniLM, item 1 only) | 0.552 | 0.685 | 0.834 | 83.4% | 0.524 |
| + item 2 (LSH no-truncation) | 0.544\* | — | 0.811\* | 81.1%\* | — |
| **+ item 4 (GTE-small int8) — shipped** | **0.705** | **0.806** | **0.930** | **93.0%** | **0.682** |
| Reference: MiniLM published (exact) | ~0.645 | — | — | — | — |
| Reference: GTE published (exact) | ~0.74 | — | — | — | — |

\* *Items 2/3 measured on the fast chunks-OFF path, so the absolute numbers differ from the
chunks-ON baseline row; the comparison is row-to-row within the same path. Item 3 (RRF) was
rejected and reverted.*

**Plain-English summary of where you landed:** the shipped system reaches **nDCG@10 = 0.705**
— past the MiniLM published ceiling (~0.645) and within ~3.5 points of GTE's exact-search
ceiling (~0.74), with **93% candidate recall** (up from 83.4%, so far fewer relevant docs
are lost before ranking). The biggest single win was the model swap; the benchmark is what
let you *prove* it was a win and reject the change (RRF) that wasn't.

---

## 10. The questions to ask a bragger (now that you know what they mean)

You can now read these and know exactly what each one tests:

1. **"nDCG@k and Recall@k on what dataset?"** — forces a real metric + a real, ideally
   public, eval set (§1–2).
2. **"At the *k* you actually use in production?"** — Recall@100 can look great while
   Recall@5 is weak (§2).
3. **"Versus a BM25 baseline — what's the delta?"** — the anchor question; un-anchored
   numbers are meaningless (§5).
4. **"Retrieval quality or answer quality?"** — people conflate them to hide retrieval
   misses (§0).
5. **"Mean over several runs, or one lucky run? What's the variance?"** — single runs lie.
6. **"Do you have a gate that fails CI on a regression?"** — separates *engineered* from
   *demoed* (§8).

If they answer with a metric, a dataset, and a baseline without flinching, they've measured
it. If they answer with adjectives, latency, or "users love it," they haven't.

---

*Cross-references: the per-component depth lives in `About_DynamicContextSelection.md`,
`Implement_DynamicContextSelection.md`, and `EE-RAG-Rev4.md`. The live numbers live in
`BenchmarkResults/retrieval.csv` and the memory notes under `memory/`.*
