using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.OnnxRuntime;
using System.Text;
using FastBertTokenizer;
using static LocalRAG.MemoryHashIndex;
using System.Diagnostics;

namespace LocalRAG
{
    public class EmbedderClassNew : IDisposable
    {
        private readonly BertTokenizer _tokenizer;
        private readonly InferenceSession _session;
        private readonly MemoryHashIndex _memoryIndex;
        private readonly RAGConfiguration _config;
        private readonly int _hiddenDim;
        private readonly string? _cacheDir;      // null = cache off
        private readonly string _cacheSalt = "";
        private bool _disposed;

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with",
            "by", "is", "it", "as", "that", "this", "these", "those", "are", "was", "were", "be"
        };

        public EmbedderClassNew(RAGConfiguration? config = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _tokenizer = new BertTokenizer();

            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                InterOpNumThreads = _config.InterOpNumThreads,
                IntraOpNumThreads = _config.IntraOpNumThreads
            };

            // Optional GPU via DirectML (any DirectX 12 GPU, including AMD). Needs the
            // DirectML build of ONNX Runtime (build with -p:UseDirectML=true) and a model that
            // runs on it (an fp32/fp16 export, not the int8 CPU model).
            if (_config.GpuDeviceId is int gpu)
            {
                sessionOptions.EnableMemoryPattern = false;                 // DirectML requirements
                sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                sessionOptions.AppendExecutionProvider_DML(gpu);
            }

            _session = new InferenceSession(_config.ModelPath, sessionOptions);

            // Determine hidden dimension from model metadata
            _hiddenDim = GetHiddenDimensionFromModel();

            using (var vocabReader = new StreamReader(_config.VocabularyPath))
            {
                _tokenizer.LoadVocabulary(vocabReader, convertInputToLowercase: _config.LowercaseInput);
            }

            var hashOptions = new MemoryHashOptions
            {
                SimilarityThreshold = 0.5,
                MaxItems = 50000,
                HashSize = 32,
                NumHashFunctions = _config.NumberOfHashFunctions,
                NumBuckets = 256
            };
            _memoryIndex = new MemoryHashIndex(hashOptions);

