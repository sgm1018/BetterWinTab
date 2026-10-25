using System.Text.Json;
using NAudio.Wave;
using SherpaOnnx;

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
    config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    config.ModelConfig.Debug = 0;

    using var recognizer = new OfflineRecognizer(config);
    using var wave = new AudioFileReader(wavePath);
    using var stream = recognizer.CreateStream();
    var channelCount = wave.WaveFormat.Channels;
    var input = new float[8192 * channelCount];
    var mono = new float[8192];
    long samplesRead = 0;
    int count;
    while ((count = wave.Read(input, 0, input.Length)) > 0)
    {
        var frames = count / channelCount;
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channelCount; channel++)
                sum += input[frame * channelCount + channel];
            mono[frame] = sum / channelCount;
        }
        stream.AcceptWaveform(wave.WaveFormat.SampleRate, mono.AsSpan(0, frames).ToArray());
        samplesRead += frames;
    }
    recognizer.Decode(stream);

    var response = new WorkerResponse(stream.Result.Text, samplesRead / (double)wave.WaveFormat.SampleRate);
    Console.Out.WriteLine("BWTA_RESULT:" + JsonSerializer.Serialize(response));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

internal sealed record WorkerResponse(string Text, double DurationSeconds);
