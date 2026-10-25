using System.Text.Json;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>Persists recording metadata and transcripts under %AppData%\BetterWinTab\recordings.</summary>
public sealed class AudioRecordingStoreService
{
    private readonly string _directory;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<AudioRecordingMetadata> _recordings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AudioRecordingStoreService(string? storageDirectory = null)
    {
        _directory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetterWinTab", "recordings");
        _indexPath = Path.Combine(_directory, "recordings.json");
        _recordings = LoadIndex();
    }

    public async Task<IReadOnlyList<AudioRecordingMetadata>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _recordings.OrderByDescending(item => item.StartedAtUtc).Select(Clone).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<AudioRecordingMetadata?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var item = _recordings.FirstOrDefault(recording => recording.Id == id);
            return item == null ? null : Clone(item);
        }
        finally { _gate.Release(); }
    }

    public async Task<AudioRecordingMetadata> CreateAsync(string? microphoneDeviceId, string? loopbackDeviceId,
        string? audioFilePath = null, CancellationToken cancellationToken = default,
        string? microphoneAudioFilePath = null, string? loopbackAudioFilePath = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var item = new AudioRecordingMetadata
            {
                MicrophoneDeviceId = microphoneDeviceId,
                LoopbackDeviceId = loopbackDeviceId,
                AudioFilePath = audioFilePath,
                MicrophoneAudioFilePath = microphoneAudioFilePath,
                LoopbackAudioFilePath = loopbackAudioFilePath
            };
            _recordings.Add(item);
            await SaveIndexAsync(cancellationToken).ConfigureAwait(false);
            return Clone(item);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> CompleteAsync(string id, DateTime? endedAtUtc = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var item = _recordings.FirstOrDefault(recording => recording.Id == id);
            if (item == null) return false;
            item.EndedAtUtc = endedAtUtc ?? DateTime.UtcNow;
            await SaveIndexAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> SetTranscriptAsync(string id, IEnumerable<AudioTranscriptSegment> segments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var replacement = segments.Select(Clone).ToList();
        if (replacement.Any(segment => segment.StartSeconds < 0 || segment.DurationSeconds < 0 ||
                                       !double.IsFinite(segment.StartSeconds) || !double.IsFinite(segment.DurationSeconds)))
            throw new ArgumentOutOfRangeException(nameof(segments), "Transcript times must be finite and non-negative.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var item = _recordings.FirstOrDefault(recording => recording.Id == id);
            if (item == null) return false;
            item.Transcript = replacement;
            await SaveIndexAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = _recordings.RemoveAll(recording => recording.Id == id) > 0;
            if (removed) await SaveIndexAsync(cancellationToken).ConfigureAwait(false);
            return removed;
        }
        finally { _gate.Release(); }
    }

    private List<AudioRecordingMetadata> LoadIndex()
    {
        try
        {
            if (!File.Exists(_indexPath)) return new List<AudioRecordingMetadata>();
            return JsonSerializer.Deserialize<List<AudioRecordingMetadata>>(File.ReadAllText(_indexPath), JsonOptions)?
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)).ToList() ?? new List<AudioRecordingMetadata>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecordingStoreService.LoadIndex: {ex.Message}");
            return new List<AudioRecordingMetadata>();
        }
    }

    private async Task SaveIndexAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = _indexPath + ".tmp";
        var json = JsonSerializer.Serialize(_recordings, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, _indexPath, overwrite: true);
    }

    private static AudioRecordingMetadata Clone(AudioRecordingMetadata item) => new()
    {
        Id = item.Id,
        StartedAtUtc = item.StartedAtUtc,
        EndedAtUtc = item.EndedAtUtc,
        MicrophoneDeviceId = item.MicrophoneDeviceId,
        LoopbackDeviceId = item.LoopbackDeviceId,
        AudioFilePath = item.AudioFilePath,
        MicrophoneAudioFilePath = item.MicrophoneAudioFilePath,
        LoopbackAudioFilePath = item.LoopbackAudioFilePath,
        Transcript = item.Transcript.Select(Clone).ToList()
    };

    private static AudioTranscriptSegment Clone(AudioTranscriptSegment item) => new()
    {
        StartSeconds = item.StartSeconds,
        DurationSeconds = item.DurationSeconds,
        Text = item.Text,
        Speaker = item.Speaker,
        Confidence = item.Confidence
    };
}
