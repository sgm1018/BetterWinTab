namespace BetterWinTab.Models.Transcription;

// Audio is little-endian mono PCM16 at 16 kHz. StartSeconds is on the recording clock.
public sealed record StreamingAudioMessage(string Type, string Source = "", string? Audio = null, double StartSeconds = 0);

public sealed record StreamingTranscriptSegment(string Id, string Source, double StartSeconds,
    double DurationSeconds, string Text, string? SpeakerId = null, string? Speaker = null,
    bool IsSpeakerUncertain = false);

public sealed record StreamingTranscriptEvent(string Type, string Source = "", string SegmentId = "",
    string Text = "", double StartSeconds = 0, double DurationSeconds = 0,
    StreamingTranscriptSegment[]? Segments = null, string? Message = null,
    double AudioSeconds = 0, double ProcessingSeconds = 0);
