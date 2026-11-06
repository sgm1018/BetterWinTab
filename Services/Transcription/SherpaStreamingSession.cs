using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BetterWinTab.Models.Transcription;

namespace BetterWinTab.Services.Transcription;

/// <summary>Owns one isolated recognizer process for the lifetime of a recording.</summary>
public sealed class SherpaStreamingSession : IAsyncDisposable
{
    private readonly string _workerPath;
    private readonly Channel<StreamingTranscriptEvent> _events = Channel.CreateBounded<StreamingTranscriptEvent>(256);
    private readonly SemaphoreSlim _inputGate = new(1, 1);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _reader;
    private Task<string>? _errors;
    private CancellationTokenRegistration _cancellation;
    private bool _finishing;
    private bool _disposed;

    public SherpaStreamingSession(string? workerPath = null) => _workerPath = workerPath ??
        Path.Combine(AppContext.BaseDirectory, "asr-worker", "AsrWorker.exe");

    public async Task StartAsync(AsrModelStore models, string locale, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process != null) throw new InvalidOperationException("The speech session has already started.");
        if (!File.Exists(_workerPath)) throw new FileNotFoundException("The speech worker is missing. Rebuild BetterWinTab.", _workerPath);
        if (!models.IsStreamingInstalled) throw new InvalidOperationException("Download the speech and speaker models before recording.");
        var start = new ProcessStartInfo(_workerPath)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(_workerPath)!
        };
        foreach (var argument in new[] { "--stream", models.GetModelPath(AsrModelCatalog.StreamingModelId),
                     models.SegmentationModelPath, models.EmbeddingModelPath, locale }) start.ArgumentList.Add(argument);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the local speech worker.");
        try { _process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        _cancellation = cancellationToken.Register(() => _lifetime.Cancel());
        _lifetime.Token.Register(Kill);
        _errors = _process.StandardError.ReadToEndAsync();
        _reader = ReadOutputAsync();
        try { await _ready.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken).ConfigureAwait(false); }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
    }

    public IAsyncEnumerable<StreamingTranscriptEvent> ReadEventsAsync(CancellationToken cancellationToken = default) =>
        _events.Reader.ReadAllAsync(cancellationToken);

    public void Abort()
    {
        if (!_disposed) _lifetime.Cancel();
    }

    public Task SendIdleAsync(string source, double recordingSeconds, CancellationToken cancellationToken = default)
    {
        if (source is not ("loopback" or "microphone") || !double.IsFinite(recordingSeconds) || recordingSeconds < 0)
            throw new InvalidDataException("Invalid idle audio source or recording time.");
        return SendMessageAsync(new StreamingAudioMessage("idle", source, StartSeconds: recordingSeconds), cancellationToken);
    }

    public Task SendAudioAsync(string source, byte[] pcm16, double startSeconds,
        CancellationToken cancellationToken = default)
    {
        if (source is not ("loopback" or "microphone")) throw new ArgumentException("Unknown audio source.", nameof(source));
        if (pcm16.Length % 2 != 0 || !double.IsFinite(startSeconds) || startSeconds < 0)
            throw new InvalidDataException("Invalid PCM16 packet or recording time.");
        if (pcm16.Length == 0) return Task.CompletedTask;
        return SendMessageAsync(new StreamingAudioMessage("audio", source, Convert.ToBase64String(pcm16), startSeconds), cancellationToken);
    }

    private async Task SendMessageAsync(StreamingAudioMessage message, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _inputGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_finishing || _process == null || _process.HasExited)
                throw new InvalidOperationException("The local speech worker is no longer accepting audio.");
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), linked.Token).ConfigureAwait(false);
        }
        finally { _inputGate.Release(); }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _inputGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (!_finishing && _process != null && !_process.HasExited)
            {
                _finishing = true;
                await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new StreamingAudioMessage("finish"))
                    .AsMemory(), linked.Token).ConfigureAwait(false);
                _process.StandardInput.Close();
            }
        }
        finally { _inputGate.Release(); }
        if (_reader != null) await _reader.WaitAsync(TimeSpan.FromMinutes(3), linked.Token).ConfigureAwait(false);
    }

    private async Task ReadOutputAsync()
    {
        Exception? failure = null;
        var completed = false;
        try
        {
            while (await _process!.StandardOutput.ReadLineAsync(_lifetime.Token).ConfigureAwait(false) is { } line)
            {
                if (!line.StartsWith("BWTA_EVENT:", StringComparison.Ordinal)) continue;
                var update = JsonSerializer.Deserialize<StreamingTranscriptEvent>(line["BWTA_EVENT:".Length..])
                    ?? throw new InvalidDataException("The speech worker returned an empty event.");
                if (update.Type == "error") throw new InvalidOperationException(update.Message ?? "The speech worker failed.");
                if (update.Type == "ready") _ready.TrySetResult();
                if (update.Type == "completed") completed = true;
                await _events.Writer.WriteAsync(update, _lifetime.Token).ConfigureAwait(false);
            }
            await _process.WaitForExitAsync(_lifetime.Token).ConfigureAwait(false);
            if (_process.ExitCode != 0 || !completed)
            {
                var error = _errors == null ? "" : await _errors.ConfigureAwait(false);
                throw new InvalidOperationException($"The speech worker stopped unexpectedly (exit {_process.ExitCode}). {error[^Math.Min(2000, error.Length)..]}");
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            _ready.TrySetException(ex);
            Kill();
            throw;
        }
        finally { _events.Writer.TryComplete(failure); }
    }

    private void Kill()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Kill();
        if (_reader != null) { try { await _reader.ConfigureAwait(false); } catch { } }
        if (_errors != null) { try { await _errors.ConfigureAwait(false); } catch { } }
        _cancellation.Dispose();
        _process?.Dispose();
        _lifetime.Dispose();
        _inputGate.Dispose();
    }
}
