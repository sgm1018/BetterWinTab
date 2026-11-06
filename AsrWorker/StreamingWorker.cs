using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BetterWinTab.Models.Transcription;
using SherpaOnnx;

namespace BetterWinTab.AsrWorker;

internal static class StreamingWorker
{
    internal const int SampleRate = 16000;
    private const int MaxMessageCharacters = SampleRate * 2 * 30 * 4 / 3 + 4096;

    public static int Run(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length != 5)
        {
            Emit(new StreamingTranscriptEvent("error", Message:
                "Usage: AsrWorker --stream <asr-directory> <segmentation.onnx> <embedding.onnx> <locale>"));
            return 2;
        }
        try
        {
            var config = new OnlineRecognizerConfig();
            config.FeatConfig.SampleRate = SampleRate;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Transducer.Encoder = RequireFile(Path.Combine(args[1], "encoder.int8.onnx"));
            config.ModelConfig.Transducer.Decoder = RequireFile(Path.Combine(args[1], "decoder.int8.onnx"));
            config.ModelConfig.Transducer.Joiner = RequireFile(Path.Combine(args[1], "joiner.int8.onnx"));
            config.ModelConfig.Tokens = RequireFile(Path.Combine(args[1], "tokens.txt"));
            config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            config.EnableEndpoint = 1;
            config.Rule1MinTrailingSilence = 2.4f;
            config.Rule2MinTrailingSilence = 0.9f;
            config.Rule3MinUtteranceLength = 12;
            using var recognizer = new OnlineRecognizer(config);
            using var diarizer = new AcousticDiarizer(RequireFile(args[2]), RequireFile(args[3]));
            var tracks = new Dictionary<string, Track>();
            var processing = new Stopwatch();
            try
            {
                Emit(new StreamingTranscriptEvent("ready"));
                string? line;
                while ((line = Console.ReadLine()) != null)
                {
                    if (line.Length > MaxMessageCharacters) throw new InvalidDataException("Audio message exceeds the 30 second packet limit.");
                    var message = JsonSerializer.Deserialize<StreamingAudioMessage>(line)
                        ?? throw new InvalidDataException("Empty streaming message.");
                    if (message.Type == "finish") break;
                    if (message.Type is not ("audio" or "idle") || message.Source is not ("microphone" or "loopback") ||
                        !double.IsFinite(message.StartSeconds) || message.StartSeconds < 0)
                        throw new InvalidDataException("Invalid streaming audio message.");
                    if (message.Type == "idle")
                    {
                        if (tracks.TryGetValue(message.Source, out var idleTrack))
                        {
                            processing.Start();
                            idleTrack.FinishIfIdle(message.StartSeconds);
                            processing.Stop();
                        }
                        continue;
                    }
                    if (message.Audio == null) throw new InvalidDataException("Audio packet has no PCM payload.");
                    var bytes = Convert.FromBase64String(message.Audio);
                    if (bytes.Length > SampleRate * 2 * 30) throw new InvalidDataException("Audio packet exceeds 30 seconds.");
                    if (bytes.Length % 2 != 0) throw new InvalidDataException("PCM16 audio contains an incomplete sample.");
                    var samples = new float[bytes.Length / 2];
                    for (var index = 0; index < samples.Length; index++)
                        samples[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(index * 2, 2)) / 32768f;
                    if (!tracks.TryGetValue(message.Source, out var track))
                    {
                        track = new Track(message.Source, recognizer, diarizer, args[4]);
                        tracks.Add(message.Source, track);
                    }
                    processing.Start();
                    track.Accept(samples, message.StartSeconds);
                    processing.Stop();
                }
                processing.Start();
                foreach (var track in tracks.Values) track.Finish();
                processing.Stop();
                Emit(new StreamingTranscriptEvent("completed", AudioSeconds: tracks.Values.Sum(track => track.AudioSeconds),
                    ProcessingSeconds: processing.Elapsed.TotalSeconds));
            }
            finally { foreach (var track in tracks.Values) track.Dispose(); }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Emit(new StreamingTranscriptEvent("error", Message: exception.Message));
            return 1;
        }
    }

    private static string RequireFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
            throw new FileNotFoundException("Required speech model is missing or empty.", fullPath);
        return fullPath;
    }

    private static void Emit(StreamingTranscriptEvent update)
    {
        Console.Out.WriteLine("BWTA_EVENT:" + JsonSerializer.Serialize(update));
        Console.Out.Flush();
    }

    private sealed class Track(string source, OnlineRecognizer recognizer, AcousticDiarizer diarizer, string locale) : IDisposable
    {
        private readonly List<float> _audio = [];
        private OnlineStream? _stream;
        private float[] _context = [];
        private string _lastText = "";
        private string _segmentId = "";
        private double _start;
        private double _expectedStart;
        private bool _hasTimeline;
        private int _sequence;
        public double AudioSeconds { get; private set; }

        public void FinishIfIdle(double recordingTime)
        {
            if (_hasTimeline && recordingTime >= _expectedStart + 0.9) Finish();
        }

        public void Accept(float[] samples, double start)
        {
            if (_hasTimeline && start > _expectedStart + 0.025)
            {
                Finish();
                _context = [];
            }
            // A repeated packet must not decode the same timeline twice.
            var skip = _hasTimeline ? Math.Clamp((int)Math.Round((_expectedStart - start) * SampleRate), 0, samples.Length) : 0;
            var offset = skip;
            while (offset < samples.Length)
            {
                if (_stream == null)
                {
                    _stream = recognizer.CreateStream();
                    _stream.SetOption("language", locale);
                    _start = start + offset / (double)SampleRate;
                    _segmentId = $"{source}-{++_sequence}";
                }
                var count = Math.Min(Math.Min(SampleRate / 10, samples.Length - offset), SampleRate * 12 - _audio.Count);
                var chunk = samples.AsSpan(offset, count).ToArray();
                _audio.AddRange(chunk);
                _stream.AcceptWaveform(SampleRate, chunk);
                AudioSeconds += count / (double)SampleRate;
                offset += count;
                Decode();
                if (recognizer.IsEndpoint(_stream) || _audio.Count >= SampleRate * 12) Finish();
            }
            _expectedStart = Math.Max(_expectedStart, start + samples.Length / (double)SampleRate);
            _hasTimeline = true;
        }

        private void Decode()
        {
            while (recognizer.IsReady(_stream!))
            {
                recognizer.Decode(_stream!);
                var text = recognizer.GetResult(_stream!).Text.Trim();
                if (text != _lastText)
                {
                    _lastText = text;
                    Emit(new StreamingTranscriptEvent("partial", source, _segmentId, text, _start,
                        _audio.Count / (double)SampleRate));
                }
            }
        }

        public void Finish()
        {
            if (_stream == null) return;
            var stream = _stream;
            try
            {
                stream.AcceptWaveform(SampleRate, new float[(int)(SampleRate * 0.8)]);
                stream.InputFinished();
                Decode();
                var result = recognizer.GetResult(stream);
                var text = result.Text.Trim();
                var duration = _audio.Count / (double)SampleRate;
                StreamingTranscriptSegment[] segments;
                if (source == "microphone")
                    segments = text.Length == 0 ? [] : [new StreamingTranscriptSegment(_segmentId + ":0", source,
                        _start, duration, text, "you", "You")];
                else
                {
                    var turns = text.Length == 0 ? [] : diarizer.Process(_audio.ToArray(), _context);
                    segments = TokenAlignment.Align(_segmentId, source, _start, duration, text,
                        result.Tokens, result.Timestamps, turns);
                }
                Emit(new StreamingTranscriptEvent("final", source, _segmentId, text, _start, duration, segments));
                _context = _audio.TakeLast(SampleRate * 3).ToArray();
            }
            finally
            {
                stream.Dispose();
                _stream = null;
                _audio.Clear();
                _lastText = "";
            }
        }

        public void Dispose() { _stream?.Dispose(); _stream = null; }
    }
}
