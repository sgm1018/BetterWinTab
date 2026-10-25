namespace BetterWinTab.Models.Transcription;

public enum TranscriptMode
{
    Offline,
    SimulatedStreaming,
    Streaming
}

public enum LanguageSelectionMode
{
    Automatic,
    Manual
}

public sealed record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    string? ParticipantId = null,
    float? Confidence = null);

public sealed record TranscriptParticipant(string Id, string? DisplayName = null);

public sealed record TranscriptDocument(
    IReadOnlyList<TranscriptSegment> Segments,
    IReadOnlyList<TranscriptParticipant> Participants,
    string? DetectedLanguage,
    string ModelId,
    TranscriptMode Mode,
    DateTimeOffset CreatedAtUtc)
{
    public static TranscriptDocument Empty(string modelId, TranscriptMode mode) =>
        new(Array.Empty<TranscriptSegment>(), Array.Empty<TranscriptParticipant>(), null, modelId, mode, DateTimeOffset.UtcNow);
}

public sealed record TranscriptionOptions(
    LanguageSelectionMode LanguageMode = LanguageSelectionMode.Automatic,
    string? Locale = null);

public sealed record TranscriptionProgress(TimeSpan AudioProcessed, string? PartialText = null);
