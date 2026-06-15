using FastBertTokenizer;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LocalRAG
{
    /// <summary>
    /// Cross-encoder reranker. Scores (query, passage) pairs with full cross-attention via
    /// an ONNX sequence-classification model (e.g. ms-marco-MiniLM-L-6-v2), so it can
    /// re-rank a small candidate set far more precisely than a bi-encoder. All candidates
    /// for a query are scored in a single batched inference.
    /// </summary>
    public sealed class RerankerClass : IDisposable
    {
        private const long Cls = 101, Sep = 102; // BERT uncased special-token ids
        private readonly InferenceSession _session;
        private readonly BertTokenizer _tokenizer;
        private readonly int _maxLen;
        private readonly bool _hasTypeIds;

        public RerankerClass(string modelPath, string vocabPath, int maxLen = 256, bool lowercase = true)
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = 0
            };
            _session = new InferenceSession(modelPath, options);

            _tokenizer = new BertTokenizer();
            using (var reader = new StreamReader(vocabPath))
                _tokenizer.LoadVocabulary(reader, convertInputToLowercase: lowercase);

            _maxLen = maxLen;
            _hasTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");
        }

        /// <summary>Relevance score per passage (higher = more relevant). Order matches input.</summary>
        public IReadOnlyList<double> Score(string query, IReadOnlyList<string> passages)
        {
            int n = passages.Count;
            if (n == 0) return Array.Empty<double>();

            var qTok = TokenIds(query);

            var ids   = new long[n * _maxLen];
            var mask  = new long[n * _maxLen];
            var types = new long[n * _maxLen];

            for (int r = 0; r < n; r++)
            {
                var pTok = TokenIds(passages[r]);
                int pos = r * _maxLen, i = 0;

                void Put(long id, long type)
                {
                    if (i >= _maxLen) return;
                    ids[pos + i] = id; mask[pos + i] = 1; types[pos + i] = type; i++;
                }

                // [CLS] query [SEP] passage [SEP]
                Put(Cls, 0);
                foreach (var t in qTok) Put(t, 0);
                Put(Sep, 0);
                int budget = _maxLen - i - 1; // reserve trailing [SEP]
                for (int k = 0; k < pTok.Count && k < budget; k++) Put(pTok[k], 1);
                Put(Sep, 1);
                // remaining positions stay 0 (pad / mask 0)
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, new[] { n, _maxLen })),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, new[] { n, _maxLen }))
            };
            if (_hasTypeIds)
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(types, new[] { n, _maxLen })));

            using var results = _session.Run(inputs);
            var tensor = results.First().AsTensor<float>();
            var dims = tensor.Dimensions.ToArray();

            var scores = new double[n];
            for (int r = 0; r < n; r++)
                scores[r] = dims.Length == 2 ? tensor[r, 0] : tensor[r];
            return scores;
        }

        // Subword token ids of a single text, without [CLS]/[SEP]/padding.
        private List<long> TokenIds(string text)
        {
            var (encoded, _, _) = _tokenizer.Encode(text ?? string.Empty, _maxLen, _maxLen);
            var span = encoded.Span;
            var list = new List<long>();
            for (int i = 1; i < span.Length; i++) // skip leading [CLS]
            {
                var id = span[i];
                if (id == Sep || id == 0) break; // stop at [SEP] / [PAD]
                list.Add(id);
            }
            return list;
        }

        public void Dispose() => _session.Dispose();
    }
}
