using System.Diagnostics;
using System.Text.Json;
using BetterWinTab.Models.Transcription;

namespace BetterWinTab.Services.Transcription;

/// <summary>Runs sherpa in its own process so its ONNX Runtime DLL cannot bind to the app's ORT.</summary>
public sealed class SherpaOfflineWorkerEngine : ITranscriptionEngine
{
    private readonly AsrModelStore _modelStore;
    private readonly string _workerPath;

    public SherpaOfflineWorkerEngine(AsrModelStore modelStore, string? workerPath = null)
    {
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _workerPath = workerPath ?? Path.Combine(AppContext.BaseDirectory, "asr-worker", "AsrWorker.exe");
    }

    public string ModelId => "moonshine-v2-base-es";
    public AsrExecutionMode ExecutionMode => AsrExecutionMode.Offline;

    public async Task<TranscriptDocument> TranscribeWaveFileAsync(string wavePath, TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavePath);
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(wavePath)) throw new FileNotFoundException("Recording track not found.", wavePath);
        if (options.LanguageMode == LanguageSelectionMode.Manual &&
            !(options.Locale?.StartsWith("es", StringComparison.OrdinalIgnoreCase) ?? false))
            throw new NotSupportedException("Moonshine Base Spanish only supports Spanish transcription.");
        if (!File.Exists(_workerPath))
            throw new FileNotFoundException("The isolated sherpa worker has not been published beside the app.", _workerPath);

        var model = AsrModelCatalog.Models.Single(m => m.Id == ModelId);
        if (!_modelStore.IsInstalled(model))
            throw new InvalidOperationException("Import the Moonshine Spanish model archive before transcribing.");

        var start = new ProcessStartInfo(_workerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_workerPath)!
        };
        start.ArgumentList.Add(_modelStore.GetModelPath(ModelId));
        start.ArgumentList.Add(Path.GetFullPath(wavePath));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated sherpa worker.");
        using var cancellation = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The offline ASR worker failed: {error.Trim()}");

        var resultLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.StartsWith("BWTA_RESULT:", StringComparison.Ordinal));
        if (resultLine == null)
            throw new InvalidDataException($"The ASR worker returned no result: {output.Trim()}");
        var response = JsonSerializer.Deserialize<WorkerResponse>(resultLine["BWTA_RESULT:".Length..])
            ?? throw new InvalidDataException("The ASR worker returned an empty response.");
        var segment = string.IsNullOrWhiteSpace(response.Text)
            ? Array.Empty<TranscriptSegment>()
            : new[] { new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(response.DurationSeconds), response.Text.Trim()) };
        return new TranscriptDocument(segment, Array.Empty<TranscriptParticipant>(), null, ModelId,
            TranscriptMode.Offline, DateTimeOffset.UtcNow);
    }

    private sealed record WorkerResponse(string Text, double DurationSeconds);
}
