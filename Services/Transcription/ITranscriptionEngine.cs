using BetterWinTab.Models.Transcription;

namespace BetterWinTab.Services.Transcription;

public interface ITranscriptionEngine
{
    string ModelId { get; }
    AsrExecutionMode ExecutionMode { get; }
    Task<TranscriptDocument> TranscribeWaveFileAsync(string wavePath, TranscriptionOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class TranscriptionService
{
    private readonly IReadOnlyDictionary<string, ITranscriptionEngine> _engines;

    public TranscriptionService(IEnumerable<ITranscriptionEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        _engines = engines.ToDictionary(e => e.ModelId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AsrModel> GetModels() => AsrModelCatalog.Models;

    public Task<TranscriptDocument> TranscribeWaveFileAsync(string modelId, string wavePath,
        TranscriptionOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!_engines.TryGetValue(modelId, out var engine))
            throw new NotSupportedException($"No ASR worker is configured for model '{modelId}'.");
        options ??= new TranscriptionOptions();
        if (options.LanguageMode == LanguageSelectionMode.Manual && string.IsNullOrWhiteSpace(options.Locale))
            throw new ArgumentException("A locale is required when manual language selection is enabled.", nameof(options));
        return engine.TranscribeWaveFileAsync(wavePath, options, cancellationToken);
    }
}
