using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LocalRAG
{
    /// <summary>How the lexical channel applies fuzzy (typo-tolerant) matching.</summary>
    public enum FuzzyLexicalMode
    {
        /// <summary>Never use fuzzy matching; exact whole-word lexical scoring only.</summary>
        Off,
        /// <summary>Escalate to fuzzy only when the normal search returns too few results.</summary>
        Fallback,
        /// <summary>Use fuzzy matching on every query.</summary>
        Always
    }

    /// <summary>How a document is split into chunks for the per-chunk embedding list.</summary>
    public enum ChunkingStrategy
    {
        /// <summary>Fixed-size sliding window (<c>WordsPerString</c> words, <c>OverlapPercentage</c> overlap); chunk count varies with length.</summary>
        WindowSize,
        /// <summary>Split into exactly <c>ChunkCount</c> overlapping chunks (sized to fit), with <c>OverlapPercentage</c> overlap.</summary>
        FixedCount
    }

    /// <summary>
    /// Configuration settings for the LocalRAG system.
    /// </summary>
    public class RAGConfiguration
    {
        private static string GetAppPath(string relativePath)
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(basePath, relativePath);
        }

        /// <summary>
        /// Path to the SQLite database file for storing embeddings. Will be created if it doesn't exist.
        /// </summary>
        public string DatabasePath { get; set; } = GetAppPath(Path.Combine("Database", "Memory", "FeedbackEmbeddings512.db"));

        /// <summary>
        /// Path to the ONNX BERT model file. Download a BERT model in ONNX format and update this path.
        /// Example: https://huggingface.co/models?library=onnx&search=bert
        /// </summary>
        // GTE-small (int8 / AVX512-VNNI). Drop-in for MiniLM: BERT-WordPiece tokenizer,
        // mean pooling, 384-dim — but markedly more accurate. On BEIR SciFact (full
        // pipeline, chunks-off) it lifted nDCG@10 0.544 -> 0.705 over MiniLM at ~the same
        // ingestion speed (int8 VNNI). MIT-licensed. The int8 variant trades ~0.01 nDCG
        // vs fp32 for ~1.7x faster inference on VNNI-capable CPUs.
        public string ModelPath { get; set; } = GetAppPath(Path.Combine("gte-small", "model_qint8_avx512_vnni.onnx"));
        public string VocabularyPath { get; set; } = GetAppPath(Path.Combine("gte-small", "vocab.txt"));

        /// <summary>
        /// Path to the BERT vocabulary file (vocab.txt). Should match the BERT model being used.
        /// </summary>
        //public string VocabularyPath { get; set; } = GetAppPath(Path.Combine("Vocabularies", "base_uncased_large.txt"));
        // GTE-small supports 512 tokens (~380 words). At 512 a full chat message or
        // abstract is embedded without truncation; anything longer is covered by chunking
        // (chunk-aware exact search), so no document content is lost at any length.
        public int MaxSequenceLength { get; set; } = 512;
        public int WordsPerString { get; set; } = 40;
        public double OverlapPercentage { get; set; } = 25;

        /// <summary>
        /// How documents are chunked for the per-chunk embedding list (when
        /// <see cref="IndexChunkEmbeddings"/> is on). <see cref="ChunkingStrategy.WindowSize"/>
        /// (default) uses fixed <see cref="WordsPerString"/>-word windows;
        /// <see cref="ChunkingStrategy.FixedCount"/> splits into exactly
        /// <see cref="ChunkCount"/> overlapping chunks. Both honor <see cref="OverlapPercentage"/>.
        /// </summary>
        public ChunkingStrategy ChunkingStrategy { get; set; } = ChunkingStrategy.WindowSize;

        /// <summary>
        /// Number of overlapping chunks to split each document into when
        /// <see cref="ChunkingStrategy.FixedCount"/> is used. 1 = whole document (no split).
        /// </summary>
        public int ChunkCount { get; set; } = 2;
        public int NumberOfHashFunctions { get; set; } = 8;
        public int NumberOfHashTables { get; set; } = 10;
        public int MaxQueueSize { get; set; } = 1000;
        public int MaxRetryAttempts { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 1000;
        // ONNX Runtime threading. BERT is a sequential graph, so inter-op parallelism
        // (running independent graph branches concurrently) buys almost nothing — 32 was
        // 4x oversubscribed on an 8-core machine and just added scheduling overhead.
        // Intra-op is where the matmuls parallelize; 0 lets ORT use all physical cores
        // instead of being capped at 2.
        public int InterOpNumThreads { get; set; } = 1;
        public int IntraOpNumThreads { get; set; } = 0;

        /// <summary>
        /// When true, stop words are removed from text before embedding.
        /// BERT is trained on complete sentences so this is off by default.
        /// </summary>
        public bool RemoveStopWords { get; set; } = false;

        /// <summary>
        /// When true (default), each document is also split into overlapping chunks and
        /// every chunk is embedded and indexed into LSH separately, giving sub-document
        /// recall at the cost of ~N extra forward passes per document (the dominant
        /// ingestion cost). Set false to index only the single full-text embedding —
        /// roughly an order of magnitude faster to seed. Whether it costs retrieval
        /// quality is corpus-dependent and should be measured on the benchmark.
        /// </summary>
        public bool IndexChunkEmbeddings { get; set; } = true;

        /// <summary>
        /// When true (default), the vector retrieval channel does an exact brute-force
        /// cosine scan over every embedding instead of LSH approximate candidate
        /// generation. More accurate (no bucketing loss) and, at local single-user corpus
        /// sizes, fast enough that the approximation isn't needed. On BEIR SciFact this was
        /// +0.033 nDCG@10 / +0.034 Recall@10 over LSH. Set false to fall back to LSH for
        /// very large corpora where a full scan is too slow.
        /// </summary>
        public bool UseExactVectorSearch { get; set; } = true;

        /// <summary>
        /// When true, a cross-encoder reranker re-scores the top
        /// <see cref="RerankCandidateCount"/> retrieved candidates with full query-document
        /// attention and reorders them. Sharpens top-rank precision; query-time only, no
        /// re-embedding. Requires <see cref="RerankerModelPath"/>. Default off (opt-in).
        /// </summary>
        public bool UseReranker { get; set; } = false;

        public string RerankerModelPath { get; set; } = GetAppPath(Path.Combine("ms-marco-MiniLM-L6-ONNX", "model_quantized.onnx"));
        public string RerankerVocabPath { get; set; } = GetAppPath(Path.Combine("ms-marco-MiniLM-L6-ONNX", "vocab.txt"));

        /// <summary>How many of the top fused candidates the reranker re-scores.</summary>
        public int RerankCandidateCount { get; set; } = 20;

        /// <summary>
        /// Controls token-level fuzzy lexical matching (Jaro-Winkler scoring + prefix-
        /// broadened FTS pre-filter), which tolerates misspellings/word variants on
        /// distinctive terms. <see cref="FuzzyLexicalMode.Fallback"/> (default) only
        /// escalates to fuzzy when the normal search returns fewer than
        /// <see cref="FuzzyFallbackMinResults"/> results — cheap first, fuzzy only when
        /// needed. <see cref="FuzzyLexicalMode.Always"/> uses it on every query;
        /// <see cref="FuzzyLexicalMode.Off"/> disables it. The vector channel still does
        /// the semantic / missing-word recall. Callers can also force fuzzy per query.
        /// </summary>
        public FuzzyLexicalMode FuzzyLexicalMode { get; set; } = FuzzyLexicalMode.Fallback;

        /// <summary>
        /// In <see cref="FuzzyLexicalMode.Fallback"/>, the result count at or below which
        /// the search re-runs with fuzzy matching enabled. 1 = only when nothing was found.
        /// </summary>
        public int FuzzyFallbackMinResults { get; set; } = 1;

        /// <summary>
        /// Minimum Jaro-Winkler similarity (0..1) for a query word to count as a fuzzy
        /// match to a document word. Tokens of 3 chars or fewer always require an exact
        /// match regardless of this threshold.
        /// </summary>
        public double FuzzyMatchThreshold { get; set; } = 0.88;

        /// <summary>
        /// When true (and fuzzy matching is active), the lexical channel also pulls
        /// candidates from a trigram FTS index, broadening recall to documents that exact
        /// + prefix FTS misses (e.g. mid-word typos). Requires the trigram index; adds
        /// some query cost. Default off.
        /// </summary>
        public bool FuzzyCandidateRecall { get; set; } = false;

        /// <summary>
        /// Controls whether input text is lowercased before tokenization.
        /// Should match the BERT model variant (uncased=true, cased=false).
        /// </summary>
        public bool LowercaseInput { get; set; } = true;
 
        public int MaxCacheItems { get; set; } = 10000;
        public long CacheItemSizeThreshold { get; set; } = 1024 * 1024; // 1MB
        public TimeSpan CacheExpiry { get; set; } = TimeSpan.FromMinutes(15);
 
    }

}
