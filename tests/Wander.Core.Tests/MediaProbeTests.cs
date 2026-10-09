using System.Buffers.Binary;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// The containers are built here byte by byte, as in <see cref="AudioTagsTests"/>:
/// the tested thing is the layout arithmetic - offsets inside the sample
/// descriptions, EBML's variable-length numbers, RIFF's padding - and a
/// binary fixture would hide a wrong assumption it shares with the reader.
/// </summary>
public class MediaProbeTests {
    // --- MP4 ----------------------------------------------------------------

    [Fact]
    public void Mp4_DescribesVideoAndAudioTracks() {
        var file = Mp4.File("isom",
            Mp4.Mvhd(timescale: 1000, duration: 10_010),
            Mp4.VideoTrak(Mp4.Hevc(width: 3840, height: 2160, depth: 10, transfer: 16), 3840, 2160,
                timescale: 24_000, delta: 1001, samples: 240, sampleSize: 50_000),
            Mp4.AudioTrak(Mp4.Mp4a(channels: 2, rate: 48_000, Mp4.Esds(0x40, 0x29, 0xB0)), "rus", "Комментарий"));

        var info = MediaProbe.Read(new MemoryStream(file));

        Assert.NotNull(info);
        Assert.Equal("MP4", info!.Container);
        Assert.Equal(10.01, info.Duration!.Value.TotalSeconds, precision: 3);
        var video = info.Video!;
        Assert.Equal("H.265", video.Codec);
        Assert.Equal((3840, 2160), (video.Width, video.Height));
        Assert.Equal(23.976, video.FrameRate!.Value, precision: 3);
        Assert.Equal(10, video.BitDepth);
        Assert.Equal("HDR10", video.Hdr);
        Assert.Null(video.Language);
        var audio = info.Tracks.Single(t => t.Kind == MediaTrackKind.Audio);
        Assert.Equal("HE-AAC", audio.Codec);
        Assert.Equal(6, audio.Channels);
        Assert.Equal(48_000, audio.SampleRate);
        Assert.Equal("rus", audio.Language);
        Assert.Equal("Комментарий", audio.Title);
    }

    [Fact]
    public void Mp4_BitrateIsMeasuredFromTheSampleSizes() {
        var file = Mp4.File("isom",
            Mp4.Mvhd(timescale: 1000, duration: 10_000),
            Mp4.VideoTrak(Mp4.Avc(640, 480), 640, 480, timescale: 25, delta: 1, samples: 250, sampleSize: 5000));

        var video = MediaProbe.Read(new MemoryStream(file))!.Video!;

        Assert.Equal("H.264", video.Codec);
        Assert.Equal(25, video.FrameRate!.Value, precision: 3);
        Assert.Equal(250 * 5000 * 8 / 10, video.Bitrate);
    }

    [Fact]
    public void Mp4_PhoneVideoTurnedByTheMatrix_IsDescribedAsItPlays() {
        var file = Mp4.File("qt  ",
            Mp4.Mvhd(timescale: 600, duration: 1200),
            Mp4.VideoTrak(Mp4.Hevc(1920, 1080, depth: 8, transfer: 1), 1920, 1080,
                timescale: 600, delta: 20, samples: 60, sampleSize: 100, turned: true));

        var info = MediaProbe.Read(new MemoryStream(file))!;

        Assert.Equal("MOV", info.Container);
        Assert.Equal((1080, 1920), (info.Video!.Width, info.Video.Height));
        Assert.Null(info.Video.Hdr);
    }

    [Fact]
    public void Mp4_IndexAfterTheMediaData_IsStillFound() {
        var file = Mp4.File("mp42", mediaFirst: 64 * 1024,
            Mp4.Mvhd(timescale: 1000, duration: 2000),
            Mp4.VideoTrak(Mp4.Avc(320, 240), 320, 240, timescale: 30, delta: 1, samples: 60, sampleSize: 10));

        var info = MediaProbe.Read(new MemoryStream(file));

        Assert.Equal(2, info!.Duration!.Value.TotalSeconds, precision: 3);
        Assert.Equal((file.Length * 8) / 2, info.Bitrate);
    }

