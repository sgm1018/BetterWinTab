using System.Diagnostics;
using BetterWinTab.Models;
using BetterWinTab.Models.Transcription;
using BetterWinTab.Services.Transcription;
using CommunityToolkit.Mvvm.Input;
using NAudio.Wave;

namespace BetterWinTab.ViewModels;

public partial class CallsViewModel
{
    private SherpaStreamingSession? _streamingSession;
    private Task? _streamingEventsTask;
    private Task? _microphonePacketTask;
    private Task? _loopbackPacketTask;
    private TranscriptAccumulator? _liveTranscript;
    private bool _isStartingRecording;
    private bool _isFinalizingRecording;
    private volatile bool _transcriptionInterrupted;
    private double _lastQueuedAudioSecond;

    private async Task PrepareSpeechModelsAsync(CancellationToken cancellationToken)
    {
        IsInstallingAsrModel = true;
        try
        {
            await _asrModelStore.EnsureStreamingModelsAsync(new Progress<string>(message => Status = message), cancellationToken);
        }
        finally
        {
            IsInstallingAsrModel = false;
            OnPropertyChanged(nameof(IsAsrModelInstalled));
        }
    }

    [RelayCommand]
    private async Task CreateRecordingAsync()
    {
        if (_isStartingRecording || _isFinalizingRecording || IsRecording || IsTranscribing || IsInstallingAsrModel) return;
        _isStartingRecording = true;
        _captureLifetime?.Dispose();
        _captureLifetime = new CancellationTokenSource();
        var token = _captureLifetime.Token;
        try
        {
            if (!IsAsrModelInstalled) await PrepareSpeechModelsAsync(token);
            Status = "Preparing live transcription and speaker detection…";
            IsTranscribing = true;
            _streamingSession = new SherpaStreamingSession();
            await _streamingSession.StartAsync(_asrModelStore, IsManualLanguage ? _appSettings.Calls.Locale : "auto", token);
            IsTranscribing = false;
            for (var remaining = 3; remaining > 0; remaining--)
            {
                CountdownSeconds = remaining;
                await Task.Delay(1000, token);
            }
            CountdownSeconds = 0;
            var includeMicrophone = IncludeMicrophone;
            var microphoneId = includeMicrophone ? SelectedMicrophone?.Id : null;
            var outputId = SelectedOutput?.Id;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BetterWinTab", "recordings", Guid.NewGuid().ToString("N"));
            await _capture.StartAsync(new AudioCaptureOptions
            {
                IncludeMicrophone = includeMicrophone, MicrophoneDeviceId = microphoneId,
                LoopbackDeviceId = outputId, RecordingDirectory = directory
            }, token);
            var metadata = await _recordings.CreateAsync(microphoneId, outputId, cancellationToken: token,
                microphoneAudioFilePath: _capture.MicrophoneRecordingPath,
                loopbackAudioFilePath: _capture.LoopbackRecordingPath, transcriptionStarted: true);
            _activeRecordingId = metadata.Id;
            _liveTranscript = new TranscriptAccumulator();
            _transcriptionInterrupted = false;
            _lastQueuedAudioSecond = 0;
            Recordings.Insert(0, metadata);
            SelectedRecording = metadata;
            ClearTranscriptMessages();
            IsRecording = true;
            IsTranscribing = true;
            _streamingEventsTask = ConsumeStreamingEventsAsync(_streamingSession, metadata.Id, _liveTranscript, true, token);
            _loopbackPacketTask = Task.Run(() => FeedCapturePacketsAsync(_capture.ReadLoopbackPacketsAsync(token),
                _capture.LoopbackWaveFormat!, "loopback", _streamingSession, token), token);
            _microphonePacketTask = _capture.MicrophoneWaveFormat is { } microphoneFormat
                ? Task.Run(() => FeedCapturePacketsAsync(_capture.ReadMicrophonePacketsAsync(token),
                    microphoneFormat, "microphone", _streamingSession, token), token)
                : Task.CompletedTask;
            Status = includeMicrophone ? "Listening to your microphone and system audio…" : "Listening to system audio…";
        }
        catch (Exception ex)
        {
            _captureLifetime.Cancel();
            if (_capture.IsCapturing) await _capture.StopAsync();
            if (_streamingSession != null) await _streamingSession.DisposeAsync();
            _streamingSession = null;
            IsRecording = false;
            IsTranscribing = false;
            CountdownSeconds = 0;
            Status = ex is OperationCanceledException ? "Recording cancelled." : $"Could not start recording: {ex.Message}";
        }
        finally { _isStartingRecording = false; }
    }

