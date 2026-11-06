using BetterWinTab.Services.Transcription;
using NAudio.Wave;
using BetterWinTab.Models;
using BetterWinTab.Models.Transcription;
using BetterWinTab.Services;

var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
var input = new byte[48000 * 2 * sizeof(float)];
for (var i = 0; i < 48000; i++)
{
    var value = (float)(0.25 * Math.Sin(i * 2 * Math.PI * 440 / 48000));
    BitConverter.GetBytes(value).CopyTo(input, i * 8);
    BitConverter.GetBytes(value).CopyTo(input, i * 8 + 4);
}
var whole = new StreamingPcmConverter(format).Convert(input);
var splitConverter = new StreamingPcmConverter(format);
var split = new List<byte>();
for (var offset = 0; offset < input.Length; offset += 3840)
    split.AddRange(splitConverter.Convert(input.AsSpan(offset, Math.Min(3840, input.Length - offset)).ToArray()));
if (Math.Abs(whole.Length - 32000) > 128) throw new Exception("One second did not normalize to 16 kHz mono PCM16.");
if (!whole.SequenceEqual(split)) throw new Exception("Resampling changed at packet boundaries.");
var pcm = new byte[] { 0, 0, 255, 127, 0, 128, 0, 0 };
if (!new StreamingPcmConverter(new WaveFormat(16000, 16, 1)).Convert(pcm).SequenceEqual(pcm))
    throw new Exception("Native PCM16 was changed.");
Console.WriteLine("PASS: Continuous stereo float normalization and native PCM16 preservation.");

var state = new TranscriptAccumulator();
state.Apply(new("partial", "loopback", "loopback-0", "Hello", 0, 1));
state.Apply(new("partial", "loopback", "loopback-0", "Hello there", 0, 2));
if (state.Snapshot(true).Length != 1 || state.Snapshot(true)[0].Text != "Hello there")
    throw new Exception("Partial updates duplicated text.");
state.Apply(new("final", "loopback", "loopback-0", Segments:
    [new("loopback-0-0", "loopback", 0, 1, "Hello", "speaker-1", "Speaker 1"),
     new("loopback-0-1", "loopback", 1, 1, "there", "speaker-2", "Speaker 2")]));
if (state.Snapshot(true).Length != 2 || state.Snapshot(true).Any(item => item.IsIncomplete))
    throw new Exception("Final speaker split did not replace the provisional turn.");
state.Apply(new("partial", "microphone", "microphone-0", "Test", 2, 1));
if (state.Snapshot(false).Length != 2 || state.Snapshot(true).Count(item => item.IsIncomplete) != 1)
    throw new Exception("Recoverable partial was lost or persisted as confirmed.");
Console.WriteLine("PASS: Partial replacement, speaker splits and incomplete result recovery.");

var storage = Path.Combine(Path.GetTempPath(), "betterwintab-transcript-test-" + Guid.NewGuid().ToString("N"));
var recordings = new AudioRecordingStoreService(storage);
var metadata = await recordings.CreateAsync(null, "output");
if (!await recordings.TryBeginTranscriptionAsync(metadata.Id) || await recordings.TryBeginTranscriptionAsync(metadata.Id))
    throw new Exception("A recording must acquire its transcription exactly once.");
await recordings.SetTranscriptAsync(metadata.Id, state.Snapshot(true));
var reloaded = await new AudioRecordingStoreService(storage).GetAsync(metadata.Id);
if (reloaded?.Transcript[0].SpeakerId != "speaker-1" || reloaded.Transcript[^1].IsIncomplete != true)
    throw new Exception("Streaming metadata was lost while saving recordings.");
await File.WriteAllTextAsync(Path.Combine(storage, "recordings.json"),
    "[{\"id\":\"legacy\",\"transcript\":[{\"startSeconds\":0,\"durationSeconds\":1,\"text\":\"old text\",\"speaker\":\"Call audio\"}]}]");
var legacy = await new AudioRecordingStoreService(storage).GetAsync("legacy");
if (legacy?.Transcript[0].Text != "old text" || legacy.Transcript[0].Speaker != "Call audio")
    throw new Exception("Legacy recordings could not be loaded.");
if (await new AudioRecordingStoreService(storage).TryBeginTranscriptionAsync("legacy"))
    throw new Exception("An existing legacy transcript must not be generated again.");
Console.WriteLine("PASS: Speaker metadata round-trip and legacy transcript compatibility.");
var corrected = TranscriptTextEditor.ApplyEdit(state.Snapshot(), "Speaker 1: Hallo" + Environment.NewLine + Environment.NewLine + "Speaker 2: there");
if (corrected[0].SpeakerId != "speaker-1" || corrected[0].Text != "Hallo" || corrected[1].StartSeconds != 1)
    throw new Exception("A text correction destroyed speaker attribution or timestamps.");
var manual = TranscriptTextEditor.ApplyEdit(corrected, "Plain replacement");
if (TranscriptTextEditor.Format(manual) != "Plain replacement" ||
    TranscriptTextEditor.Format(TranscriptTextEditor.ApplyEdit(manual, "Plain replacement edited")) != "Plain replacement edited")
    throw new Exception("Manual editing added synthetic speaker prefixes.");

if (args.Length == 2)
{
    var nativeState = new TranscriptAccumulator();
    var partials = 0;
    var finals = 0;
    await using var session = new SherpaStreamingSession(args[0]);
    await session.StartAsync(new AsrModelStore(), "es-ES");
    var consume = Task.Run(async () =>
    {
        await foreach (var update in session.ReadEventsAsync())
        {
            if (update.Type == "partial" && update.Text.Length > 0) Interlocked.Increment(ref partials);
            if (update.Type == "final") Interlocked.Increment(ref finals);
            nativeState.Apply(update);
        }
    });
    using var wave = new WaveFileReader(args[1]);
    var converter = new StreamingPcmConverter(wave.WaveFormat);
    var buffer = new byte[wave.WaveFormat.AverageBytesPerSecond / 10 / wave.WaveFormat.BlockAlign * wave.WaveFormat.BlockAlign];
    var time = 0d;
    int read;
    while ((read = wave.Read(buffer, 0, buffer.Length)) > 0)
    {
        var audio = converter.Convert(buffer.AsSpan(0, read).ToArray());
        await session.SendAudioAsync("loopback", audio, time);
        time += audio.Length / 32000d;
    }
    await session.CompleteAsync();
    await consume;
    if (partials == 0 || finals == 0 || nativeState.Snapshot().Length == 0 || nativeState.Snapshot(true).Any(item => item.IsIncomplete))
        throw new Exception("The app streaming session failed to deliver and finalize provisional speech.");
    Console.WriteLine($"PASS: Native app session delivered {partials} partials, {finals} final event(s) and {nativeState.Snapshot().Length} speaker segment(s).");
    await using var aborted = new SherpaStreamingSession(args[0]);
    await aborted.StartAsync(new AsrModelStore(), "es-ES");
    aborted.Abort();
    var rejected = false;
    try { await aborted.SendAudioAsync("loopback", new byte[3200], 0).WaitAsync(TimeSpan.FromSeconds(2)); }
    catch (OperationCanceledException) { rejected = true; }
    if (!rejected) throw new Exception("Failed consumers must be able to abort audio feeders promptly.");
    Console.WriteLine("PASS: Aborting a speech session cancels audio feeders promptly.");
}