    [Fact]
    public void Mp4_Ac3TrackTakesChannelsFromItsConfig() {
        // fscod 0, bsid 8, bsmod 0, acmod 7 (3/2), lfe on, bit rate code 18 (640 kbps)
        int bits = (8 << 17) | (7 << 11) | (1 << 10) | (18 << 5);
        var dac3 = Mp4.Box("dac3", new[] { (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits });
        var file = Mp4.File("isom",
            Mp4.Mvhd(timescale: 1000, duration: 1000),
            Mp4.AudioTrak(Mp4.AudioEntry("ac-3", channels: 2, rate: 48_000, dac3), "eng", null));

        var audio = MediaProbe.Read(new MemoryStream(file))!.Tracks.Single();

        Assert.Equal("AC-3", audio.Codec);
        Assert.Equal(6, audio.Channels);
    }

    [Fact]
    public void Mp4_ChapterListTrack_IsNotASubtitle() {
        var file = Mp4.File("qt  ",
            Mp4.Mvhd(timescale: 1000, duration: 1000),
            Mp4.TextTrak("text", "text", enabled: false),
            Mp4.TextTrak("sbtl", "tx3g", enabled: true));

        var tracks = MediaProbe.Read(new MemoryStream(file))!.Tracks;

        var subtitle = Assert.Single(tracks);
        Assert.Equal(MediaTrackKind.Subtitle, subtitle.Kind);
        Assert.Equal("Timed Text", subtitle.Codec);
    }

    // --- Matroska -----------------------------------------------------------

    [Fact]
    public void Matroska_DescribesTracksLanguagesAndNames() {
        var file = Mkv.File("matroska",
            Mkv.Info(scale: 1_000_000, duration: 5000, title: "Фильм"),
            Mkv.Tracks(
                Mkv.Track(1, 1, "V_MPEGH/ISO/HEVC",
                    Mkv.E(0x23E383, Mkv.U(41_708_333)),
                    Mkv.E(0xE0, Mkv.E(0xB0, Mkv.U(1920)), Mkv.E(0xBA, Mkv.U(1080)),
                        Mkv.E(0x55B0, Mkv.E(0x55B2, Mkv.U(10)), Mkv.E(0x55BA, Mkv.U(18))))),
                Mkv.Track(2, 2, "A_AC3",
                    Mkv.E(0x22B59C, Mkv.S("rus")), Mkv.E(0x536E, Mkv.S("Дубляж")),
                    Mkv.E(0xE1, Mkv.E(0xB5, Mkv.F(48_000)), Mkv.E(0x9F, Mkv.U(6)))),
                Mkv.Track(3, 2, "A_AAC", Mkv.E(0xE1, Mkv.E(0xB5, Mkv.F(44_100)), Mkv.E(0x9F, Mkv.U(2)))),
                Mkv.Track(4, 0x11, "S_TEXT/UTF8", Mkv.E(0x22B59C, Mkv.S("und")), Mkv.E(0x55AA, Mkv.U(1)))));

        var info = MediaProbe.Read(new MemoryStream(file));

        Assert.NotNull(info);
        Assert.Equal("Matroska", info!.Container);
        Assert.Equal(5, info.Duration!.Value.TotalSeconds, precision: 3);
        var video = info.Video!;
        Assert.Equal("H.265", video.Codec);
        Assert.Equal((1920, 1080), (video.Width, video.Height));
        Assert.Equal(23.976, video.FrameRate!.Value, precision: 3);
        Assert.Equal(10, video.BitDepth);
        Assert.Equal("HLG", video.Hdr);
        var audio = info.Tracks.Where(t => t.Kind == MediaTrackKind.Audio).ToList();
        Assert.Equal(("AC-3", 6, 48_000, "rus", "Дубляж"), (audio[0].Codec, audio[0].Channels, audio[0].SampleRate, audio[0].Language, audio[0].Title));
        // No language element means English, by the specification.
        Assert.Equal(("AAC", "eng"), (audio[1].Codec, audio[1].Language));
        var subtitle = info.Tracks.Single(t => t.Kind == MediaTrackKind.Subtitle);
        Assert.Equal("SRT", subtitle.Codec);
        Assert.Null(subtitle.Language);
        Assert.True(subtitle.IsForced);
    }

    [Fact]
    public void Matroska_WebMDocType_IsSaidAsWebM() {
        var file = Mkv.File("webm", Mkv.Tracks(Mkv.Track(1, 1, "V_VP9", Mkv.E(0xE0, Mkv.E(0xB0, Mkv.U(1280)), Mkv.E(0xBA, Mkv.U(720))))));

        var info = MediaProbe.Read(new MemoryStream(file))!;

        Assert.Equal("WebM", info.Container);
        Assert.Equal("VP9", info.Video!.Codec);
    }

    [Fact]
    public void Matroska_TagsAtTheEnd_AreFoundThroughTheSeekHead() {
        // The statistics tags mkvmerge writes after the clusters: the
        // seek head points there, and the bitrate comes from them.
        var tracks = Mkv.Tracks(Mkv.Track(1, 2, "A_OPUS", Mkv.E(0x73C5, Mkv.U(77)), Mkv.E(0xE1, Mkv.E(0x9F, Mkv.U(2)))));
        var cluster = Mkv.E(0x1F43B675, new byte[1000]);
        var tags = Mkv.E(0x1254C367, Mkv.E(0x7373,
            Mkv.E(0x63C0, Mkv.E(0x63C5, Mkv.U(77))),
            Mkv.E(0x67C8, Mkv.E(0x45A3, Mkv.S("BPS")), Mkv.E(0x4487, Mkv.S("128000")))));
        // The seek head is written with a fixed size, so where the tags
        // land is known before it is.
        byte[] SeekHead(long at) => Mkv.E(0x114D9B74, Mkv.E(0x4DBB, Mkv.E(0x53AB, Mkv.Id(0x1254C367)), Mkv.E(0x53AC, Mkv.U8(at))));
        long tagsAt = SeekHead(0).Length + tracks.Length + cluster.Length;
        var file = Mkv.File("matroska", SeekHead(tagsAt), tracks, cluster, tags);

        var audio = MediaProbe.Read(new MemoryStream(file))!.Tracks.Single();

        Assert.Equal("Opus", audio.Codec);
        Assert.Equal(128_000, audio.Bitrate);
    }

    // --- AVI ----------------------------------------------------------------

    [Fact]
    public void Avi_DescribesItsStreams() {
        var file = Avi.File(
            Avi.Chunk("avih", Avi.Le(40_000, 0, 0, 0, 250, 0, 2, 0, 640, 480)),
            Avi.List("strl",
                Avi.Chunk("strh", Avi.Strh("vids", scale: 1, rate: 25, length: 250)),
                Avi.Chunk("strf", Avi.Bitmap(640, 480, "XVID"))),
            Avi.List("strl",
                Avi.Chunk("strh", Avi.Strh("auds", scale: 1, rate: 44_100, length: 441_000)),
                Avi.Chunk("strf", Avi.Wave(0x55, channels: 2, rate: 44_100, bytesPerSecond: 16_000)),
                Avi.Chunk("strn", Encoding.UTF8.GetBytes("Commentary\0"))));

        var info = MediaProbe.Read(new MemoryStream(file));

        Assert.NotNull(info);
        Assert.Equal("AVI", info!.Container);
        Assert.Equal(10, info.Duration!.Value.TotalSeconds, precision: 3);
        Assert.Equal(("Xvid", 640, 480), (info.Video!.Codec, info.Video.Width, info.Video.Height));
        Assert.Equal(25, info.Video.FrameRate!.Value, precision: 3);
        var audio = info.Tracks.Single(t => t.Kind == MediaTrackKind.Audio);
        Assert.Equal(("MP3", 2, 44_100, 128_000L, "Commentary"), (audio.Codec, audio.Channels, audio.SampleRate, audio.Bitrate!.Value, audio.Title));
    }

    // --- Anything else ------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 })]
    public void NotAContainer_IsNull(byte[] bytes) {
        Assert.Null(MediaProbe.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void CutShort_NeverThrows() {
        var files = new[] {
            Mkv.File("matroska", Mkv.Info(1_000_000, 1000, "x"), Mkv.Tracks(Mkv.Track(1, 1, "V_AV1"))),
            Mp4.File("isom", Mp4.Mvhd(1000, 1000), Mp4.VideoTrak(Mp4.Avc(2, 2), 2, 2, 1, 1, 2, 1)),
            Avi.File(Avi.List("strl", Avi.Chunk("strh", Avi.Strh("vids", 1, 25, 25)), Avi.Chunk("strf", Avi.Bitmap(2, 2, "H264")))),
        };
        foreach (var whole in files) {
            for (int cut = 1; cut < whole.Length; cut += 3) {
                var stream = new MemoryStream(whole, 0, cut);

                Assert.Null(Record.Exception(() => MediaProbe.Read(stream)));
            }
        }
    }


    // --- Builders -----------------------------------------------------------

    private static byte[] Concat(params byte[][] parts) {
        return parts.SelectMany(p => p).ToArray();
    }


    private static class Mp4 {
        public static byte[] Box(string type, params byte[][] parts) {
            byte[] body = Concat(parts);
            var box = new byte[8 + body.Length];
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
            body.CopyTo(box, 8);

            return box;
        }

        public static byte[] File(string brand, params byte[][] moovChildren) {
            return File(brand, 0, moovChildren);
        }

        public static byte[] File(string brand, int mediaFirst, params byte[][] moovChildren) {
            var ftyp = Box("ftyp", Encoding.ASCII.GetBytes(brand), new byte[4], Encoding.ASCII.GetBytes("isom"));
            var moov = Box("moov", moovChildren);

            return mediaFirst > 0 ? Concat(ftyp, Box("mdat", new byte[mediaFirst]), moov) : Concat(ftyp, moov);
        }

        public static byte[] Mvhd(long timescale, long duration) {
            var body = new byte[100];
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), (uint)timescale);
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), (uint)duration);

            return Box("mvhd", body);
        }