            // Optional shared embedding cache (see RAGConfiguration.EmbeddingCacheDirectory). The salt makes
            // vectors from another model or other vector-changing settings land under different keys.
            if (!string.IsNullOrWhiteSpace(_config.EmbeddingCacheDirectory) && !_config.UseMemoryChannel)
            {
                _cacheDir = _config.EmbeddingCacheDirectory;
                Directory.CreateDirectory(_cacheDir);
                var model = new FileInfo(_config.ModelPath);
                _cacheSalt = $"v1|{model.Name}|{model.Length}|{_config.MaxSequenceLength}|{_config.RemoveStopWords}|{_config.LowercaseInput}";
            }
        }

        private int GetHiddenDimensionFromModel()
        {
            // Try to get hidden dimension from output metadata
            // BERT large: 1024, BERT base: 768
            if (_session.OutputMetadata.TryGetValue("last_hidden_state", out var metadata))
            {
                var dims = metadata.Dimensions;
                if (dims != null && dims.Length >= 3)
                {
                    // Shape is [batch, seq_len, hidden_dim]
                    var hiddenDim = dims[2];
                    if (hiddenDim > 0)
                        return hiddenDim;
                }
            }

            // Fallback: assume BERT base (768) which is most common
            // BERT base: 768, BERT large: 1024
            // MaxSequenceLength (512) is NOT the hidden dimension!
            return 768;
        }

        /// <summary>
        /// Generates embeddings for the given text using BERT.
        /// Returns the [CLS] token embedding (first token) which represents the entire sequence.
        /// </summary>
        public async Task<float[]> GetEmbeddingsAsync(string textToEmbed)
        {
            if (string.IsNullOrWhiteSpace(textToEmbed))
                throw new ArgumentException("Text to embed cannot be null or empty.", nameof(textToEmbed));

            return await GetEmbeddingsInternalAsync(textToEmbed);
        }

        /// <summary>
        /// Attempts to get embeddings, returning null on failure instead of throwing.
        /// </summary>
        public async Task<float[]?> TryGetEmbeddingsAsync(string textToEmbed)
        {
            if (string.IsNullOrWhiteSpace(textToEmbed))
                return null;

            try
            {
                return await GetEmbeddingsInternalAsync(textToEmbed);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Embedding generation failed: {ex.Message}");
                return null;
            }
        }

        private async Task<float[]> GetEmbeddingsInternalAsync(string textToEmbed)
        {
            if (_cacheDir == null) return await ComputeEmbeddingAsync(textToEmbed);

            var key = CacheKey(textToEmbed);
            if (key == null) return await ComputeEmbeddingAsync(textToEmbed);   // empty text: fails exactly as before
            var path = Path.Combine(_cacheDir, key[..2], key + ".bin");
            var hit = ReadCachedVector(path);
            if (hit != null) return hit;
            var vector = await ComputeEmbeddingAsync(textToEmbed);
            WriteCachedVector(path, vector);
            return vector;
        }

        private string? CacheKey(string textToEmbed)
        {
            var t = _config.RemoveStopWords ? RemoveStopWords(textToEmbed) : textToEmbed;
            t = NormalizeWhitespace(t);
            if (string.IsNullOrWhiteSpace(t)) return null;
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(_cacheSalt + "\n" + t))).ToLowerInvariant();
        }

        private static float[]? ReadCachedVector(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0 || bytes.Length % 4 != 0) return null;
                var v = new float[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, v, 0, bytes.Length);
                return v;
            }
            catch { return null; }   // an unreadable entry is just a miss
        }

        private static void WriteCachedVector(string path, float[] vector)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";   // write whole, then move: readers never see half a file
                var bytes = new byte[vector.Length * 4];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, path, overwrite: true);
            }
            catch { /* a failed write only loses the speed-up */ }
        }

        private async Task<float[]> ComputeEmbeddingAsync(string textToEmbed)
        {
            var text = textToEmbed;

            // Optionally remove stop words before embedding.
            // Off by default — BERT is trained on complete sentences and stop words
            // provide grammatical structure. Enable for experimentation.
            if (_config.RemoveStopWords)
                text = RemoveStopWords(text);

            text = NormalizeWhitespace(text);

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text is empty after preprocessing.", nameof(textToEmbed));

            // Encode to the text's real length, then pad only to PaddedLength (not always 512).
            var (ids0, mask0, types0) = _tokenizer.Encode(text, _config.MaxSequenceLength);
            int len = PaddedLength(ids0.Length);
            var inputIds = PadTo(ids0.Span, len);
            var attentionMask = PadTo(mask0.Span, len);
            var tokenTypeIds = PadTo(types0.Span, len);

            var inputData = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids",
                    new DenseTensor<long>(inputIds, new[] { 1, len })),
                NamedOnnxValue.CreateFromTensor("attention_mask",
                    new DenseTensor<long>(attentionMask, new[] { 1, len }))
            };

            if (_session.InputMetadata.ContainsKey("token_type_ids"))
            {
                inputData.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
                    new DenseTensor<long>(tokenTypeIds, new[] { 1, len })));
            }

            using var results = await Task.Run(() => _session.Run(inputData));

            var output = results.FirstOrDefault(r => r.Name == "last_hidden_state")
                      ?? results.FirstOrDefault();

            if (output == null)
                throw new InvalidOperationException("Model returned no output.");

            var tensor = output.AsTensor<float>();
            var embeddings = ExtractMeanEmbedding(tensor, attentionMask);
            var normalizedEmbeddings = Normalize(embeddings);

            // Store in memory index for similarity search (only when that channel is enabled;
            // otherwise every text and query would pile up in an index nothing reads).
            if (_config.UseMemoryChannel)
                await _memoryIndex.AddOrUpdateAsync(
                    id: Guid.NewGuid().ToString(),
                    vector: normalizedEmbeddings,
                    tags: new Dictionary<string, string> { { "text", textToEmbed } }
                );

            return normalizedEmbeddings;
        }

        /// <summary>
        /// Pools BERT's last_hidden_state into a single sentence embedding using
        /// attention-mask-weighted mean pooling.
        /// Output shape: [batch_size, sequence_length, hidden_dim].
        /// MiniLM (and most modern sentence encoders) are trained for mean pooling,
        /// not CLS-token extraction — averaging the unmasked token states yields the
        /// representation the model was actually optimized to produce. Padding tokens
        /// (attention_mask == 0) are excluded from the average.
        /// </summary>
        /// <summary>
        /// Length a text is padded to for inference. Attention-masked padding changes nothing
        /// in the pooled vector but costs compute, and most texts are far shorter than 512
        /// tokens, so by default pad only to the next multiple of 16 (few distinct shapes keeps
        /// a GPU fast). <see cref="RAGConfiguration.PadToMaxSequenceLength"/> restores 512.
        /// </summary>
        private int PaddedLength(int tokens) =>
            _config.PadToMaxSequenceLength
                ? _config.MaxSequenceLength
                : Math.Min(_config.MaxSequenceLength, Math.Max(16, (tokens + 15) / 16 * 16));

        private static long[] PadTo(ReadOnlySpan<long> src, int len)
        {
            var padded = new long[len];
            src[..Math.Min(src.Length, len)].CopyTo(padded);
            return padded;
        }

        private float[] ExtractMeanEmbedding(Tensor<float> tensor, Memory<long> attentionMask)
        {
            var dims = tensor.Dimensions.ToArray();
            var mask = attentionMask.Span;

            if (dims.Length == 3)
            {
                // Shape: [batch=1, seq_len, hidden_dim]
                int seqLen = dims[1];
                int hiddenDim = dims[2];
                var pooled = new float[hiddenDim];

                // Sum token states weighted by the attention mask, then divide by
                // the number of unmasked tokens.
                long validTokens = 0;
                for (int t = 0; t < seqLen; t++)
                {
                    // attentionMask may be shorter/longer than seqLen in edge cases;
                    // treat out-of-range positions as masked.
                    if (t >= mask.Length || mask[t] == 0)
                        continue;

                    validTokens++;
                    for (int i = 0; i < hiddenDim; i++)
                        pooled[i] += tensor[0, t, i];
                }

                if (validTokens == 0)
                {
                    // No unmasked tokens — fall back to the first token rather than
                    // returning a zero vector.
                    for (int i = 0; i < hiddenDim; i++)
                        pooled[i] = tensor[0, 0, i];
                    return pooled;
                }

                for (int i = 0; i < hiddenDim; i++)
                    pooled[i] /= validTokens;

                return pooled;
            }
            else if (dims.Length == 2)
            {
                // Shape: [seq_len, hidden_dim] - single batch, take first row
                int hiddenDim = dims[1];
                var clsEmbedding = new float[hiddenDim];

                for (int i = 0; i < hiddenDim; i++)
                {
                    clsEmbedding[i] = tensor[0, i];
                }

                return clsEmbedding;
            }
            else if (dims.Length == 1)
            {
                // Already a 1D embedding (unlikely for BERT but handle it)
                return tensor.ToArray();
            }
            else
            {
                // Fallback: flatten and take first hidden_dim elements
                Debug.WriteLine($"Unexpected tensor shape: [{string.Join(", ", dims)}]. Using fallback extraction.");
                return tensor.ToArray().Take(_hiddenDim).ToArray();
            }
        }

        /// <summary>
        /// Splits text into overlapping chunks and generates embeddings for each.
        /// All chunks for a document are embedded in a single batched ONNX Run
        /// (input shape [N, seq_len]) rather than N sequential calls — this saturates
        /// the CPU and removes per-call overhead. Per-row results are identical to
        /// embedding each chunk individually (BERT rows are independent), so this is a
        /// pure throughput optimization.
        /// </summary>
        public async Task<List<float[]>> SplitStringIntoEmbeddingsListAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be null or empty.", nameof(text));

            var chunks = _config.ChunkingStrategy == ChunkingStrategy.FixedCount
                ? FixedCountWindow(text, _config.ChunkCount, _config.OverlapPercentage)
                : SlidingWindow(text, _config.WordsPerString, _config.OverlapPercentage);
            return await GetEmbeddingsBatchInternalAsync(chunks);
        }

        /// <summary>
        /// Embeds a batch of texts in one ONNX inference call. Mirrors the preprocessing,
        /// mean pooling, normalization and memory-index updates of the single-text path.
        /// </summary>
        private async Task<List<float[]>> GetEmbeddingsBatchInternalAsync(IReadOnlyList<string> rawTexts)
        {
            // Preprocess and drop chunks that are empty after normalization, keeping the
            // raw chunk text (for the memory-index tag) aligned with the processed text.
            var raw = new List<string>(rawTexts.Count);
            var processed = new List<string>(rawTexts.Count);
            foreach (var t in rawTexts)
            {
                var p = _config.RemoveStopWords ? RemoveStopWords(t) : t;
                p = NormalizeWhitespace(p);
                if (string.IsNullOrWhiteSpace(p)) continue;
                raw.Add(t);
                processed.Add(p);
            }

            int n = processed.Count;
            if (n == 0) return new List<float[]>();

            bool hasTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");

            // Encode each text to its real length; the batch is padded to its longest member.
            var encoded = processed
                .Select(p => _tokenizer.Encode(p, _config.MaxSequenceLength))
                .ToList();
            int seq = PaddedLength(encoded.Max(e => e.InputIds.Length));

            var ids   = new long[n * seq];
            var mask  = new long[n * seq];
            var types = hasTypeIds ? new long[n * seq] : null;

            for (int r = 0; r < n; r++)
            {
                var (inputIds, attentionMask, tokenTypeIds) = encoded[r];
                inputIds.Span.CopyTo(ids.AsSpan(r * seq, inputIds.Length));
                attentionMask.Span.CopyTo(mask.AsSpan(r * seq, attentionMask.Length));
                if (hasTypeIds) tokenTypeIds.Span.CopyTo(types!.AsSpan(r * seq, tokenTypeIds.Length));
            }

            var inputData = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids",
                    new DenseTensor<long>(ids, new[] { n, seq })),
                NamedOnnxValue.CreateFromTensor("attention_mask",
                    new DenseTensor<long>(mask, new[] { n, seq }))
            };
            if (hasTypeIds)
                inputData.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
                    new DenseTensor<long>(types!, new[] { n, seq })));

            using var results = await Task.Run(() => _session.Run(inputData));

            var output = results.FirstOrDefault(r => r.Name == "last_hidden_state")
                      ?? results.FirstOrDefault();
            if (output == null)
                throw new InvalidOperationException("Model returned no output.");

            var tensor = output.AsTensor<float>(); // [n, seq, hidden]
            int hidden = tensor.Dimensions[2];

            var list = new List<float[]>(n);
            for (int r = 0; r < n; r++)
            {
                var pooled = new float[hidden];

                // Attention-mask-weighted mean pool over this row's tokens.
                long valid = 0;
                for (int t = 0; t < seq; t++)
                {
                    if (mask[r * seq + t] == 0) continue;
                    valid++;
                    for (int i = 0; i < hidden; i++)
                        pooled[i] += tensor[r, t, i];
                }

                if (valid == 0)
                    for (int i = 0; i < hidden; i++) pooled[i] = tensor[r, 0, i];
                else
                    for (int i = 0; i < hidden; i++) pooled[i] /= valid;

                var normalized = Normalize(pooled);
                list.Add(normalized);

                if (_config.UseMemoryChannel)
                    await _memoryIndex.AddOrUpdateAsync(
                        id: Guid.NewGuid().ToString(),
                        vector: normalized,
                        tags: new Dictionary<string, string> { { "text", raw[r] } });
            }

            return list;
        }

        /// <summary>
        /// Searches the memory index for similar embeddings.
        /// </summary>
        public async Task<List<SearchResult>> SearchSimilarAsync(string text, int topK = 10)
        {
            var embeddings = await TryGetEmbeddingsAsync(text);
            if (embeddings == null)
                return new List<SearchResult>();

            return await _memoryIndex.SearchAsync(embeddings, topK);
        }

        /// <summary>
        /// Helper to generate and set a single embedding with timeout protection.
        /// </summary>
        public async Task HandleSingleEmbedding(string? content, bool embedFlag, Action<float[]?> setter)
        {
            if (string.IsNullOrEmpty(content) || !embedFlag)
                return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                var embedding = await GetEmbeddingsAsync(content);
                setter(embedding);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Single embedding generation timed out.");
                setter(null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Single embedding generation failed: {ex.Message}");
                setter(null);
            }
        }

        /// <summary>
        /// Helper to generate and set a list of embeddings with timeout protection.
        /// </summary>
        public async Task HandleListEmbedding(string? content, bool embedFlag, Action<List<float[]>?> setter)
        {
            if (string.IsNullOrEmpty(content) || !embedFlag)
                return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                var embeddings = await SplitStringIntoEmbeddingsListAsync(content);
                setter(embeddings);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("List embedding generation timed out.");
                setter(null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"List embedding generation failed: {ex.Message}");
                setter(null);
            }
        }

        #region Text Processing

        /// <summary>
        /// Normalizes whitespace without removing stop words or punctuation.
        /// BERT handles these naturally and they provide important context.
        /// </summary>
        private static string NormalizeWhitespace(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Replace multiple whitespace characters with single space
            var normalized = System.Text.RegularExpressions.Regex.Replace(input.Trim(), @"\s+", " ");
            return normalized;
        }

        /// <summary>
        /// Splits text into exactly <paramref name="chunkCount"/> overlapping chunks, each
        /// sized so they tile the whole text with <paramref name="overlapPercentage"/>
        /// overlap. <paramref name="chunkCount"/> &lt;= 1 (or text shorter than the count)
        /// returns the whole text as a single chunk.
        /// </summary>
        private List<string> FixedCountWindow(string text, int chunkCount, double overlapPercentage)
        {
            if (overlapPercentage < 0 || overlapPercentage >= 100)
                throw new ArgumentOutOfRangeException(nameof(overlapPercentage), "Overlap percentage must be between 0 and 100.");

            var words = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (chunkCount <= 1 || words.Length <= chunkCount)
                return new List<string> { text };

            double o = overlapPercentage / 100.0;
            // chunkLen so that: chunkCount*L - (chunkCount-1)*o*L = totalWords
            double denom = chunkCount - (chunkCount - 1) * o;
            int chunkLen = Math.Max(1, (int)Math.Ceiling(words.Length / denom));
            int stepSize = Math.Max(1, (int)Math.Round(chunkLen * (1 - o)));

            var result = new List<string>();
            for (int c = 0; c < chunkCount; c++)
            {
                int start = c * stepSize;
                if (start >= words.Length) break;
                int end = Math.Min(start + chunkLen, words.Length);
                result.Add(string.Join(" ", words.Skip(start).Take(end - start)));
                if (end >= words.Length) break;
            }

            return result;
        }

        private List<string> SlidingWindow(string text, int wordsPerString, double overlapPercentage)
        {
            if (wordsPerString <= 0)
                throw new ArgumentException("Words per string must be greater than zero.", nameof(wordsPerString));

            if (overlapPercentage < 0 || overlapPercentage >= 100)
                throw new ArgumentOutOfRangeException(nameof(overlapPercentage), "Overlap percentage must be between 0 and 100.");

            var words = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length <= wordsPerString)
                return new List<string> { text };

            var result = new List<string>();
            int overlapWords = (int)Math.Floor(wordsPerString * (overlapPercentage / 100));
            int stepSize = Math.Max(1, wordsPerString - overlapWords);

            for (int i = 0; i < words.Length; i += stepSize)
            {
                int endIndex = Math.Min(i + wordsPerString, words.Length);
                result.Add(string.Join(" ", words.Skip(i).Take(endIndex - i)));

                if (endIndex >= words.Length)
                    break;
            }

            return result;
        }

        private string RemoveStopWords(string inputText)
        {
            if (string.IsNullOrWhiteSpace(inputText))
                return string.Empty;

            var words = inputText.Split(
                new[] { ' ', '.', ',', ';', ':', '!', '?', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            var filteredWords = words.Where(word => !StopWords.Contains(word));

            return string.Join(" ", filteredWords);
        }

        private static string TrimWhitespace(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            int startIndex = 0;
            int endIndex = input.Length - 1;

            // Skip leading whitespace
            while (startIndex <= endIndex && char.IsWhiteSpace(input[startIndex]))
                startIndex++;

            // Skip trailing whitespace (including \r and \n)
            while (endIndex >= startIndex && (char.IsWhiteSpace(input[endIndex])))
                endIndex--;

            if (startIndex > endIndex)
                return string.Empty;

            return input.Substring(startIndex, endIndex - startIndex + 1);
        }

        private static string PreprocessText(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var normalized = input.Normalize(NormalizationForm.FormC);
            var sb = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                if (!char.IsPunctuation(c))
                    sb.Append(c);
            }

            return sb.ToString();
        }

        #endregion

        #region Vector Operations

        private static float[] Normalize(float[] vector)
        {
            if (vector == null || vector.Length == 0)
                return Array.Empty<float>();

            double magnitude = 0;
            for (int i = 0; i < vector.Length; i++)
                magnitude += vector[i] * vector[i];

            magnitude = Math.Sqrt(magnitude);

            if (magnitude == 0)
                return new float[vector.Length]; // Return zero vector

            var result = new float[vector.Length];
            for (int i = 0; i < vector.Length; i++)
                result[i] = (float)(vector[i] / magnitude);

            return result;
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _session?.Dispose();
                _memoryIndex?.Dispose();
            }

            _disposed = true;
        }

        #endregion
    }
}