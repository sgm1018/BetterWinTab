using System.Diagnostics;
using System.Threading.Channels;
using BetterWinTab.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BetterWinTab.Services;

/// <summary>
/// Captures microphone and render-loopback endpoints independently. Packet bytes are copied
/// inside NAudio callbacks and delivered through bounded channels; consumers must align the
/// independent device clocks themselves using the monotonic packet timestamps.
/// </summary>
public sealed class AudioCaptureService : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private WasapiCapture? _microphone;
    private WasapiLoopbackCapture? _loopback;
    private MMDevice? _microphoneDevice;
    private MMDevice? _loopbackDevice;
    private Channel<AudioCapturePacket>? _microphonePackets;
    private Channel<AudioCapturePacket>? _loopbackPackets;
    private Channel<AudioCapturePacket>? _microphoneFilePackets;
    private Channel<AudioCapturePacket>? _loopbackFilePackets;
    private Task? _microphoneWriterTask;
    private Task? _loopbackWriterTask;
    private TaskCompletionSource? _microphoneStopped;
    private TaskCompletionSource? _loopbackStopped;
    private bool _disposed;
    private Exception? _recordingWriteError;
    private Exception? _captureError;

    private long _microphoneDroppedPackets;
    private long _loopbackDroppedPackets;
    private long _captureStartedTimestamp;

    public bool IsCapturing => _microphone != null || _loopback != null;
    public long MicrophoneDroppedPackets => Interlocked.Read(ref _microphoneDroppedPackets);
    public long LoopbackDroppedPackets => Interlocked.Read(ref _loopbackDroppedPackets);
    public long TimestampFrequency => Stopwatch.Frequency;
    public long CaptureStartedTimestamp => Interlocked.Read(ref _captureStartedTimestamp);
    public string? MicrophoneRecordingPath { get; private set; }
    public string? LoopbackRecordingPath { get; private set; }
    public WaveFormat? MicrophoneWaveFormat => _microphone?.WaveFormat;
    public WaveFormat? LoopbackWaveFormat => _loopback?.WaveFormat;
    public Exception? RecordingWriteError => Volatile.Read(ref _recordingWriteError);
    public Exception? CaptureError => Volatile.Read(ref _captureError);

    /// <summary>Single-consumer packet stream. A new channel is created on each start.</summary>
    public IAsyncEnumerable<AudioCapturePacket> ReadMicrophonePacketsAsync(CancellationToken cancellationToken = default) =>
        (_microphonePackets ?? throw new InvalidOperationException("Capture has not been started."))
            .Reader.ReadAllAsync(cancellationToken);

    /// <summary>Single-consumer packet stream. A new channel is created on each start.</summary>
    public IAsyncEnumerable<AudioCapturePacket> ReadLoopbackPacketsAsync(CancellationToken cancellationToken = default) =>
        (_loopbackPackets ?? throw new InvalidOperationException("Capture has not been started."))
            .Reader.ReadAllAsync(cancellationToken);

    public async Task StartAsync(AudioCaptureOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new AudioCaptureOptions();
        ValidateOptions(options);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsCapturing)
                throw new InvalidOperationException("Audio capture is already running.");

            Interlocked.Exchange(ref _microphoneDroppedPackets, 0);
            Interlocked.Exchange(ref _loopbackDroppedPackets, 0);
            Volatile.Write(ref _recordingWriteError, null);
            Volatile.Write(ref _captureError, null);
            _microphonePackets = CreatePacketChannel(options.PacketBufferCapacity);
            _loopbackPackets = CreatePacketChannel(options.PacketBufferCapacity);
            if (!string.IsNullOrWhiteSpace(options.RecordingDirectory))
            {
                Directory.CreateDirectory(options.RecordingDirectory);
                MicrophoneRecordingPath = options.IncludeMicrophone
                    ? Path.Combine(options.RecordingDirectory, "microphone.wav") : null;
                LoopbackRecordingPath = Path.Combine(options.RecordingDirectory, "loopback.wav");
                if (File.Exists(MicrophoneRecordingPath) || File.Exists(LoopbackRecordingPath))
                    throw new IOException("A recording track already exists in the requested directory.");
                _microphoneFilePackets = options.IncludeMicrophone ? CreatePacketChannel(options.PacketBufferCapacity) : null;
                _loopbackFilePackets = CreatePacketChannel(options.PacketBufferCapacity);
            }
            else
            {
                MicrophoneRecordingPath = null;
                LoopbackRecordingPath = null;
            }
            _microphoneStopped = NewStoppedSignal();
            _loopbackStopped = NewStoppedSignal();

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                _loopbackDevice = OpenDevice(enumerator, options.LoopbackDeviceId, DataFlow.Render, Role.Multimedia);
                if (AudioDeviceService.RequiresHandsFreeProfile(_loopbackDevice.ID))
                    throw new NotSupportedException("Choose the stereo headphone output instead of the Bluetooth hands-free output.");

                if (options.IncludeMicrophone)
                {
                    _microphoneDevice = OpenDevice(enumerator, options.MicrophoneDeviceId, DataFlow.Capture, Role.Multimedia);
                    if (AudioDeviceService.RequiresHandsFreeProfile(_microphoneDevice.ID))
                        throw new NotSupportedException("The Bluetooth headset microphone changes headphone playback to hands-free audio. Use a separate USB or built-in microphone.");
                    _microphone = new WasapiCapture(_microphoneDevice, options.UseEventSync, options.BufferMilliseconds);
                    _microphone.DataAvailable += OnMicrophoneData;
                    _microphone.RecordingStopped += OnMicrophoneStopped;
                }
                else
                {
                    _microphonePackets.Writer.TryComplete();
                    _microphoneStopped.TrySetResult();
                }
                // NAudio 2.4 exposes only the endpoint constructor for loopback capture.
                _loopback = new WasapiLoopbackCapture(_loopbackDevice);
                Interlocked.Exchange(ref _captureStartedTimestamp, Stopwatch.GetTimestamp());
                if (_microphoneFilePackets != null && _microphone != null)
                    _microphoneWriterTask = WriteWaveTrackAsync(_microphoneFilePackets.Reader,
                        MicrophoneRecordingPath!, _microphone.WaveFormat, CaptureStartedTimestamp);
                if (_loopbackFilePackets != null)
                    _loopbackWriterTask = WriteWaveTrackAsync(_loopbackFilePackets.Reader,
                        LoopbackRecordingPath!, _loopback.WaveFormat, CaptureStartedTimestamp);
                _loopback.DataAvailable += OnLoopbackData;
                _loopback.RecordingStopped += OnLoopbackStopped;

                _loopback.StartRecording();
                _microphone?.StartRecording();
            }
            catch
            {
                await StopAndReleaseCoreAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopAndReleaseCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopAndReleaseCoreAsync()
    {
        var microphone = _microphone;
        var loopback = _loopback;
        var microphoneStopped = _microphoneStopped;
        var loopbackStopped = _loopbackStopped;

        if (microphone != null)
            await RequestStopAsync(microphone, microphoneStopped).ConfigureAwait(false);
        if (loopback != null)
            await RequestStopAsync(loopback, loopbackStopped).ConfigureAwait(false);

        _microphonePackets?.Writer.TryComplete();
        _loopbackPackets?.Writer.TryComplete();
        _microphoneFilePackets?.Writer.TryComplete();
        _loopbackFilePackets?.Writer.TryComplete();

        await IgnoreWriterFailureAsync(_microphoneWriterTask).ConfigureAwait(false);
        await IgnoreWriterFailureAsync(_loopbackWriterTask).ConfigureAwait(false);

        if (microphone != null)
        {
            microphone.DataAvailable -= OnMicrophoneData;
            microphone.RecordingStopped -= OnMicrophoneStopped;
            microphone.Dispose();
        }
        if (loopback != null)
        {
            loopback.DataAvailable -= OnLoopbackData;
            loopback.RecordingStopped -= OnLoopbackStopped;
            loopback.Dispose();
        }

        _microphoneDevice?.Dispose();
        _loopbackDevice?.Dispose();
        _microphone = null;
        _loopback = null;
        _microphoneDevice = null;
        _loopbackDevice = null;
        _microphoneStopped = null;
        _loopbackStopped = null;
        _microphoneFilePackets = null;
        _loopbackFilePackets = null;
        _microphoneWriterTask = null;
        _loopbackWriterTask = null;
    }

    private static async Task RequestStopAsync(IWaveIn capture, TaskCompletionSource? stopped)
    {
        try
        {
            await Task.Run(capture.StopRecording).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            // WASAPI may already have stopped after endpoint removal.
        }

        if (stopped != null)
        {
            try { await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { /* Continue cleanup if a driver omits RecordingStopped. */ }
        }
    }

    private void OnMicrophoneData(object? sender, WaveInEventArgs args) =>
        Publish(AudioCaptureSource.Microphone, _microphonePackets, args, _microphone?.WaveFormat);

    private void OnLoopbackData(object? sender, WaveInEventArgs args) =>
        Publish(AudioCaptureSource.Loopback, _loopbackPackets, args, _loopback?.WaveFormat);

    private void Publish(AudioCaptureSource source, Channel<AudioCapturePacket>? channel, WaveInEventArgs args, WaveFormat? format)
    {
        if (channel == null || format == null || args.BytesRecorded <= 0)
            return;

        var bytes = new byte[args.BytesRecorded];
        Buffer.BlockCopy(args.Buffer, 0, bytes, 0, bytes.Length);
        var packet = new AudioCapturePacket(source, Stopwatch.GetTimestamp(), format.SampleRate,
            format.Channels, format.BitsPerSample, (int)format.Encoding, format.Encoding == WaveFormatEncoding.Extensible
                ? ((WaveFormatExtensible)format).SubFormat : Guid.Empty, bytes);

        var fileChannel = source == AudioCaptureSource.Microphone ? _microphoneFilePackets : _loopbackFilePackets;
        bool dropped = !channel.Writer.TryWrite(packet);
        if (fileChannel != null)
        {
            var filePacket = packet with { Data = (byte[])bytes.Clone() };
            if (!fileChannel.Writer.TryWrite(filePacket))
                Interlocked.CompareExchange(ref _recordingWriteError,
                    new IOException($"The {source} audio file buffer overflowed; part of the recording could not be saved."), null);
        }

        if (dropped)
        {
            if (source == AudioCaptureSource.Microphone)
                Interlocked.Increment(ref _microphoneDroppedPackets);
            else
                Interlocked.Increment(ref _loopbackDroppedPackets);
        }
    }

    private void OnMicrophoneStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception != null) Interlocked.CompareExchange(ref _captureError, args.Exception, null);
        _microphonePackets?.Writer.TryComplete(args.Exception);
        _microphoneFilePackets?.Writer.TryComplete(args.Exception);
        _microphoneStopped?.TrySetResult();
    }

    private void OnLoopbackStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception != null) Interlocked.CompareExchange(ref _captureError, args.Exception, null);
        _loopbackPackets?.Writer.TryComplete(args.Exception);
        _loopbackFilePackets?.Writer.TryComplete(args.Exception);
        _loopbackStopped?.TrySetResult();
    }

    private static Channel<AudioCapturePacket> CreatePacketChannel(int capacity) => Channel.CreateBounded<AudioCapturePacket>(
        new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private static async Task WriteWaveTrackAsync(ChannelReader<AudioCapturePacket> reader, string path, WaveFormat format,
        long startedTimestamp)
    {
        using var writer = new WaveFileWriter(path, format);
        long writtenFrames = 0;
        var silence = new byte[format.AverageBytesPerSecond];
        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 8) Array.Fill(silence, (byte)128);
        await foreach (var packet in reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (!MatchesFormat(packet, format))
                throw new InvalidOperationException("Audio format changed during capture; start a new recording for the new format.");
            var frames = packet.Data.Length / format.BlockAlign;
            var startFrame = Math.Max(0, (long)Math.Round((packet.TimestampTicks - startedTimestamp) *
                (double)format.SampleRate / Stopwatch.Frequency) - frames);
            // Loopback omits callbacks during silence. Preserve those gaps on the recording clock.
            if (startFrame - writtenFrames > format.SampleRate / 5)
            {
                var missingBytes = (startFrame - writtenFrames) * format.BlockAlign;
                while (missingBytes > 0)
                {
                    var count = (int)Math.Min(missingBytes, silence.Length);
                    writer.Write(silence, 0, count);
                    missingBytes -= count;
                }
                writtenFrames = startFrame;
            }
            writer.Write(packet.Data, 0, packet.Data.Length);
            writtenFrames += frames;
        }
    }

    private static bool MatchesFormat(AudioCapturePacket packet, WaveFormat format) =>
        packet.SampleRate == format.SampleRate && packet.Channels == format.Channels &&
        packet.BitsPerSample == format.BitsPerSample &&
        (packet.SubFormat == Guid.Empty || format is not WaveFormatExtensible extensible || packet.SubFormat == extensible.SubFormat);

    private async Task IgnoreWriterFailureAsync(Task? task)
    {
        if (task == null) return;
        try { await task.ConfigureAwait(false); }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _recordingWriteError, ex, null);
            System.Diagnostics.Debug.WriteLine($"AudioCaptureService WAV write failed: {ex.Message}");
        }
    }

    private static TaskCompletionSource NewStoppedSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static MMDevice OpenDevice(MMDeviceEnumerator enumerator, string? id, DataFlow flow, Role role)
    {
        if (string.IsNullOrWhiteSpace(id))
            return enumerator.GetDefaultAudioEndpoint(flow, role);
        return enumerator.GetDevice(id);
    }

    private static void ValidateOptions(AudioCaptureOptions options)
    {
        if (options.PacketBufferCapacity < 1 || options.PacketBufferCapacity > 16_384)
            throw new ArgumentOutOfRangeException(nameof(options.PacketBufferCapacity));
        if (options.BufferMilliseconds < 10 || options.BufferMilliseconds > 2_000)
            throw new ArgumentOutOfRangeException(nameof(options.BufferMilliseconds));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _lifecycleGate.Dispose();
    }
}
