using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using LLama;
using LLama.Common;
using LLama.Sampling;
using LLama.Transformers;
using AiMessagingCore.Abstractions;
using AiMessagingCore.Configuration;
using AiMessagingCore.Core;
using AiMessagingCore.Models;

namespace AiMessagingCore.Providers.Local;

/// <summary>
/// LlamaSharp in-process GGUF inference session with streaming and channel-format detection.
///
/// Model resolution order:
///   1. Absolute path — used as-is.
///   2. LLAMASHARP_MODEL_DIR env var + filename.
///   3. &lt;AppBase&gt;/models/ + filename.
///
/// The session owns the LLamaWeights/LLamaContext/ChatSession lifecycle and reuses them
/// across turns so conversation history is maintained in-process.
/// </summary>
public sealed class LlamaSharpChatSession : ChatSessionBase, IDisposable
{
    private readonly ILocalModelManager _localModelManager;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private LLamaWeights?      _weights;
    private LLamaContext?      _context;
    private LLama.ChatSession? _llamaSession;
    private string             _loadedModelPath = "";

    public LlamaSharpChatSession(ChatSessionOptions options, ILocalModelManager localModelManager)
        : base(options)
    {
        _localModelManager = localModelManager;
    }

    protected override async IAsyncEnumerable<ChatMessage> ExecuteStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        RequestOverrides? overrides,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var modelId   = overrides?.Model ?? Model;
        var modelPath = ResolveModelPath(modelId);

        await EnsureModelLoadedAsync(modelPath, messages, cancellationToken);

        var userMessage = messages.LastOrDefault(x => x.Role == ChatRole.User)?.Content ?? string.Empty;
        var temperature = (float)(overrides?.Temperature ?? 0.1);
        var maxTokens   = overrides?.MaxTokens ?? 4096;

        var inferenceParams = new InferenceParams
        {
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = temperature },
            MaxTokens        = maxTokens,
            AntiPrompts      = ["User:"]
        };

        // Bridge LLama's IAsyncEnumerable to a channel so inference runs on the threadpool
        // and does not block the caller.
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });

        _ = Task.Run(async () =>
        {
            // Reasoning models emit output in named channel blocks:
            //   analysis\n{...}  commentary\n{...}  final\n{actual answer}
            // We buffer tokens until "final\n" is detected and only surface the final section.
            // If no channel marker appears we emit everything.
            const string FinalMarker = "final\n";
            var pending        = new StringBuilder();
            var inFinalChannel = false;
            Exception? error   = null;

            try
            {
                await foreach (var tok in _llamaSession!
                    .ChatAsync(new ChatHistory.Message(AuthorRole.User, userMessage), inferenceParams)
                    .WithCancellation(cancellationToken))
                {
                    if (inFinalChannel)
                    {
                        channel.Writer.TryWrite(tok);
                    }
                    else
                    {
                        pending.Append(tok);
                        var buf       = pending.ToString();
                        var markerIdx = buf.IndexOf(FinalMarker, StringComparison.Ordinal);

                        if (markerIdx >= 0)
                        {
                            inFinalChannel = true;
                            var afterMarker = buf[(markerIdx + FinalMarker.Length)..];
                            if (afterMarker.Length > 0)
                                channel.Writer.TryWrite(afterMarker);
                            pending.Clear();
                        }
                        else if (buf.Length > 200)
                        {
                            // Sliding window: keep only enough to detect a split marker.
                            pending.Remove(0, buf.Length - FinalMarker.Length);
                        }
                    }
                }

                // Fallback: model does not use channel format — emit everything buffered.
                if (!inFinalChannel && pending.Length > 0)
                    channel.Writer.TryWrite(pending.ToString());
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                channel.Writer.TryComplete(error);
            }
        }, cancellationToken);

        await foreach (var text in channel.Reader.ReadAllAsync(cancellationToken))
            yield return new ChatMessage(ChatRole.Assistant, text, DateTimeOffset.UtcNow, Model: modelId);
    }

    private async ValueTask EnsureModelLoadedAsync(
        string modelPath,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (_llamaSession != null && _loadedModelPath == modelPath)
            return;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_llamaSession != null && _loadedModelPath == modelPath)
                return;

            DisposeModel();
            _loadedModelPath = modelPath;

            await Task.Run(() =>
            {
                var parameters = new ModelParams(modelPath) { ContextSize = 4096 };
                _weights = LLamaWeights.LoadFromFile(parameters);
                _context = _weights.CreateContext(parameters);

                var executor = new InteractiveExecutor(_context);
                var history  = new ChatHistory();

                var systemPrompt = messages.FirstOrDefault(x => x.Role == ChatRole.System)?.Content;
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    history.AddMessage(AuthorRole.System, systemPrompt);

                _llamaSession = new LLama.ChatSession(executor, history);
                _llamaSession.WithHistoryTransform(new PromptTemplateTransformer(_weights, withAssistant: true));
                _llamaSession.WithOutputTransform(new LLamaTransforms.KeywordTextOutputStreamTransform(
                    ["User:", "\ufffd"], redundancyLength: 5));
            }, cancellationToken);

            await _localModelManager.LoadAsync(Model, modelPath, cancellationToken);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static string ResolveModelPath(string modelId)
    {
        if (Path.IsPathRooted(modelId))
            return modelId;

        var modelDir = Environment.GetEnvironmentVariable("LLAMASHARP_MODEL_DIR")
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");

        return Path.Combine(modelDir, modelId);
    }

    private void DisposeModel()
    {
        _llamaSession = null;
        _context?.Dispose();
        _context = null;
        _weights?.Dispose();
        _weights = null;
    }

    public void Dispose()
    {
        _loadLock.Dispose();
        DisposeModel();
    }
}
