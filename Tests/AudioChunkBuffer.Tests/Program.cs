using BetterWinTab.Services.Transcription;

var chunker = new AudioChunkBuffer(bytesPerSecond: 8, chunkSeconds: 1, blockAlign: 2);
var first = chunker.Push(new byte[] { 1, 2, 3, 4, 5, 6 });
if (first.Count != 0) throw new InvalidOperationException("A partial audio chunk was emitted too early.");

var second = chunker.Push(new byte[] { 7, 8, 9, 10, 11, 12 });
if (second.Count != 1 || second[0].Length != 8)
    throw new InvalidOperationException("The chunker did not emit one complete, aligned second of audio.");

var remainder = chunker.Flush();
if (remainder == null || remainder.Length != 4)
    throw new InvalidOperationException("The final partial chunk was not preserved.");

Console.WriteLine("Audio chunk buffer tests passed.");
