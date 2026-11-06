using BetterWinTab.Models;

namespace BetterWinTab.Services.Transcription;

public static class TranscriptTextEditor
{
    public static string Format(IEnumerable<AudioTranscriptSegment> segments) => string.Join(
        Environment.NewLine + Environment.NewLine, segments.Select(segment =>
            segment.Source == "manual" || string.IsNullOrWhiteSpace(segment.Speaker)
                ? segment.Text : $"{segment.Speaker}: {segment.Text}"));

    public static AudioTranscriptSegment[] ApplyEdit(IReadOnlyList<AudioTranscriptSegment> existing, string text)
    {
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.None);
        if (paragraphs.Length == existing.Count && existing.All(segment => segment.Source != "manual" && !string.IsNullOrEmpty(segment.Speaker)) &&
            paragraphs.Select((paragraph, index) => paragraph.StartsWith(existing[index].Speaker + ": ", StringComparison.Ordinal)).All(matches => matches))
        {
            return existing.Select((segment, index) => new AudioTranscriptSegment
            {
                Id = segment.Id, Source = segment.Source, SpeakerId = segment.SpeakerId, Speaker = segment.Speaker,
                StartSeconds = segment.StartSeconds, DurationSeconds = segment.DurationSeconds,
                IsIncomplete = segment.IsIncomplete, IsSpeakerUncertain = segment.IsSpeakerUncertain,
                Text = paragraphs[index][(segment.Speaker!.Length + 2)..].Trim()
            }).ToArray();
        }
        return [new AudioTranscriptSegment
        {
            Id = Guid.NewGuid().ToString("N"), Source = "manual", Text = text.Trim(),
            DurationSeconds = existing.Select(segment => segment.StartSeconds + segment.DurationSeconds).DefaultIfEmpty(0).Max()
        }];
    }
}
