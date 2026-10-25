namespace BetterWinTab.Services.Transcription;

/// <summary>Splits captured audio into fixed-duration, frame-aligned byte chunks.</summary>
public sealed class AudioChunkBuffer
{
    private readonly int _chunkSize;
    private readonly byte[] _buffer;
    private int _length;

    public AudioChunkBuffer(int bytesPerSecond, int chunkSeconds, int blockAlign)
    {
        if (bytesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        if (chunkSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSeconds));
        if (blockAlign <= 0) throw new ArgumentOutOfRangeException(nameof(blockAlign));

        var targetSize = checked(bytesPerSecond * chunkSeconds);
        _chunkSize = targetSize - targetSize % blockAlign;
        if (_chunkSize == 0) throw new ArgumentOutOfRangeException(nameof(blockAlign));
        _buffer = new byte[_chunkSize];
    }

    public IReadOnlyList<byte[]> Push(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var chunks = new List<byte[]>();
        var offset = 0;
        while (offset < data.Length)
        {
            var copyLength = Math.Min(_chunkSize - _length, data.Length - offset);
            Buffer.BlockCopy(data, offset, _buffer, _length, copyLength);
            _length += copyLength;
            offset += copyLength;

            if (_length == _chunkSize)
            {
                chunks.Add((byte[])_buffer.Clone());
                _length = 0;
            }
        }

        return chunks;
    }

    public byte[]? Flush()
    {
        if (_length == 0) return null;
        var remainder = new byte[_length];
        Buffer.BlockCopy(_buffer, 0, remainder, 0, _length);
        _length = 0;
        return remainder;
    }
}
