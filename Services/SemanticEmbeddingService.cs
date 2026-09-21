using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace BetterWinTab.Services;

public sealed class SemanticEmbeddingService : IAsyncDisposable
{
    private const int MaxTokens = 512;
    private readonly SemanticModelService _modelService;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private InferenceSession? _session;
    private SentencePieceTokenizer? _tokenizer;

    public SemanticEmbeddingService(SemanticModelService modelService)
    {
        _modelService = modelService;
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default)
        => await EmbedAsync("query: " + text, cancellationToken);

    public async Task<float[]> EmbedPassageAsync(string text, CancellationToken cancellationToken = default)
        => await EmbedAsync("passage: " + text, cancellationToken);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => EnsureInitializedAsync(cancellationToken);

    private async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var tokenizer = _tokenizer ?? throw new InvalidOperationException("Tokenizer is not initialized.");
        var session = _session ?? throw new InvalidOperationException("ONNX session is not initialized.");
        var tokenIds = tokenizer.EncodeToIds(
            text,
            addBeginningOfSentence: true,
            addEndOfSentence: true,
            maxTokenCount: MaxTokens,
            out _,
            out _,
            considerNormalization: true,
            considerPreTokenization: true);

        var inputIds = tokenIds.Select(id => (long)id).ToArray();
        var attentionMask = Enumerable.Repeat(1L, inputIds.Length).ToArray();
        var tokenTypeIds = new long[inputIds.Length];
        var inputNames = session.InputMetadata.Keys.ToArray();
        var inputs = new List<NamedOnnxValue>(inputNames.Length);

        foreach (var inputName in inputNames)
        {
            var tensor = inputName.Contains("attention", StringComparison.OrdinalIgnoreCase)
                ? attentionMask
                : inputName.Contains("token_type", StringComparison.OrdinalIgnoreCase)
                    ? tokenTypeIds
                    : inputIds;
            inputs.Add(NamedOnnxValue.CreateFromTensor(
                inputName,
                new DenseTensor<long>(tensor, new[] { 1, tensor.Length })));
        }

        using var results = session.Run(inputs);
        var output = results.First().AsTensor<float>();
        return MeanPool(output, attentionMask);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_session is not null && _tokenizer is not null)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null && _tokenizer is not null)
                return;

            await _modelService.EnsureModelAsync(cancellationToken);
            await using var tokenizerStream = File.OpenRead(_modelService.TokenizerPath);
            _tokenizer = SentencePieceTokenizer.Create(tokenizerStream, addBeginningOfSentence: true, addEndOfSentence: true);
            _session = new InferenceSession(_modelService.ModelPath);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static float[] MeanPool(Tensor<float> output, IReadOnlyList<long> attentionMask)
    {
        if (output.Dimensions.Length != 3)
            return output.ToArray();

        var sequenceLength = output.Dimensions[1];
        var embeddingSize = output.Dimensions[2];
        var embedding = new float[embeddingSize];
        var tokenCount = Math.Min(sequenceLength, attentionMask.Count);

        for (var tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
        {
            if (attentionMask[tokenIndex] == 0)
                continue;

            for (var dimension = 0; dimension < embeddingSize; dimension++)
                embedding[dimension] += output[0, tokenIndex, dimension];
        }

        if (tokenCount > 0)
        {
            for (var dimension = 0; dimension < embeddingSize; dimension++)
                embedding[dimension] /= tokenCount;
        }

        return embedding;
    }

    public ValueTask DisposeAsync()
    {
        _session?.Dispose();
        _initializationLock.Dispose();
        return ValueTask.CompletedTask;
    }
}