    private async Task FeedCapturePacketsAsync(IAsyncEnumerable<AudioCapturePacket> packets, WaveFormat format,
        string source, SherpaStreamingSession session, CancellationToken cancellationToken)
    {
        var converter = new StreamingPcmConverter(format);
        var nextSecond = 0d;
        var idleSent = false;
        await using var enumerator = packets.GetAsyncEnumerator(cancellationToken);
        var pendingPacket = enumerator.MoveNextAsync().AsTask();
        while (true)
        {
            if (await Task.WhenAny(pendingPacket, Task.Delay(400, cancellationToken)).ConfigureAwait(false) != pendingPacket)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recordingSecond = (Stopwatch.GetTimestamp() - _capture.CaptureStartedTimestamp) / (double)Stopwatch.Frequency;
                if (!idleSent && nextSecond > 0 && recordingSecond - nextSecond >= 1)
                {
                    await session.SendIdleAsync(source, recordingSecond, cancellationToken).ConfigureAwait(false);
                    idleSent = true;
                }
                continue;
            }
            if (!await pendingPacket.ConfigureAwait(false)) break;
            var packet = enumerator.Current;
            var packetSeconds = packet.Data.Length / (double)format.AverageBytesPerSecond;
            var arrivedAt = (packet.TimestampTicks - _capture.CaptureStartedTimestamp) / (double)Stopwatch.Frequency;
            var actualStart = Math.Max(0, arrivedAt - packetSeconds);
            if (actualStart - nextSecond > 0.2) nextSecond = actualStart;
            var pcm = converter.Convert(packet.Data);
            await session.SendAudioAsync(source, pcm, nextSecond, cancellationToken).ConfigureAwait(false);
            nextSecond += pcm.Length / 32000d;
            Volatile.Write(ref _lastQueuedAudioSecond, Math.Max(nextSecond, Volatile.Read(ref _lastQueuedAudioSecond)));
            idleSent = false;
            pendingPacket = enumerator.MoveNextAsync().AsTask();
        }
    }

    private async Task ConsumeStreamingEventsAsync(SherpaStreamingSession session, string recordingId,
        TranscriptAccumulator transcript, bool persistWhileStreaming, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in session.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (update.Type is not ("partial" or "final")) continue;
                transcript.Apply(update);
                var snapshot = transcript.Snapshot();
                await RunOnUiAsync(() =>
                {
                    if (SelectedRecording?.Id == recordingId)
                    {
                        ApplyStreamingUpdate(update);
                        if (update.Type == "final")
                        {
                            SelectedRecording.Transcript = snapshot.ToList();
                            TranscriptText = FormatTranscript(snapshot);
                        }
                    }
                    var libraryItem = Recordings.FirstOrDefault(item => item.Id == recordingId);
                    if (update.Type == "final" && libraryItem != null && !ReferenceEquals(libraryItem, SelectedRecording))
                        libraryItem.Transcript = snapshot.ToList();
                    if (_activeRecordingId == recordingId && IsRecording)
                    {
                        var behind = Volatile.Read(ref _lastQueuedAudioSecond) - update.StartSeconds - update.DurationSeconds;
                        Status = behind > 6
                            ? "Transcription is catching up. The full audio is still being saved."
                            : "Listening… Words and speaker labels update as speech is recognized.";
                    }
                }).ConfigureAwait(false);
                if (persistWhileStreaming && update.Type == "final")
                    await _recordings.SetTranscriptAsync(recordingId, snapshot, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            session.Abort();
            _transcriptionInterrupted = true;
            if (persistWhileStreaming)
            {
                try { await _recordings.SetTranscriptAsync(recordingId, transcript.Snapshot(true), cancellationToken).ConfigureAwait(false); }
                catch (Exception saveError) { Debug.WriteLine($"Could not save incomplete transcript: {saveError.Message}"); }
                try { await RunOnUiAsync(() => Status = $"Live transcription interrupted. Recording continues and the original audio is preserved. {ex.Message}").ConfigureAwait(false); }
                catch (Exception uiError) { Debug.WriteLine($"Could not show speech error: {uiError.Message}"); }
            }
            else throw;
        }
    }

    [RelayCommand]
    public async Task StopRecordingAsync()
    {
        if (_isFinalizingRecording) return;
        if (_isStartingRecording) { _captureLifetime?.Cancel(); return; }
        var recordingId = _activeRecordingId;
        if (recordingId == null) return;
        _isFinalizingRecording = true;
        try
        {
            Status = "Finishing the last words and speaker labels…";
            await _capture.StopAsync();
            IsRecording = false;
            try
            {
                await Task.WhenAll(_microphonePacketTask ?? Task.CompletedTask, _loopbackPacketTask ?? Task.CompletedTask)
                    .WaitAsync(TimeSpan.FromSeconds(30));
                if (_streamingSession != null) await _streamingSession.CompleteAsync();
            }
            catch (Exception)
            {
                _transcriptionInterrupted = true;
                if (_streamingSession != null) await _streamingSession.DisposeAsync();
            }
            if (_streamingEventsTask != null)
            {
                try { await _streamingEventsTask.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception) { _transcriptionInterrupted = true; }
            }
            await _recordings.CompleteAsync(recordingId);
            _transcriptionInterrupted |= _capture.MicrophoneDroppedPackets > 0 || _capture.LoopbackDroppedPackets > 0;
            if (_streamingSession != null) await _streamingSession.DisposeAsync();
            _streamingSession = null;
            if (_transcriptionInterrupted)
                Status = "Recording saved. Live transcription was interrupted; the available text and original audio are preserved.";
            else
            {
                var saved = await _recordings.GetAsync(recordingId);
                Status = saved?.Transcript.Count > 0 ? "Recording and speaker transcript saved." : "Recording saved. No speech was detected.";
            }
            if (_capture.CaptureError != null)
                Status = $"Audio capture stopped unexpectedly: {_capture.CaptureError.Message}. Saved audio has been preserved.";
            if (_capture.RecordingWriteError != null)
                Status = $"Recording saved with missing audio: {_capture.RecordingWriteError.Message}";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Status = $"Could not finish the transcript. The recorded WAV files have been preserved: {ex.Message}";
        }
        finally
        {
            _activeRecordingId = null;
            _captureLifetime?.Cancel();
            if (_streamingSession != null) await _streamingSession.DisposeAsync();
            _streamingSession = null;
            _captureLifetime?.Dispose();
            _captureLifetime = null;
            _microphonePacketTask = _loopbackPacketTask = _streamingEventsTask = null;
            _liveTranscript = null;
            IsRecording = false;
            IsTranscribing = false;
            _isFinalizingRecording = false;
        }
    }

    private async Task TranscribeRecordingAsync(string recordingId)
    {
        var recording = await _recordings.GetAsync(recordingId);
        if (recording == null) return;
        if (!await _recordings.TryBeginTranscriptionAsync(recordingId))
        {
            Status = "This recording already has a transcription. It will not be generated again.";
            return;
        }
        recording.TranscriptionStarted = true;
        if (SelectedRecording?.Id == recordingId) SelectedRecording.TranscriptionStarted = true;
        var startingItem = Recordings.FirstOrDefault(item => item.Id == recordingId);
        if (startingItem != null) startingItem.TranscriptionStarted = true;
        IsTranscribing = true;
        var transcript = new TranscriptAccumulator();
        using var lifetime = new CancellationTokenSource();
        await using var session = new SherpaStreamingSession();
        Task? events = null;
        try
        {
            if (!IsAsrModelInstalled) await PrepareSpeechModelsAsync(lifetime.Token);
            Status = "Preparing speech and speaker detection for the saved recording…";
            await session.StartAsync(_asrModelStore, IsManualLanguage ? _appSettings.Calls.Locale : "auto", lifetime.Token);
            if (SelectedRecording?.Id == recordingId) ClearTranscriptMessages();
            events = ConsumeStreamingEventsAsync(session, recordingId, transcript, false, lifetime.Token);
            var tracks = new[] { (recording.LoopbackAudioFilePath ?? recording.AudioFilePath, "loopback"),
                (recording.MicrophoneAudioFilePath, "microphone") };
            var foundTrack = false;
            foreach (var (path, source) in tracks)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                foundTrack = true;
                Status = source == "loopback" ? "Transcribing saved audio and identifying voices…" : "Transcribing the saved microphone track…";
                await Task.Run(async () =>
                {
                    using var wave = new WaveFileReader(path);
                    var converter = new StreamingPcmConverter(wave.WaveFormat);
                    var buffer = new byte[wave.WaveFormat.AverageBytesPerSecond / 10 /
                        wave.WaveFormat.BlockAlign * wave.WaveFormat.BlockAlign];
                    var nextSecond = 0d;
                    int read;
                    while ((read = wave.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        var pcm = converter.Convert(buffer.AsSpan(0, read).ToArray());
                        await session.SendAudioAsync(source, pcm, nextSecond, lifetime.Token).ConfigureAwait(false);
                        nextSecond += pcm.Length / 32000d;
                    }
                }, lifetime.Token);
            }
            if (!foundTrack) throw new FileNotFoundException("This recording has no saved audio tracks to transcribe.");
            await session.CompleteAsync(lifetime.Token);
            await events;
            var snapshot = transcript.Snapshot();
            await _recordings.SetTranscriptAsync(recordingId, snapshot);
            var updated = await _recordings.GetAsync(recordingId);
            if (updated != null && SelectedRecording?.Id == recordingId) SelectedRecording = updated;
            var libraryItem = Recordings.FirstOrDefault(item => item.Id == recordingId);
            if (libraryItem != null) libraryItem.Transcript = snapshot.ToList();
            Status = snapshot.Length == 0 ? "No speech was detected in the saved audio." : "Transcript saved with speaker labels.";
        }
        catch (Exception ex)
        {
            lifetime.Cancel();
            await session.DisposeAsync();
            if (events != null) { try { await events; } catch { } }
            if (SelectedRecording?.Id == recordingId) SelectedRecording = recording;
            Status = $"Could not transcribe the saved audio: {ex.Message}";
        }
        finally { IsTranscribing = false; }
    }

    private static string FormatTranscript(IEnumerable<AudioTranscriptSegment> segments) => TranscriptTextEditor.Format(segments);
}
