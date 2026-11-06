using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;
using BetterWinTab.AsrWorker;

if (args.Length > 0 && args[0] == "--stream")
    return StreamingWorker.Run(args);

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: AsrWorker <model-directory> <mono-wave-file>");
    return 2;
}

try
{
    var modelDirectory = Path.GetFullPath(args[0]);
    var wavePath = Path.GetFullPath(args[1]);
    var config = new OfflineRecognizerConfig();
    config.ModelConfig.Moonshine.Encoder = Path.Combine(modelDirectory, "encoder_model.ort");
    config.ModelConfig.Moonshine.MergedDecoder = Path.Combine(modelDirectory, "decoder_model_merged.ort");
    config.ModelConfig.Tokens = Path.Combine(modelDirectory, "tokens.txt");
    config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);
    config.ModelConfig.Debug = 0;

    using var recognizer = new OfflineRecognizer(config);
    using var wave = new AudioFileReader(wavePath);
    // Resample continuously: sherpa creates a fresh resampler on each AcceptWaveform
    // call at a different rate, losing filter state at every input buffer boundary.
    const int sampleRate = 16000;
    ISampleProvider samples = wave.WaveFormat.SampleRate == sampleRate
        ? wave : new WdlResamplingSampleProvider(wave, sampleRate);
    var channelCount = wave.WaveFormat.Channels;
    var input = new float[8192 * channelCount];
    var mono = new float[sampleRate * 8];
    var monoCount = 0;
    var segments = new List<WorkerSegment>();
    long samplesRead = 0;
    int count;
    while ((count = samples.Read(input, 0, input.Length)) > 0)
    {
        var frames = count / channelCount;
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channelCount; channel++)
                sum += input[frame * channelCount + channel];
            mono[monoCount++] = sum / channelCount;
            samplesRead++;
            if (monoCount == mono.Length)
            {
                DecodeChunk(monoCount);
                monoCount = 0;
            }
        }
    }
    if (monoCount > 0) DecodeChunk(monoCount);

    var response = new WorkerResponse(string.Join(" ", segments.Select(segment => segment.Text)),
        samplesRead / (double)sampleRate, segments);
    Console.Out.WriteLine("BWTA_RESULT:" + JsonSerializer.Serialize(response));
    return 0;

    void DecodeChunk(int length)
    {
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(sampleRate, mono.AsSpan(0, length).ToArray());
        recognizer.Decode(stream);
        var text = stream.Result.Text.Trim();
        if (text.Length > 0)
            segments.Add(new WorkerSegment(text, (samplesRead - length) / (double)sampleRate,
                length / (double)sampleRate));
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

internal sealed record WorkerResponse(string Text, double DurationSeconds, IReadOnlyList<WorkerSegment> Segments);
internal sealed record WorkerSegment(string Text, double StartSeconds, double DurationSeconds);