        public static byte[] VideoTrak(byte[] entry, int width, int height,
            long timescale, int delta, int samples, int sampleSize, bool turned = false) {
            var stbl = Box("stbl", Stsd(entry), Stts(samples, delta), Stsz(sampleSize, samples));

            return Box("trak",
                Tkhd(enabled: true, width, height, turned),
                Box("mdia", Mdhd(timescale, (long)samples * delta, "und"), Hdlr("vide"), Box("minf", stbl)));
        }

        public static byte[] AudioTrak(byte[] entry, string language, string? name) {
            var stbl = Box("stbl", Stsd(entry), Stts(48, 1024), Stsz(400, 48));
            var parts = new List<byte[]> {
                Tkhd(enabled: true, 0, 0, false),
                Box("mdia", Mdhd(48_000, 48 * 1024, language), Hdlr("soun"), Box("minf", stbl)),
            };
            if (name is not null) {
                parts.Add(Box("udta", Box("name", Encoding.UTF8.GetBytes(name))));
            }

            return Box("trak", parts.ToArray());
        }

        public static byte[] TextTrak(string handler, string format, bool enabled) {
            var entry = Box(format, new byte[8]);
            var stbl = Box("stbl", Stsd(entry));

            return Box("trak", Tkhd(enabled, 0, 0, false), Box("mdia", Mdhd(1000, 1000, "eng"), Hdlr(handler), Box("minf", stbl)));
        }

