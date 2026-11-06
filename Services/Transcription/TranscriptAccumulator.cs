using BetterWinTab.Models;
using BetterWinTab.Models.Transcription;

namespace BetterWinTab.Services.Transcription;

/// <summary>Replaces provisional turns by ID instead of appending repeated hypotheses.</summary>
public sealed class TranscriptAccumulator
{
    private readonly Dictionary<string, AudioTranscriptSegment> _confirmed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AudioTranscriptSegment> _partials = new(StringComparer.Ordinal);

    public void Apply(StreamingTranscriptEvent update)
    {
        if (update.Type == "partial")
        {
            if (string.IsNullOrWhiteSpace(update.Text)) _partials.Remove(update.Source);
            else _partials[update.Source] = new AudioTranscriptSegment
            {
                Id = update.SegmentId, Source = update.Source, StartSeconds = update.StartSeconds,
                DurationSeconds = update.DurationSeconds, Text = update.Text,
                Speaker = update.Source == "microphone" ? "You" : "Identifying speaker…",
                SpeakerId = update.Source == "microphone" ? "you" : null,
                IsIncomplete = true, IsSpeakerUncertain = update.Source != "microphone"
            };
        }
        else if (update.Type == "final")
        {
            if (_partials.TryGetValue(update.Source, out var partial) && partial.Id == update.SegmentId)
                _partials.Remove(update.Source);
            foreach (var segment in update.Segments ?? Array.Empty<StreamingTranscriptSegment>())
            {
                if (string.IsNullOrWhiteSpace(segment.Text)) continue;
                _confirmed[segment.Id] = new AudioTranscriptSegment
                {
                    Id = segment.Id, Source = segment.Source, Text = segment.Text,
                    StartSeconds = segment.StartSeconds, DurationSeconds = segment.DurationSeconds,
                    SpeakerId = segment.SpeakerId, Speaker = segment.Speaker,
                    IsSpeakerUncertain = segment.IsSpeakerUncertain
                };
            }
        }
    }

    public AudioTranscriptSegment[] Snapshot(bool includePartials = false) => _confirmed.Values
        .Concat(includePartials ? _partials.Values : Enumerable.Empty<AudioTranscriptSegment>())
        .OrderBy(segment => segment.StartSeconds).ThenBy(segment => segment.Source, StringComparer.Ordinal).ToArray();
}
