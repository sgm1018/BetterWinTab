namespace BetterWinTab.Models;

public enum AudioCaptureSource
{
    Microphone,
    Loopback
}

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    AudioCaptureSource Source,
    bool IsDefault,
    bool RequiresHandsFreeProfile = false);

public sealed class AudioCaptureOptions
{
    /// <summary>Open the microphone only when explicitly enabled by the caller.</summary>
    public bool IncludeMicrophone { get; init; }

    /// <summary>WASAPI endpoint ID. Null selects the Windows communications default.</summary>
    public string? MicrophoneDeviceId { get; init; }

    /// <summary>Render endpoint ID. Null selects the Windows multimedia default.</summary>
    public string? LoopbackDeviceId { get; init; }

    /// <summary>Maximum queued packets per stream. Full queues drop new packets and increment a counter.</summary>
    public int PacketBufferCapacity { get; init; } = 128;

    /// <summary>Optional directory where separate microphone.wav and loopback.wav tracks are written.</summary>
    public string? RecordingDirectory { get; init; }

    /// <summary>WASAPI shared-mode event synchronization.</summary>
    public bool UseEventSync { get; init; } = true;

    /// <summary>Requested WASAPI endpoint buffer duration in milliseconds.</summary>
    public int BufferMilliseconds { get; init; } = 100;
}

/// <summary>A copied WASAPI packet. TimestampTicks use Stopwatch.GetTimestamp() units.</summary>
public sealed record AudioCapturePacket(
    AudioCaptureSource Source,
    long TimestampTicks,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    int Encoding,
    Guid SubFormat,
    byte[] Data);

public sealed class AudioRecordingMetadata : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public string? LoopbackDeviceId { get; set; }
    public string? AudioFilePath { get; set; }
    public string? MicrophoneAudioFilePath { get; set; }
    public string? LoopbackAudioFilePath { get; set; }
    public bool TranscriptionStarted { get; set; }
    private List<AudioTranscriptSegment> _transcript = new();
    public List<AudioTranscriptSegment> Transcript
    {
        get => _transcript;
        set
        {
            _transcript = value ?? new();
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Transcript)));
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class AudioTranscriptSegment
{
    public string? Id { get; set; }
    public string? Source { get; set; }
    public string? SpeakerId { get; set; }
    public bool IsIncomplete { get; set; }
    public bool IsSpeakerUncertain { get; set; }
    public double StartSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Speaker { get; set; }
    public float? Confidence { get; set; }
}
