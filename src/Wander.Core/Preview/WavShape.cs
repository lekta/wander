using System.Buffers.Binary;
using System.Text;

namespace Wander.Core.Preview;

/// <summary>
/// The shape of a plain WAV - PCM of 8, 16, 24 or 32 bits, or floats of 32
/// or 64 - read straight from the file. A folder of sounds is mostly these,
/// and reading the samples ourselves is both faster than a decoder and
/// testable byte by byte. Anything else in a RIFF (ADPCM, mu-law, MP3
/// inside a WAV) is not ours: <see cref="ReadFormat"/> says null, and the
/// caller hands the file to the system's decoders.
/// </summary>
public static class WavShape {
    private const int FormatPcm = 1;
    private const int FormatFloat = 3;
    private const int FormatExtensible = 0xFFFE;

    /// <summary>Bytes read at a time - a whole number of frames is taken from each.</summary>
    private const int ChunkBytes = 64 * 1024;


    /// <summary>
    /// What the samples are and where they start, the stream left at the
    /// first of them; null when this is not a WAV of samples this reader
    /// knows.
    /// </summary>
    public static WavFormat? ReadFormat(Stream stream) {
        Span<byte> head = stackalloc byte[12];
        if (!ReadExactly(stream, head)
            || !head[..4].SequenceEqual("RIFF"u8)
            || !head[8..12].SequenceEqual("WAVE"u8)) {
            return null;
        }

        int tag = 0, channels = 0, rate = 0, bits = 0, align = 0;
        Span<byte> chunk = stackalloc byte[8];
        while (ReadExactly(stream, chunk)) {
            string id = Encoding.ASCII.GetString(chunk[..4]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);

            if (id == "fmt ") {
                if (size < 16 || size > 1024) {
                    return null;
                }

                var fmt = new byte[size];
                if (!ReadExactly(stream, fmt)) {
                    return null;
                }

                tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4));
                align = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(12));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14));

                // WAVE_FORMAT_EXTENSIBLE: the real tag is the first two bytes
                // of the sub-format GUID, after cbSize, valid bits and mask.
                if (tag == FormatExtensible && size >= 40) {
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(24));
                }

                SkipPad(stream, size);
            } else if (id == "data") {
                if (channels <= 0 || rate <= 0 || align != channels * bits / 8 || !Known(tag, bits)) {
                    return null;
                }

                // A writer that streamed the file and never came back to
                // fill the size in leaves 0 or all ones: the rest of the file.
                long rest = stream.Length - stream.Position;
                long length = size == 0 || size == uint.MaxValue ? rest : Math.Min(size, rest);

                return new WavFormat(channels, rate, bits, tag == FormatFloat, length - length % align);
            } else {
                if (stream.Position + size > stream.Length) {
                    return null;
                }

                stream.Seek(size, SeekOrigin.Current);
                SkipPad(stream, size);
            }
        }

        return null;
    }

    /// <summary>
    /// The shape of the samples <paramref name="format"/> describes, the
    /// stream standing at the first of them (<see cref="ReadFormat"/>).
    /// </summary>
    public static Waveform? Read(Stream stream, WavFormat format, CancellationToken cancel) {
        var builder = new WaveformBuilder();
        int frameBytes = format.Channels * format.BitsPerSample / 8;
        var bytes = new byte[ChunkBytes - ChunkBytes % frameBytes];
        var samples = new float[bytes.Length / (format.BitsPerSample / 8)];

        long left = format.DataLength;
        while (left > 0) {
            cancel.ThrowIfCancellationRequested();

            int want = (int)Math.Min(bytes.Length, left);
            int got = ReadUpTo(stream, bytes.AsSpan(0, want));
            got -= got % frameBytes;
            if (got == 0) {
                break;
            }

            int count = Decode(bytes.AsSpan(0, got), format, samples);
            builder.Add(samples.AsSpan(0, count), format.Channels);
            left -= got;
        }

        return builder.Build(format.Duration);
    }


    private static bool Known(int tag, int bits) {
        return tag == FormatPcm && bits is 8 or 16 or 24 or 32
            || tag == FormatFloat && bits is 32 or 64;
    }

    private static int Decode(ReadOnlySpan<byte> bytes, WavFormat format, float[] samples) {
        int n = 0;
        switch (format.BitsPerSample) {
            case 8:
                foreach (byte b in bytes) {
                    samples[n++] = (b - 128) / 128f;
                }

                break;
            case 16:
                for (int i = 0; i + 1 < bytes.Length; i += 2) {
                    samples[n++] = BinaryPrimitives.ReadInt16LittleEndian(bytes[i..]) / 32768f;
                }

                break;
            case 24:
                for (int i = 0; i + 2 < bytes.Length; i += 3) {
                    int v = bytes[i] | bytes[i + 1] << 8 | (sbyte)bytes[i + 2] << 16;
                    samples[n++] = v / 8388608f;
                }

                break;
            case 32 when format.IsFloat:
                for (int i = 0; i + 3 < bytes.Length; i += 4) {
                    samples[n++] = BinaryPrimitives.ReadSingleLittleEndian(bytes[i..]);
                }

                break;
            case 32:
                for (int i = 0; i + 3 < bytes.Length; i += 4) {
                    samples[n++] = BinaryPrimitives.ReadInt32LittleEndian(bytes[i..]) / 2147483648f;
                }

                break;
            case 64:
                for (int i = 0; i + 7 < bytes.Length; i += 8) {
                    samples[n++] = (float)BinaryPrimitives.ReadDoubleLittleEndian(bytes[i..]);
                }

                break;
        }

        return n;
    }

    private static void SkipPad(Stream stream, uint size) {
        if (size % 2 == 1 && stream.Position < stream.Length) {
            stream.Seek(1, SeekOrigin.Current);
        }
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer) {
        return ReadUpTo(stream, buffer) == buffer.Length;
    }

    private static int ReadUpTo(Stream stream, Span<byte> buffer) {
        int total = 0;
        while (total < buffer.Length) {
            int read = stream.Read(buffer[total..]);
            if (read == 0) {
                break;
            }

            total += read;
        }

        return total;
    }
}


/// <summary>The samples of a WAV, as <see cref="WavShape.ReadFormat"/> found them.</summary>
public sealed record WavFormat(int Channels, int SampleRate, int BitsPerSample, bool IsFloat, long DataLength) {
    public TimeSpan Duration =>
        TimeSpan.FromSeconds((double)DataLength / (Channels * BitsPerSample / 8) / SampleRate);
}
