using BetterWinTab.Models.Transcription;

namespace BetterWinTab.AsrWorker;

internal sealed record SpeakerMatch(string Id, string Name, bool Uncertain);
internal sealed record VoiceTurn(double Start, double End, string? SpeakerId, string? Speaker, bool Uncertain);

internal sealed class SessionSpeakers
{
    private readonly List<float[]> _centroids = [];

    public SpeakerMatch Match(float[] embedding)
    {
        var norm = Math.Sqrt(embedding.Sum(value => (double)value * value));
        if (norm <= 1e-8 || embedding.Any(value => !float.IsFinite(value)))
            return new SpeakerMatch("unknown", "Speaker pending", true);
        var normalized = embedding.Select(value => (float)(value / norm)).ToArray();
        var scores = _centroids.Select((centroid, index) => (Index: index,
            Score: centroid.Zip(normalized, (left, right) => (double)left * right).Sum()))
            .OrderByDescending(item => item.Score).ToArray();
        var best = scores.FirstOrDefault();
        var uncertain = scores.Length > 1 && best.Score - scores[1].Score < 0.10;
        int selected;
        if (scores.Length == 0 || best.Score < 0.55)
        {
            selected = _centroids.Count;
            _centroids.Add(normalized);
        }
        else
        {
            selected = best.Index;
            if (!uncertain && best.Score >= 0.7)
            {
                var centroid = _centroids[selected];
                for (var index = 0; index < centroid.Length; index++)
                    centroid[index] = 0.9f * centroid[index] + 0.1f * normalized[index];
                var length = Math.Sqrt(centroid.Sum(value => (double)value * value));
                for (var index = 0; index < centroid.Length; index++) centroid[index] /= (float)length;
            }
        }
        return new SpeakerMatch($"speaker-{selected + 1}", $"Speaker {selected + 1}", uncertain);
    }
}

internal static class TokenAlignment
{
    public static StreamingTranscriptSegment[] Align(string parent, string source, double start, double duration,
        string text, string[] tokens, float[] timestamps, IReadOnlyList<VoiceTurn> turns)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (tokens.Length == 0 || timestamps.Length != tokens.Length)
            return [new StreamingTranscriptSegment(parent + ":0", source, start, duration, text.Trim(),
                IsSpeakerUncertain: true)];

        var words = new List<(string Text, double Start, double End)>();
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index].Replace('▁', ' ');
            if (token.StartsWith('<') && token.EndsWith('>')) continue;
            var tokenStart = Math.Clamp(timestamps[index], 0, duration);
            var tokenEnd = Math.Min(duration, Math.Max(tokenStart + 0.08,
                index + 1 < timestamps.Length ? Math.Min(timestamps[index + 1], tokenStart + 0.4) : tokenStart + 0.24));
            if (words.Count == 0 || char.IsWhiteSpace(token.FirstOrDefault()))
                words.Add((token, tokenStart, tokenEnd));
            else
            {
                var previous = words[^1];
                words[^1] = (previous.Text + token, previous.Start, tokenEnd);
            }
        }

        var output = new List<StreamingTranscriptSegment>();
        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word.Text)) continue;
            var candidates = turns.Select(turn => (Turn: turn,
                Overlap: Math.Max(0, Math.Min(word.End, turn.End) - Math.Max(word.Start, turn.Start))))
                .Where(item => item.Overlap > 0).OrderByDescending(item => item.Overlap).ToArray();
            var assigned = candidates.FirstOrDefault().Turn;
            var uncertain = assigned == null || assigned.Uncertain || candidates.Any(candidate =>
                candidate.Turn.SpeakerId != assigned.SpeakerId && candidate.Overlap > 0.02);
            if (output.Count > 0 && output[^1].SpeakerId == assigned?.SpeakerId &&
                output[^1].IsSpeakerUncertain == uncertain)
            {
                var previous = output[^1];
                output[^1] = previous with { Text = previous.Text + word.Text,
                    DurationSeconds = start + word.End - previous.StartSeconds };
            }
            else
                output.Add(new StreamingTranscriptSegment($"{parent}:{output.Count}", source,
                    start + word.Start, Math.Max(0.01, word.End - word.Start), word.Text.TrimStart(),
                    assigned?.SpeakerId, assigned?.Speaker, uncertain));
        }
        if (output.Count == 0) return [new StreamingTranscriptSegment(parent + ":0", source, start,
            duration, text.Trim(), IsSpeakerUncertain: true)];
        return output.Select(segment => segment with { Text = segment.Text.Trim() }).ToArray();
    }
}
