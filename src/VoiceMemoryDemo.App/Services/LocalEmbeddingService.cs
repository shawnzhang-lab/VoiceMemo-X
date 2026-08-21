using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tokenizers.HuggingFace.Tokenizer;

namespace VoiceMemoryDemo.App.Services;

public sealed class LocalEmbeddingService : IDisposable
{
    public const string ModelId = "intfloat/multilingual-e5-small";
    public const int Dimensions = 384;
    private const int MaxTokens = 512;

    private readonly string _modelDirectory;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private readonly Lazy<RuntimeState> _runtime;
    private bool _disposed;

    public LocalEmbeddingService(string? modelDirectory = null)
    {
        _modelDirectory = modelDirectory ?? Path.Combine(
            AppContext.BaseDirectory, "Models", "multilingual-e5-small");
        _runtime = new Lazy<RuntimeState>(LoadRuntime, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool ModelFilesAvailable =>
        (File.Exists(Path.Combine(_modelDirectory, "model-int8.onnx")) ||
         File.Exists(Path.Combine(_modelDirectory, "model.onnx"))) &&
        File.Exists(Path.Combine(_modelDirectory, "tokenizer.json"));

    public Task<float[]> CreateQueryEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        CreateEmbeddingAsync("query: " + text.Trim(), cancellationToken);

    public Task<float[]> CreatePassageEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        CreateEmbeddingAsync("passage: " + text.Trim(), cancellationToken);

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        if (!ModelFilesAvailable) return;
        _ = await CreateQueryEmbeddingAsync("语义记忆预热", cancellationToken);
    }

    private async Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return new float[Dimensions];
        if (!ModelFilesAvailable)
        {
            throw new FileNotFoundException("本地语义模型文件不完整。", _modelDirectory);
        }

        await _inferenceLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => RunInference(text), cancellationToken);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    private float[] RunInference(string text)
    {
        var runtime = _runtime.Value;
        var encoding = runtime.Tokenizer.Encode(
            text,
            addSpecialTokens: true,
            includeTypeIds: true,
            includeAttentionMask: true).First();

        var sourceIds = encoding.Ids.Select(value => (long)value).ToArray();
        if (sourceIds.Length == 0) return new float[Dimensions];

        var length = Math.Min(sourceIds.Length, MaxTokens);
        var ids = new long[length];
        Array.Copy(sourceIds, ids, length);
        if (sourceIds.Length > MaxTokens)
        {
            ids[^1] = sourceIds[^1];
        }

        var typeIds = new long[length];
        for (var index = 0; index < length && index < encoding.TypeIds.Count; index++)
        {
            typeIds[index] = encoding.TypeIds[index];
        }
        var attentionMask = Enumerable.Repeat(1L, length).ToArray();

        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, [1, length])),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, [1, length])),
            NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(typeIds, [1, length]))
        };

        using var results = runtime.Session.Run(inputs);
        var tokenEmbeddings = results.First().AsTensor<float>();
        var pooled = new float[Dimensions];
        var activeTokens = 0;

        for (var token = 0; token < length; token++)
        {
            if (attentionMask[token] == 0) continue;
            activeTokens++;
            for (var dimension = 0; dimension < Dimensions; dimension++)
            {
                pooled[dimension] += tokenEmbeddings[0, token, dimension];
            }
        }

        if (activeTokens == 0) return pooled;
        double squaredNorm = 0;
        for (var dimension = 0; dimension < Dimensions; dimension++)
        {
            pooled[dimension] /= activeTokens;
            squaredNorm += pooled[dimension] * pooled[dimension];
        }

        var norm = Math.Sqrt(squaredNorm);
        if (norm <= 0) return pooled;
        for (var dimension = 0; dimension < Dimensions; dimension++)
        {
            pooled[dimension] = (float)(pooled[dimension] / norm);
        }
        return pooled;
    }

    private RuntimeState LoadRuntime()
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8),
            InterOpNumThreads = 1
        };
        var quantizedPath = Path.Combine(_modelDirectory, "model-int8.onnx");
        var modelPath = File.Exists(quantizedPath)
            ? quantizedPath
            : Path.Combine(_modelDirectory, "model.onnx");
        var session = new InferenceSession(modelPath, options);
        var tokenizer = Tokenizer.FromFile(Path.Combine(_modelDirectory, "tokenizer.json"));
        return new RuntimeState(session, tokenizer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_runtime.IsValueCreated)
        {
            _runtime.Value.Dispose();
        }
        _inferenceLock.Dispose();
    }

    private sealed class RuntimeState(InferenceSession session, Tokenizer tokenizer) : IDisposable
    {
        public InferenceSession Session { get; } = session;
        public Tokenizer Tokenizer { get; } = tokenizer;

        public void Dispose()
        {
            Tokenizer.Dispose();
            Session.Dispose();
        }
    }
}