        public static byte[] Avc(int width, int height) {
            return VisualEntry("avc1", width, height, Box("avcC", new byte[] { 1, 100, 0, 41 }));
        }

        public static byte[] Hevc(int width, int height, int depth, int transfer) {
            var hvcC = new byte[23];
            hvcC[17] = (byte)(0xF8 | (depth - 8));
            var colr = Concat(Encoding.ASCII.GetBytes("nclx"), Be16(9), Be16(transfer), Be16(9), new byte[1]);

            return VisualEntry("hvc1", width, height, Box("hvcC", hvcC), Box("colr", colr));
        }

        public static byte[] Mp4a(int channels, int rate, byte[] esds) {
            return AudioEntry("mp4a", channels, rate, esds);
        }

        public static byte[] AudioEntry(string format, int channels, int rate, params byte[][] children) {
            var body = new byte[28];
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(16), (ushort)channels);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(18), 16);
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(24), (uint)rate << 16);

            return Box(format, body, Concat(children));
        }

        /// <summary>An <c>esds</c> with an object type and an AudioSpecificConfig.</summary>
        public static byte[] Esds(byte objectType, params byte[] config) {
            var specific = Concat(new byte[] { 0x05, (byte)config.Length }, config);
            var decoder = Concat(new byte[] { 0x04, (byte)(13 + specific.Length), objectType, 0x15 }, new byte[11], specific);
            var es = Concat(new byte[] { 0x03, (byte)(3 + decoder.Length), 0, 1, 0 }, decoder);

            return Box("esds", new byte[4], es);
        }

        private static byte[] VisualEntry(string format, int width, int height, params byte[][] children) {
            var body = new byte[78];
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(24), (ushort)width);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(26), (ushort)height);

            return Box(format, body, Concat(children));
        }

        private static byte[] Tkhd(bool enabled, int width, int height, bool turned) {
            var body = new byte[84];
            body[3] = (byte)(enabled ? 3 : 0);
            int[] matrix = turned
                ? new[] { 0, 0x10000, 0, -0x10000, 0, 0, 0, 0, 0x40000000 }
                : new[] { 0x10000, 0, 0, 0, 0x10000, 0, 0, 0, 0x40000000 };
            for (int i = 0; i < 9; i++) {
                BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(40 + i * 4), matrix[i]);
            }
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(76), (uint)width << 16);
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(80), (uint)height << 16);

            return Box("tkhd", body);
        }

        private static byte[] Mdhd(long timescale, long duration, string language) {
            var body = new byte[24];
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), (uint)timescale);
            BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), (uint)duration);
            int packed = ((language[0] - 0x60) << 10) | ((language[1] - 0x60) << 5) | (language[2] - 0x60);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(20), (ushort)packed);

            return Box("mdhd", body);
        }

        private static byte[] Hdlr(string handler) {
            return Box("hdlr", new byte[8], Encoding.ASCII.GetBytes(handler), new byte[13]);
        }

        private static byte[] Stsd(byte[] entry) {
            return Box("stsd", new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, entry);
        }

        private static byte[] Stts(int samples, int delta) {
            return Box("stts", new byte[4], Be32(1), Be32(samples), Be32(delta));
        }

        private static byte[] Stsz(int sampleSize, int samples) {
            return Box("stsz", new byte[4], Be32(sampleSize), Be32(samples));
        }

        private static byte[] Be16(int value) {
            return new[] { (byte)(value >> 8), (byte)value };
        }

        private static byte[] Be32(int value) {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);

            return bytes;
        }
    }


    private static class Mkv {
        public static byte[] File(string docType, params byte[][] segmentChildren) {
            var ebml = E(0x1A45DFA3, E(0x4286, U(1)), E(0x4282, S(docType)));

            return Concat(ebml, E(0x18538067, segmentChildren));
        }

        public static byte[] Info(long scale, double duration, string title) {
            return E(0x1549A966, E(0x2AD7B1, U(scale)), E(0x4489, F(duration)), E(0x7BA9, S(title)));
        }

        public static byte[] Tracks(params byte[][] entries) {
            return E(0x1654AE6B, entries);
        }

        public static byte[] Track(int number, int type, string codec, params byte[][] more) {
            return E(0xAE, Concat(E(0xD7, U(number)), E(0x83, U(type)), E(0x86, S(codec))), Concat(more));
        }

        /// <summary>An element: its ID as written, an eight-byte size, the payload.</summary>
        public static byte[] E(uint id, params byte[][] parts) {
            byte[] body = Concat(parts);
            var size = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)body.Length);
            size[0] = 0x01;

            return Concat(Id(id), size, body);
        }

        public static byte[] Id(uint id) {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, id);

            return bytes.SkipWhile(b => b == 0).ToArray();
        }

        public static byte[] U(long value) {
            var bytes = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            var trimmed = bytes.SkipWhile(b => b == 0).ToArray();

            return trimmed.Length > 0 ? trimmed : new byte[] { 0 };
        }

        /// <summary>An unsigned integer always eight bytes long, so a value written later fits the same space.</summary>
        public static byte[] U8(long value) {
            var bytes = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);

            return bytes;
        }

        public static byte[] F(double value) {
            var bytes = new byte[8];
            BinaryPrimitives.WriteDoubleBigEndian(bytes, value);

            return bytes;
        }

        public static byte[] S(string value) {
            return Encoding.UTF8.GetBytes(value);
        }
    }


    private static class Avi {
        public static byte[] File(params byte[][] headerChunks) {
            var hdrl = List("hdrl", headerChunks);
            var movi = List("movi", Chunk("00dc", new byte[10]));
            var body = Concat(Encoding.ASCII.GetBytes("AVI "), hdrl, movi);

            return Concat(Encoding.ASCII.GetBytes("RIFF"), Le32(body.Length), body);
        }

        public static byte[] List(string type, params byte[][] chunks) {
            var body = Concat(Encoding.ASCII.GetBytes(type), Concat(chunks));

            return Concat(Encoding.ASCII.GetBytes("LIST"), Le32(body.Length), body);
        }

        public static byte[] Chunk(string id, byte[] body) {
            var padded = body.Length % 2 == 1 ? Concat(body, new byte[1]) : body;

            return Concat(Encoding.ASCII.GetBytes(id), Le32(body.Length), padded);
        }

        public static byte[] Le(params int[] values) {
            return values.SelectMany(Le32).ToArray();
        }

        public static byte[] Strh(string type, int scale, int rate, int length) {
            var body = new byte[56];
            Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(20), scale);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(24), rate);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(32), length);

            return body;
        }

        public static byte[] Bitmap(int width, int height, string fourcc) {
            var body = new byte[40];
            BinaryPrimitives.WriteInt32LittleEndian(body, 40);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), width);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), height);
            Encoding.ASCII.GetBytes(fourcc).CopyTo(body, 16);

            return body;
        }

        public static byte[] Wave(int tag, int channels, int rate, int bytesPerSecond) {
            var body = new byte[18];
            BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)tag);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), (ushort)channels);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), rate);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), bytesPerSecond);

            return body;
        }

        private static byte[] Le32(int value) {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);

            return bytes;
        }
    }
}
