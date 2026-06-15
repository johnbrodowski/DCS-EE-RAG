using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LocalRAG
{
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
        public int MaxSequenceLength { get; set; } = 256;
        public int WordsPerString { get; set; } = 40;
        public double OverlapPercentage { get; set; } = 25;
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
        /// When true, the vector retrieval channel does an exact brute-force cosine scan
        /// over every embedding instead of LSH approximate candidate generation. More
        /// accurate (no bucketing loss) and, at local single-user corpus sizes, fast
        /// enough that the approximation isn't needed.
        /// </summary>
        public bool UseExactVectorSearch { get; set; } = false;

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
