using System.Buffers.Binary;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// The WAVs are built byte by byte, as in <see cref="AudioTagsTests"/>: the
/// layout arithmetic - chunk padding, the extensible sub-format, 24-bit
/// sign - is what is being tested.
/// </summary>
public class WaveformTests {
    // --- WaveformBuilder ---------------------------------------------------

    [Fact]
    public void Builder_LoudHalfThenSilence_ShowsOnTheLeftOnly() {
        var builder = new WaveformBuilder(8);
        builder.Add(Repeat(0.5f, 1024), 1);
        builder.Add(Repeat(0f, 1024), 1);

        var shape = builder.Build(TimeSpan.FromSeconds(1))!;

        Assert.All(shape.Peaks[..4], p => Assert.Equal(128, p));
        Assert.All(shape.Peaks[4..], p => Assert.Equal(0, p));
        Assert.All(shape.Rms[..4], r => Assert.Equal(128, r));
    }

    [Fact]
    public void Builder_KeepsAClickThatLastsOneSample() {
        // Merging slices must keep the loudest, not the average: a click is
        // the thing the eye looks for.
        var samples = Repeat(0.01f, 100_000);
        samples[77_777] = -1f;
        var builder = new WaveformBuilder(16);
        builder.Add(samples, 1);

        var shape = builder.Build(TimeSpan.FromSeconds(2))!;

        Assert.Equal(255, shape.Peaks.Max());
        Assert.Equal(1, shape.Peaks.Count(p => p == 255));
        Assert.Equal(12, Array.IndexOf(shape.Peaks, (byte)255));
    }

    [Fact]
    public void Builder_StereoPeakIsTheLouderChannel() {
        var builder = new WaveformBuilder(4);
        builder.Add([0.25f, -0.75f, 0.25f, -0.75f], 2);

        var shape = builder.Build(TimeSpan.Zero)!;

        Assert.Equal(191, shape.Peaks[0]);
    }

    [Fact]
    public void Builder_ShorterThanTheSlices_Stretches() {
        var builder = new WaveformBuilder(8);
        builder.Add([1f, 0f], 1);

        var shape = builder.Build(TimeSpan.Zero)!;

        Assert.Equal([255, 255, 255, 255, 0, 0, 0, 0], shape.Peaks);
    }

    [Fact]
    public void Builder_NothingTaken_IsNull() {
        Assert.Null(new WaveformBuilder().Build(TimeSpan.Zero));
    }

    [Fact]
    public void Slice_NarrowsToTheLoudestPeak() {
        var shape = new Waveform([0, 255, 0, 0], [0, 255, 0, 0], TimeSpan.Zero);

        Assert.Equal(1.0, shape.Slice(0, 2).Peak);
        Assert.Equal(0.0, shape.Slice(1, 2).Peak);
    }

    // --- WavShape ----------------------------------------------------------

    [Fact]
    public void Wav16_FormatAndDuration() {
        var wav = Wav(1, 8000, 16, Pcm16(Repeat(0.5f, 16000)));

        var format = WavShape.ReadFormat(new MemoryStream(wav))!;

        Assert.Equal(1, format.Channels);
        Assert.Equal(8000, format.SampleRate);
        Assert.Equal(TimeSpan.FromSeconds(2), format.Duration);
    }

    [Fact]
    public void Wav16_ReadsTheLevel() {
        var stream = new MemoryStream(Wav(1, 8000, 16, Pcm16(Repeat(-0.5f, 4000))));
        var format = WavShape.ReadFormat(stream)!;

        var shape = WavShape.Read(stream, format, CancellationToken.None)!;

        Assert.All(shape.Peaks, p => Assert.Equal(128, p));
    }

    [Fact]
    public void Wav24_SignIsKept() {
        // -0.5 in 24 bits: 0xC00000, the high byte negative.
        byte[] data = [0x00, 0x00, 0xC0, 0x00, 0x00, 0xC0];
        var stream = new MemoryStream(Wav(1, 8000, 24, data));
        var format = WavShape.ReadFormat(stream)!;

        var shape = WavShape.Read(stream, format, CancellationToken.None)!;

        Assert.Equal(128, shape.Peaks[0]);
    }

    [Fact]
    public void WavFloat_Extensible_IsRead() {
        var data = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(data, 1f);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 1f);
        var stream = new MemoryStream(Wav(1, 8000, 32, data, extensibleTag: 3));
        var format = WavShape.ReadFormat(stream)!;

        var shape = WavShape.Read(stream, format, CancellationToken.None)!;

        Assert.True(format.IsFloat);
        Assert.Equal(255, shape.Peaks[0]);
    }

    [Fact]
    public void Wav_OddChunkBeforeData_IsSkippedWithItsPad() {
        var wav = Wav(1, 8000, 16, Pcm16([0.5f, 0.5f]), extraChunk: "abc");

        var format = WavShape.ReadFormat(new MemoryStream(wav));

        Assert.NotNull(format);
        Assert.Equal(4, format!.DataLength);
    }

    [Fact]
    public void Wav_Adpcm_IsNotOurs() {
        // Tag 2, MS ADPCM: the system's decoders take it.
        var wav = Wav(1, 8000, 4, new byte[16], tag: 2);

        Assert.Null(WavShape.ReadFormat(new MemoryStream(wav)));
    }

    [Fact]
    public void Wav_DataSizeUnfilled_TakesTheRest() {
        var wav = Wav(1, 8000, 16, Pcm16(Repeat(0.5f, 10)));
        int dataSize = wav.Length - 4 - 40;
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), 0);

        var format = WavShape.ReadFormat(new MemoryStream(wav))!;

        Assert.Equal(dataSize, format.DataLength);
    }

    [Fact]
    public void NotRiff_IsNull() {
        Assert.Null(WavShape.ReadFormat(new MemoryStream("ID3 not a wav at all"u8.ToArray())));
    }


    private static float[] Repeat(float value, int count) {
        return Enumerable.Repeat(value, count).ToArray();
    }

    private static byte[] Pcm16(float[] samples) {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++) {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)Math.Clamp(samples[i] * 32768, short.MinValue, short.MaxValue));
        }

        return bytes;
    }

    private static byte[] Wav(int channels, int rate, int bits, byte[] data,
        int tag = 1, int extensibleTag = 0, string? extraChunk = null) {
        var body = new MemoryStream();
        var w = new BinaryWriter(body);
        w.Write("WAVE"u8);

        w.Write("fmt "u8);
        w.Write(extensibleTag != 0 ? 40 : 16);
        w.Write((ushort)(extensibleTag != 0 ? 0xFFFE : tag));
        w.Write((ushort)channels);
        w.Write(rate);
        w.Write(rate * channels * bits / 8);
        w.Write((ushort)(channels * bits / 8));
        w.Write((ushort)bits);
        if (extensibleTag != 0) {
            w.Write((ushort)22);
            w.Write((ushort)bits);
            w.Write(0);
            w.Write((ushort)extensibleTag);
            w.Write(new byte[14]);
        }

        if (extraChunk is not null) {
            w.Write(Encoding.ASCII.GetBytes("LIST"));
            w.Write(extraChunk.Length);
            w.Write(Encoding.ASCII.GetBytes(extraChunk));
            if (extraChunk.Length % 2 == 1) {
                w.Write((byte)0);
            }
        }

        w.Write("data"u8);
        w.Write(data.Length);
        w.Write(data);
        w.Flush();

        var file = new MemoryStream();
        var f = new BinaryWriter(file);
        f.Write("RIFF"u8);
        f.Write((int)body.Length);
        f.Write(body.ToArray());
        f.Flush();

        return file.ToArray();
    }
}
