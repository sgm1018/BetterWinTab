using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using BetterWinTab.Models.Transcription;

internal static class NativeIdleTests
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("--native-idle <worker-exe> <asr-directory> <segmentation.onnx> <embedding.onnx> <mono16k-wav>");
        var data = ReadPcm16(args[5]);
        var events = Channel.CreateUnbounded<StreamingTranscriptEvent>();
        var start = new ProcessStartInfo(args[1])
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in new[] { "--stream", args[2], args[3], args[4], "auto" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new Exception("Worker did not start");
        var stderr = process.StandardError.ReadToEndAsync();
        var read = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                if (line.StartsWith("BWTA_EVENT:"))
                    await events.Writer.WriteAsync(JsonSerializer.Deserialize<StreamingTranscriptEvent>(line[11..])!);
            events.Writer.TryComplete();
        });
        try
        {
            await WaitFor("ready");
            await SendAudio(0, 0, data.Length);
            var duration = data.Length / 32000d;
            await Send(new StreamingAudioMessage("idle", "loopback", StartSeconds: duration + 1.2));
            var first = await WaitFor("final");
            if (process.HasExited || first.Text.Length == 0 || Math.Abs(first.DurationSeconds - duration) > 0.001)
                throw new Exception("Idle did not finalize captured speech while stdin stayed open");

            // An idle for a source that has never delivered audio must do nothing.
            await Send(new StreamingAudioMessage("idle", "microphone", StartSeconds: 500));

            const int firstPacketBytes = 6400;
            await SendAudio(10, 0, firstPacketBytes);
            await Send(new StreamingAudioMessage("idle", "loopback", StartSeconds: duration + 1.2));
            // Below-threshold idle must also leave the turn open.
            await Send(new StreamingAudioMessage("idle", "loopback", StartSeconds: 10.6));
            await SendAudio(10, firstPacketBytes, data.Length);
            await Send(new StreamingAudioMessage("idle", "loopback", StartSeconds: 10 + duration + 1.2));
            var second = await WaitFor("final");
            if (second.SegmentId == first.SegmentId || second.StartSeconds != 10 ||
                Math.Abs(second.DurationSeconds - duration) > 0.001 || second.Text.Length == 0)
                throw new Exception("Stale idle finalized or split the newer turn");
            await Send(new StreamingAudioMessage("finish"));
            await WaitFor("completed");
            process.StandardInput.Close();
            await process.WaitForExitAsync();
            await read;
            if (process.ExitCode != 0) throw new Exception(await stderr);
            Console.WriteLine("PASS: native idle finalizes before Stop; unknown, stale and short idle preserve turns");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL native idle: " + error.Message);
            return 1;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        async Task Send(StreamingAudioMessage message)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
            await process.StandardInput.FlushAsync();
        }
        async Task SendAudio(double beginning, int from, int end)
        {
            for (var offset = from; offset < end; offset += 3200)
                await Send(new StreamingAudioMessage("audio", "loopback",
                    Convert.ToBase64String(data, offset, Math.Min(3200, end - offset)), beginning + offset / 32000d));
        }
        async Task<StreamingTranscriptEvent> WaitFor(string type)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (await events.Reader.WaitToReadAsync(timeout.Token))
            {
                while (events.Reader.TryRead(out var update))
                {
                    if (update.Type == "error") throw new Exception(update.Message);
                    if (update.Type == type) return update;
                }
            }
            throw new Exception("Worker exited before " + type + ": " + await stderr);
        }
    }

    private static byte[] ReadPcm16(string path)
    {
        using var input = new BinaryReader(File.OpenRead(path));
        if (new string(input.ReadChars(4)) != "RIFF") throw new Exception("Expected RIFF WAV");
        input.ReadUInt32();
        if (new string(input.ReadChars(4)) != "WAVE") throw new Exception("Expected WAVE audio");
        var validFormat = false;
        while (input.BaseStream.Position + 8 <= input.BaseStream.Length)
        {
            var id = new string(input.ReadChars(4));
            var length = input.ReadInt32();
            var next = input.BaseStream.Position + length + (length % 2);
            if (id == "fmt ")
                validFormat = input.ReadInt16() == 1 && input.ReadInt16() == 1 && input.ReadInt32() == 16000;
            if (id == "data")
            {
                if (!validFormat) throw new Exception("Fixture must be mono PCM16 at 16 kHz");
                return input.ReadBytes(length);
            }
            input.BaseStream.Position = next;
        }
        throw new Exception("WAV has no sample data");
    }
}
