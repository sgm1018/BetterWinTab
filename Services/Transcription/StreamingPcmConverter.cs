using System.Buffers.Binary;
using NAudio.Dsp;
using NAudio.Wave;

namespace BetterWinTab.Services.Transcription;

/// <summary>Maintains the resampling filter between packets from one capture endpoint.</summary>
public sealed class StreamingPcmConverter
{
    private readonly WaveFormat _format;
    private readonly bool _float;
    private readonly WdlResampler? _resampler;

    public StreamingPcmConverter(WaveFormat format)
    {
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _float = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible && extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        var pcm = format.Encoding == WaveFormatEncoding.Pcm ||
            format is WaveFormatExtensible pcmExtensible && pcmExtensible.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71");
        if (format.Channels < 1 || format.SampleRate <= 0 ||
            !(_float && format.BitsPerSample is 32 or 64 || pcm && format.BitsPerSample is 8 or 16 or 24 or 32))
            throw new NotSupportedException($"Unsupported capture format: {format}.");
        if (format.SampleRate != 16000)
        {
            _resampler = new WdlResampler();
            _resampler.SetMode(true, 2, false);
            _resampler.SetFilterParms();
            _resampler.SetFeedMode(true);
            _resampler.SetRates(format.SampleRate, 16000);
        }
    }

    public byte[] Convert(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length % _format.BlockAlign != 0)
            throw new InvalidDataException("A capture packet ends inside an audio frame.");
        if (data.Length == 0) return Array.Empty<byte>();
        if (_format.SampleRate == 16000 && _format.Channels == 1 && _format.BitsPerSample == 16 && !_float)
            return (byte[])data.Clone();
        var frames = data.Length / _format.BlockAlign;
        var mono = new float[frames];
        var bytesPerSample = _format.BitsPerSample / 8;
        for (var frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            for (var channel = 0; channel < _format.Channels; channel++)
            {
                var bytes = data.AsSpan(frame * _format.BlockAlign + channel * bytesPerSample, bytesPerSample);
                double sample = _float
                    ? bytesPerSample == 4 ? BitConverter.ToSingle(bytes) : BitConverter.ToDouble(bytes)
                    : bytesPerSample switch
                    {
                        1 => (bytes[0] - 128) / 128d,
                        2 => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768d,
                        3 => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8 >> 8) / 8388608d,
                        4 => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648d,
                        _ => 0
                    };
                if (double.IsFinite(sample)) sum += sample;
            }
            mono[frame] = (float)(sum / _format.Channels);
        }
        if (_resampler != null)
        {
            _resampler.ResamplePrepare(frames, 1, out var input, out var offset);
            mono.CopyTo(input, offset);
            var output = new float[(int)Math.Ceiling((frames + 64d) * 16000 / _format.SampleRate) + 128];
            var count = _resampler.ResampleOut(output, 0, frames, output.Length, 1);
            mono = output.AsSpan(0, count).ToArray();
        }
        var result = new byte[mono.Length * 2];
        for (var i = 0; i < mono.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(i * 2),
                (short)Math.Clamp(Math.Round(mono[i] * 32768d), short.MinValue, short.MaxValue));
        return result;
    }
}
