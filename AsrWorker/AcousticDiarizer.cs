using SherpaOnnx;

namespace BetterWinTab.AsrWorker;

internal sealed class AcousticDiarizer : IDisposable
{
    private const int SampleRate = 16000;
    private readonly OfflineSpeakerDiarization _diarizer;
    private readonly SpeakerEmbeddingExtractor _extractor;
    private readonly SessionSpeakers _speakers = new();

    public AcousticDiarizer(string segmentation, string embedding)
    {
        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = segmentation;
        config.Segmentation.Pyannote.WindowShiftRatio = 0.25f;
        config.Segmentation.NumThreads = 2;
        config.Embedding.Model = embedding;
        config.Embedding.NumThreads = 2;
        config.Clustering.NumClusters = -1;
        config.Clustering.Threshold = 0.5f;
        config.MinDurationOn = 0.2f;
        config.MinDurationOff = 0.3f;
        _diarizer = new OfflineSpeakerDiarization(config);
        try { _extractor = new SpeakerEmbeddingExtractor(config.Embedding); }
        catch { _diarizer.Dispose(); throw; }
        if (_diarizer.SampleRate != SampleRate)
        {
            _extractor.Dispose();
            _diarizer.Dispose();
            throw new InvalidOperationException("Diarization model must use 16 kHz audio.");
        }
    }

    public VoiceTurn[] Process(float[] audio, float[] precedingAudio)
    {
        var contextLength = precedingAudio.Length;
        var window = new float[Math.Max(SampleRate * 10, contextLength + audio.Length)];
        precedingAudio.CopyTo(window, 0);
        audio.CopyTo(window, contextLength);
        var availableSeconds = (contextLength + audio.Length) / (double)SampleRate;
        var turns = _diarizer.Process(window).Where(turn => turn.Start < availableSeconds && turn.End > 0).ToArray();
        var identities = new Dictionary<int, SpeakerMatch>();
        foreach (var group in turns.GroupBy(turn => turn.Speaker))
        {
            // Embeddings use only isolated speech; overlapping voices contaminate speaker centroids.
            var isolated = new List<float>();
            foreach (var turn in group)
            {
                var start = Math.Max(0, (int)(turn.Start * SampleRate));
                var end = Math.Min(contextLength + audio.Length, (int)(turn.End * SampleRate));
                for (var index = start; index < end; index++)
                {
                    var seconds = index / (double)SampleRate;
                    if (!turns.Any(other => other.Speaker != turn.Speaker && other.Start <= seconds && other.End > seconds))
                        isolated.Add(window[index]);
                }
            }
            if (isolated.Count < SampleRate * 0.8)
            {
                identities[group.Key] = new SpeakerMatch("unknown", "Speaker pending", true);
                continue;
            }
            using var stream = _extractor.CreateStream();
            stream.AcceptWaveform(SampleRate, isolated.ToArray());
            stream.InputFinished();
            identities[group.Key] = _extractor.IsReady(stream)
                ? _speakers.Match(_extractor.Compute(stream))
                : new SpeakerMatch("unknown", "Speaker pending", true);
        }
        var offset = contextLength / (double)SampleRate;
        return turns.Where(turn => turn.End > offset).Select(turn =>
        {
            var identity = identities[turn.Speaker];
            return new VoiceTurn(Math.Max(0, turn.Start - offset), Math.Min(audio.Length / (double)SampleRate,
                turn.End - offset), identity.Id == "unknown" ? null : identity.Id, identity.Name, identity.Uncertain);
        }).Where(turn => turn.End > turn.Start).ToArray();
    }

    public void Dispose()
    {
        _extractor.Dispose();
        _diarizer.Dispose();
    }
}
