using System.Buffers.Binary;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// The containers are built byte by byte here rather than kept as sample
/// files: what is being tested is the layout arithmetic — synch-safe sizes,
/// the endianness flip between a FLAC comment block and a FLAC picture
/// block, where a description ends in a UTF-16 frame — and a binary fixture
/// hides exactly those, because a wrong assumption baked into the fixture
/// agrees with the same wrong assumption in the reader.
/// </summary>
public class AudioTagsTests {
    // --- FLAC -------------------------------------------------------------

    [Fact]
    public void Flac_ReadsVorbisComments() {
        var flac = new FlacBuilder()
            .StreamInfo(sampleRate: 44100, channels: 2, samples: 44100 * 125)
            .Comments(("TITLE", "Идут белые снеги"), ("ARTIST", "Барто"), ("ALBUM", "Улица"), ("DATE", "2010"))
            .Build();

        var info = AudioTags.Read(new MemoryStream(flac), ".flac");

        Assert.NotNull(info);
        Assert.Equal("Идут белые снеги", info!.Title);
        Assert.Equal("Барто", info.Artist);
        Assert.Equal("Улица", info.Album);
        Assert.Equal("2010", info.Year);
    }

    [Fact]
    public void Flac_DurationComesFromTheSampleCount() {
        // The one thing FLAC states exactly, unlike MP3 where it is
        // arithmetic on the file size.
        var flac = new FlacBuilder()
            .StreamInfo(sampleRate: 48000, channels: 2, samples: 48000 * 200)
            .Comments(("TITLE", "x"))
            .Build();

        var info = AudioTags.Read(new MemoryStream(flac), ".flac");

        Assert.Equal(200, info!.Duration!.Value.TotalSeconds, precision: 3);
        Assert.Equal(48000, info.SampleRate);
        Assert.Equal(2, info.Channels);
    }

    [Fact]
    public void Flac_ReadsCoverArt() {
        byte[] cover = { 1, 2, 3, 4, 5, 6, 7 };
        var flac = new FlacBuilder()
            .StreamInfo(sampleRate: 44100, channels: 2, samples: 44100)
            .Comments(("TITLE", "x"))
            .Picture("image/jpeg", cover)
            .Build();

        var info = AudioTags.Read(new MemoryStream(flac), ".flac");

        Assert.Equal(cover, info!.Cover);
    }

    [Fact]
    public void Flac_TruncatedFile_IsNotAnException() {
        var flac = new FlacBuilder()
            .StreamInfo(sampleRate: 44100, channels: 2, samples: 44100)
            .Comments(("TITLE", "x"))
            .Build();

        var info = AudioTags.Read(new MemoryStream(flac[..(flac.Length / 2)]), ".flac");

        // Whatever it managed to read, or nothing — never a throw.
        Assert.True(info is null || info.Title is null || info.Title == "x");
    }

    [Fact]
    public void Flac_NotAFlac_ReturnsNull() {
        Assert.Null(AudioTags.Read(new MemoryStream(Encoding.ASCII.GetBytes("not a flac at all")), ".flac"));
    }


    // --- ID3v2 ------------------------------------------------------------

    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    public void Id3v2_ReadsTextFrames(byte version) {
        // 2.3 writes a frame size as a plain integer and 2.4 as a
        // synch-safe one. Reading the wrong one lands frame two inside
        // frame one, so both versions are checked with more than one frame.
        var mp3 = new Id3Builder(version)
            .Text("TIT2", "Sixteen Tons")
            .Text("TPE1", "Tennessee Ernie Ford")
            .Text("TALB", "Capitol")
            .Text("TYER", "1955")
            .Audio(seconds: 30, bitrateKbps: 128)
            .Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.Equal("Sixteen Tons", info!.Title);
        Assert.Equal("Tennessee Ernie Ford", info.Artist);
        Assert.Equal("Capitol", info.Album);
        Assert.Equal("1955", info.Year);
    }

    [Fact]
    public void Id3v2_ReadsUtf16TextWithABom() {
        var mp3 = new Id3Builder(3)
            .Text("TIT2", "Пора домой", encoding: 1)
            .Text("TPE1", "Кто-то", encoding: 1)
            .Audio(seconds: 10, bitrateKbps: 128)
            .Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.Equal("Пора домой", info!.Title);
        Assert.Equal("Кто-то", info.Artist);
    }

    [Fact]
    public void Id3v2_ReadsLatin1Text() {
        var mp3 = new Id3Builder(3)
            .Text("TIT2", "Café", encoding: 0)
            .Audio(seconds: 10, bitrateKbps: 128)
            .Build();

        Assert.Equal("Café", AudioTags.Read(new MemoryStream(mp3), ".mp3")!.Title);
    }

    [Fact]
    public void Id3v2_ReadsCoverArtPastTheDescription() {
        byte[] cover = { 9, 8, 7, 6, 5 };
        var mp3 = new Id3Builder(3)
            .Text("TIT2", "x")
            .Picture("image/png", "front cover", cover)
            .Audio(seconds: 10, bitrateKbps: 128)
            .Build();

        Assert.Equal(cover, AudioTags.Read(new MemoryStream(mp3), ".mp3")!.Cover);
    }

    [Fact]
    public void Id3v22_ThreeCharacterFrames_AreStillRead() {
        var mp3 = new Id3Builder(2)
            .Text("TT2", "Old Rip")
            .Text("TP1", "Someone")
            .Audio(seconds: 10, bitrateKbps: 128)
            .Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.Equal("Old Rip", info!.Title);
        Assert.Equal("Someone", info.Artist);
    }


    [Fact]
    public void Id3v2_Encoding0HoldingCodepagedBytes_IsNotReadAsLatin1() {
        // The everyday case in a Russian music folder, and the one that
        // made this worth writing: the encoding byte says Latin-1 because
        // the specification offers nothing else single-byte, and the bytes
        // are the tagger's own codepage. Read literally they come out as
        // "Áåãè ïî íåáó".
        var mp3 = new Id3Builder(3)
            .Raw("TIT2", 0, Cp1251("Беги по небу"))
            .Raw("TPE1", 0, Cp1251("Фадеев Максим"))
            .Raw("TALB", 0, Cp1251("Ромашки"))
            .Audio(seconds: 10, bitrateKbps: 128)
            .Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.Equal("Беги по небу", info!.Title);
        Assert.Equal("Фадеев Максим", info.Artist);
    }

    [Fact]
    public void Id3v2_Encoding0HoldingActualLatin1_StaysLatin1() {
        // The other side of the same guess: nothing Cyrillic in the bytes,
        // so the codepage reading must not be forced onto them.
        var mp3 = new Id3Builder(3)
            .Text("TIT2", "Café del Mar", encoding: 0)
            .Text("TPE1", "Björk", encoding: 0)
            .Audio(seconds: 10, bitrateKbps: 128)
            .Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.Equal("Café del Mar", info!.Title);
        Assert.Equal("Björk", info.Artist);
    }

    [Fact]
    public void Id3v1_CodepagedFields_AreReadTogether() {
        // Thirty characters is not enough text to guess a codepage from,
        // so the three fields are judged as one block.
        var tag = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(tag, 0);
        Cp1251("Снилось мне").CopyTo(tag, 3);
        Cp1251("Воскресенье").CopyTo(tag, 33);
        Cp1251("Кто виноват").CopyTo(tag, 63);

        var mp3 = new Id3Builder(3).Audio(seconds: 10, bitrateKbps: 128).V1Raw(tag).Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.Equal("Снилось мне", info!.Title);
        Assert.Equal("Воскресенье", info.Artist);
    }


    // --- ID3v1 ------------------------------------------------------------

    [Fact]
    public void Id3v1_IsReadWhenThereIsNoV2Tag() {
        var mp3 = new Id3Builder(3)
            .Audio(seconds: 10, bitrateKbps: 128)
            .V1("Trailing Title", "Trailing Artist", "Trailing Album", "1999")
            .Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.Equal("Trailing Title", info!.Title);
        Assert.Equal("Trailing Artist", info.Artist);
        Assert.Equal("1999", info.Year);
    }

    [Fact]
    public void Id3v1_LosesToV2WhenBothArePresent() {
        // Both blocks exist in plenty of files and they disagree; the newer
        // one is the one a tag editor wrote.
        var mp3 = new Id3Builder(3)
            .Text("TIT2", "The Edited One")
            .Audio(seconds: 10, bitrateKbps: 128)
            .V1("The Stale One", "x", "x", "1999")
            .Build();

        Assert.Equal("The Edited One", AudioTags.Read(new MemoryStream(mp3), ".mp3")!.Title);
    }


    // --- Duration ---------------------------------------------------------

    [Fact]
    public void Mp3_ConstantBitrate_DurationFromTheFileSize() {
        var mp3 = new Id3Builder(3)
            .Text("TIT2", "x")
            .Audio(seconds: 90, bitrateKbps: 192)
            .Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.NotNull(info!.Duration);
        Assert.Equal(90, info.Duration!.Value.TotalSeconds, tolerance: 1.0);
        Assert.Equal(192, info.BitrateKbps);
    }

    [Fact]
    public void Mp3_VariableBitrate_DurationFromTheXingFrameCount() {
        // The case the file size cannot answer: a VBR rip whose first frame
        // says 32 kbps would come out several times too long.
        const int frames = 44100 * 240 / 1152;
        var mp3 = new Id3Builder(3)
            .Text("TIT2", "x")
            .Audio(seconds: 60, bitrateKbps: 32, xingFrames: frames)
            .Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.Equal(240, info!.Duration!.Value.TotalSeconds, tolerance: 1.0);
    }

    [Fact]
    public void Mp3_NoTagsAtAll_StillReportsWhatTheFrameSays() {
        var mp3 = new Id3Builder(3).Audio(seconds: 45, bitrateKbps: 128).Build();

        var info = AudioTags.Read(new MemoryStream(mp3), ".mp3");

        Assert.NotNull(info);
        Assert.Null(info!.Title);
        Assert.Equal(45, info.Duration!.Value.TotalSeconds, tolerance: 1.0);
    }

    [Fact]
    public void Mp3_Garbage_ReturnsNullRatherThanThrowing() {
        var noise = new byte[4096];
        new Random(7).NextBytes(noise);

        var ex = Record.Exception(() => AudioTags.Read(new MemoryStream(noise), ".mp3"));

        Assert.Null(ex);
    }

    [Fact]
    public void UnknownExtension_IsNotOurs() {
        Assert.Null(AudioTags.Read(new MemoryStream(new byte[64]), ".mid"));
        Assert.False(AudioTags.IsAudio("song.mid"));
        Assert.True(AudioTags.IsAudio("song.MP3"));
        Assert.True(AudioTags.IsAudio("song.flac"));
    }


    // --- MP4 / M4A ---------------------------------------------------------

    [Fact]
    public void Mp4_ReadsTheTagList() {
        var m4a = new Mp4Builder()
            .Text("\u00A9nam", "Ноты флейты")
            .Text("\u00A9ART", "Кто-то")
            .Text("\u00A9alb", "Звукозаписи")
            .Text("\u00A9day", "2026")
            .Build();

        var info = AudioTags.Read(new MemoryStream(m4a), ".m4a");

        Assert.NotNull(info);
        Assert.Equal("Ноты флейты", info!.Title);
        Assert.Equal("Кто-то", info.Artist);
        Assert.Equal("Звукозаписи", info.Album);
        Assert.Equal("2026", info.Year);
    }

    [Fact]
    public void Mp4_ReadsCoverArt() {
        byte[] cover = { 0xFF, 0xD8, 1, 2, 3, 4 };
        var m4a = new Mp4Builder().Text("\u00A9nam", "x").Cover(cover).Build();

        Assert.Equal(cover, AudioTags.Read(new MemoryStream(m4a), ".m4a")!.Cover);
    }

    [Fact]
    public void Mp4_MoovAtTheEndOfTheFile_IsStillFound() {
        // Where recorders put it: the audio is written first and the index
        // appended when the recording stops.
        var m4a = new Mp4Builder().Text("\u00A9nam", "Запись").MediaFirst(64 * 1024).Build();

        Assert.Equal("Запись", AudioTags.Read(new MemoryStream(m4a), ".m4a")!.Title);
    }

    [Fact]
    public void Mp4_EmptyTagList_IsNoTagsRatherThanNonsense() {
        var m4a = new Mp4Builder().Build();

        Assert.Null(AudioTags.Read(new MemoryStream(m4a), ".m4a"));
    }

    [Fact]
    public void Mp4_Garbage_ReturnsNullRatherThanThrowing() {
        var noise = new byte[8192];
        new Random(3).NextBytes(noise);

        Assert.Null(Record.Exception(() => AudioTags.Read(new MemoryStream(noise), ".m4a")));
    }


    // --- WAV ---------------------------------------------------------------

    [Fact]
    public void Wav_ReadsTheInfoChunkAndTheFormat() {
        var wav = new WavBuilder()
            .Format(sampleRate: 48000, channels: 2)
            .Info(("INAM", "Моя запись 10"), ("IART", "Диктофон"), ("ICRD", "2026"))
            .Build();

        var info = AudioTags.Read(new MemoryStream(wav), ".wav");

        Assert.NotNull(info);
        Assert.Equal("Моя запись 10", info!.Title);
        Assert.Equal("Диктофон", info.Artist);
        Assert.Equal(48000, info.SampleRate);
        Assert.Equal(2, info.Channels);
    }

    [Fact]
    public void Wav_WithoutTags_StillReportsTheFormat() {
        var wav = new WavBuilder().Format(sampleRate: 16000, channels: 1).Build();

        var info = AudioTags.Read(new MemoryStream(wav), ".wav");

        Assert.Equal(16000, info!.SampleRate);
        Assert.Null(info.Title);
    }

    [Fact]
    public void Wav_NotARiff_IsNull() {
        Assert.Null(AudioTags.Read(new MemoryStream(Encoding.ASCII.GetBytes("not a wav at all!!!!")), ".wav"));
    }


    // --- routing across the widened format list ----------------------------

    [Fact]
    public void Aac_IsNotMeasuredAsAnMpegStream() {
        // An ADTS frame opens with the same eleven set bits as an MPEG one
        // and passes the layer and bitrate checks by coincidence, so the
        // MP3 arithmetic produces a plausible and wrong length. Better to
        // report none and let the player state the real one.
        var adts = new byte[64 * 1024];
        for (int i = 0; i < adts.Length; i += 512) {
            adts[i] = 0xFF;
            adts[i + 1] = 0xF1;                       // ADTS sync, MPEG-4, no CRC
            adts[i + 2] = 0x50;
            adts[i + 3] = 0x80;
        }

        var info = AudioTags.Read(new MemoryStream(adts), ".aac");

        Assert.True(info is null || info.Duration is null, "a raw AAC stream must not report an MPEG duration");
    }

    [Fact]
    public void PlayableFormats_AreTheOnesTheCardIsOfferedFor() {
        foreach (string name in new[] { "a.mp3", "a.flac", "a.m4a", "a.m4b", "a.aac", "a.wav", "a.wma", "a.ogg", "a.opus" }) {
            Assert.True(AudioTags.IsAudio(name), name);
        }
        foreach (string name in new[] { "a.mid", "a.txt", "a.mp4" }) {
            Assert.False(AudioTags.IsAudio(name), name);
        }
    }


    // --- the cover beside the track ---------------------------------------

    [Fact]
    public void CoverBeside_PrefersTheFileNamedLikeACover() {
        string dir = Folder("01 - Track.flac", "back.jpg", "Cover.jpg", "booklet.jpg");

        Assert.Equal("Cover.jpg", Path.GetFileName(AudioTags.CoverBeside(Path.Combine(dir, "01 - Track.flac"))));
    }

    [Theory]
    [InlineData("folder.jpg")]
    [InlineData("front.png")]
    [InlineData("AlbumArt.jpg")]
    public void CoverBeside_KnowsTheUsualNames(string name) {
        string dir = Folder("01 - Track.mp3", name, "scan-of-something-else.jpg", "and-another.jpg");

        Assert.Equal(name, Path.GetFileName(AudioTags.CoverBeside(Path.Combine(dir, "01 - Track.mp3"))));
    }

    [Fact]
    public void CoverBeside_FallsBackToAPictureNamedAfterTheTrack() {
        string dir = Folder("01 - Track.mp3", "01 - Track.jpg", "something.png");

        Assert.Equal("01 - Track.jpg", Path.GetFileName(AudioTags.CoverBeside(Path.Combine(dir, "01 - Track.mp3"))));
    }

    [Fact]
    public void CoverBeside_TakesALonePictureWhateverItIsCalled() {
        string dir = Folder("01 - Track.flac", "scan001.jpg");

        Assert.Equal("scan001.jpg", Path.GetFileName(AudioTags.CoverBeside(Path.Combine(dir, "01 - Track.flac"))));
    }

    [Fact]
    public void CoverBeside_TwoUnnamedPictures_PicksNeither() {
        // Front and back of the sleeve, neither named: showing the back as
        // the cover is worse than showing nothing.
        string dir = Folder("01 - Track.flac", "scan001.jpg", "scan002.jpg");

        Assert.Null(AudioTags.CoverBeside(Path.Combine(dir, "01 - Track.flac")));
    }

    [Fact]
    public void CoverBeside_NoPicturesAtAll_IsNull() {
        string dir = Folder("01 - Track.flac", "notes.txt");

        Assert.Null(AudioTags.CoverBeside(Path.Combine(dir, "01 - Track.flac")));
    }

    [Fact]
    public void CoverBeside_MissingFolder_IsNullRatherThanAThrow() {
        Assert.Null(AudioTags.CoverBeside(Path.Combine(Path.GetTempPath(), "no-such-folder-x", "t.mp3")));
    }


    private static string Folder(params string[] names) {
        string dir = Path.Combine(Path.GetTempPath(), "wander-cover-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (string name in names) {
            File.WriteAllBytes(Path.Combine(dir, name), new byte[] { 0 });
        }

        return dir;
    }


    // --- builders ---------------------------------------------------------

    /// <summary>
    /// Encodes text the way a Windows tagger of the CD era did: one byte
    /// per character out of codepage 1251. Written out rather than taken
    /// from Encoding.GetEncoding(1251), which .NET does not ship without
    /// the CodePages package — the same reason EncodingProbe carries its
    /// own table.
    /// </summary>
    private static byte[] Cp1251(string text) {
        const string table =
            "ЂЃ‚ѓ„…†‡€‰Љ‹ЊЌЋЏ" +
            "ђ‘’“”•–—�™љ›њќћџ" +
            " ЎўЈ¤Ґ¦§Ё©Є«¬­®Ї" +
            "°±Ііґµ¶·ё№є»јЅѕї" +
            "АБВГДЕЖЗИЙКЛМНОП" +
            "РСТУФХЦЧШЩЪЫЬЭЮЯ" +
            "абвгдежзийклмноп" +
            "рстуфхцчшщъыьэюя";

        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (c < 0x80) {
                bytes[i] = (byte)c;

                continue;
            }

            int index = table.IndexOf(c);
            Assert.True(index >= 0, $"{c} is not in codepage 1251");
            bytes[i] = (byte)(0x80 + index);
        }

        return bytes;
    }


    /// <summary>
    /// A minimal but structurally real MP4: the boxes on the path to the
    /// tag list, and nothing else. Built here rather than kept as a file
    /// because the offsets are the thing under test — in particular that
    /// <c>meta</c> is a full box with four bytes between its header and its
    /// children.
    /// </summary>
    private sealed class Mp4Builder {
        private readonly List<byte[]> _tags = new();
        private int _mediaBytes;


        public Mp4Builder Text(string name, string value) {
            byte[] payload = Encoding.UTF8.GetBytes(value);
            _tags.Add(Box(name, Data(1, payload)));

            return this;
        }

        public Mp4Builder Cover(byte[] jpeg) {
            _tags.Add(Box("covr", Data(13, jpeg)));

            return this;
        }

        /// <summary>Puts a big mdat in front of moov, the way a recorder does.</summary>
        public Mp4Builder MediaFirst(int bytes) {
            _mediaBytes = bytes;

            return this;
        }

        public byte[] Build() {
            byte[] ilst = Box("ilst", Concat(_tags));
            byte[] meta = Box("meta", Concat(new List<byte[]> { new byte[4], ilst }));   // version + flags
            byte[] udta = Box("udta", meta);
            byte[] moov = Box("moov", udta);

            using var file = new MemoryStream();
            file.Write(Box("ftyp", Encoding.ASCII.GetBytes("M4A mp42")));
            if (_mediaBytes > 0) {
                file.Write(Box("mdat", new byte[_mediaBytes]));
            }
            file.Write(moov);

            return file.ToArray();
        }


        private static byte[] Data(int kind, byte[] payload) {
            using var body = new MemoryStream();
            Span<byte> head = stackalloc byte[8];
            BinaryPrimitives.WriteInt32BigEndian(head, kind);
            body.Write(head);
            body.Write(payload);

            return Box("data", body.ToArray());
        }

        private static byte[] Box(string type, byte[] payload) {
            using var box = new MemoryStream();
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, payload.Length + 8);
            box.Write(size);
            box.Write(Encoding.Latin1.GetBytes(type));
            box.Write(payload);

            return box.ToArray();
        }

        private static byte[] Concat(List<byte[]> parts) {
            using var all = new MemoryStream();
            foreach (byte[] part in parts) {
                all.Write(part);
            }

            return all.ToArray();
        }
    }


    private sealed class WavBuilder {
        private readonly List<byte[]> _chunks = new();


        public WavBuilder Format(int sampleRate, int channels) {
            var fmt = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(0), 1);                  // PCM
            BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), (ushort)channels);
            BinaryPrimitives.WriteUInt32LittleEndian(fmt.AsSpan(4), (uint)sampleRate);
            _chunks.Add(Chunk("fmt ", fmt));

            return this;
        }

        public WavBuilder Info(params (string Id, string Value)[] fields) {
            using var list = new MemoryStream();
            list.Write(Encoding.ASCII.GetBytes("INFO"));
            var size = new byte[4];
            foreach (var (id, value) in fields) {
                // Codepage 1251, the way a Russian-locale recorder writes
                // it — the case the reader has to survive.
                byte[] text = Concat(Cp1251(value), new byte[] { 0 });
                list.Write(Encoding.ASCII.GetBytes(id));
                BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)text.Length);
                list.Write(size);
                list.Write(text);
                if (text.Length % 2 != 0) {
                    list.WriteByte(0);
                }
            }
            _chunks.Add(Chunk("LIST", list.ToArray()));

            return this;
        }

        public byte[] Build() {
            using var body = new MemoryStream();
            body.Write(Encoding.ASCII.GetBytes("WAVE"));
            foreach (byte[] chunk in _chunks) {
                body.Write(chunk);
            }
            body.Write(Chunk("data", new byte[256]));

            byte[] payload = body.ToArray();

            using var file = new MemoryStream();
            file.Write(Encoding.ASCII.GetBytes("RIFF"));
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)payload.Length);
            file.Write(size);
            file.Write(payload);

            return file.ToArray();
        }


        private static byte[] Concat(byte[] a, byte[] b) {
            var result = new byte[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);

            return result;
        }

        private static byte[] Chunk(string id, byte[] payload) {
            using var chunk = new MemoryStream();
            chunk.Write(Encoding.ASCII.GetBytes(id));
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)payload.Length);
            chunk.Write(size);
            chunk.Write(payload);
            if (payload.Length % 2 != 0) {
                chunk.WriteByte(0);
            }

            return chunk.ToArray();
        }
    }


    private sealed class FlacBuilder {
        private readonly List<(byte Type, byte[] Body)> _blocks = new();


        public FlacBuilder StreamInfo(int sampleRate, int channels, long samples) {
            var body = new byte[34];
            // 20 bits of sample rate, 3 of channels-1, 5 of depth-1, 36 of
            // sample count — packed across bytes 10..17.
            body[10] = (byte)(sampleRate >> 12);
            body[11] = (byte)((sampleRate >> 4) & 0xFF);
            body[12] = (byte)(((sampleRate & 0x0F) << 4) | (((channels - 1) & 0x07) << 1) | ((16 - 1) >> 4));
            body[13] = (byte)((((16 - 1) & 0x0F) << 4) | (byte)((samples >> 32) & 0x0F));
            body[14] = (byte)((samples >> 24) & 0xFF);
            body[15] = (byte)((samples >> 16) & 0xFF);
            body[16] = (byte)((samples >> 8) & 0xFF);
            body[17] = (byte)(samples & 0xFF);
            _blocks.Add((0, body));

            return this;
        }

        public FlacBuilder Comments(params (string Key, string Value)[] entries) {
            using var body = new MemoryStream();
            WriteLe(body, 0);                                   // no vendor string
            WriteLe(body, entries.Length);
            foreach (var (key, value) in entries) {
                byte[] line = Encoding.UTF8.GetBytes($"{key}={value}");
                WriteLe(body, line.Length);
                body.Write(line);
            }
            _blocks.Add((4, body.ToArray()));

            return this;
        }

        public FlacBuilder Picture(string mime, byte[] data) {
            using var body = new MemoryStream();
            WriteBe(body, 3);                                   // front cover
            byte[] mimeBytes = Encoding.ASCII.GetBytes(mime);
            WriteBe(body, mimeBytes.Length);
            body.Write(mimeBytes);
            WriteBe(body, 0);                                   // empty description
            for (int i = 0; i < 4; i++) {
                WriteBe(body, 0);                               // width, height, depth, colours
            }
            WriteBe(body, data.Length);
            body.Write(data);
            _blocks.Add((6, body.ToArray()));

            return this;
        }

        public byte[] Build() {
            using var file = new MemoryStream();
            file.Write(Encoding.ASCII.GetBytes("fLaC"));
            for (int i = 0; i < _blocks.Count; i++) {
                var (type, body) = _blocks[i];
                bool last = i == _blocks.Count - 1;
                file.WriteByte((byte)((last ? 0x80 : 0) | type));
                file.WriteByte((byte)(body.Length >> 16));
                file.WriteByte((byte)((body.Length >> 8) & 0xFF));
                file.WriteByte((byte)(body.Length & 0xFF));
                file.Write(body);
            }
            file.Write(new byte[512]);                          // stand-in for audio

            return file.ToArray();
        }

        private static void WriteLe(Stream s, int value) {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(b, value);
            s.Write(b);
        }

        private static void WriteBe(Stream s, int value) {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, value);
            s.Write(b);
        }
    }


    private sealed class Id3Builder {
        private readonly byte _version;
        private readonly List<byte[]> _frames = new();
        private byte[] _audio = Array.Empty<byte>();
        private byte[]? _v1;


        public Id3Builder(byte version) {
            _version = version;
        }


        public Id3Builder Text(string id, string value, byte encoding = 3) {
            using var body = new MemoryStream();
            body.WriteByte(encoding);
            body.Write(encoding switch {
                1 => Concat(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes(value)),
                0 => Encoding.Latin1.GetBytes(value),
                _ => Encoding.UTF8.GetBytes(value),
            });
            _frames.Add(Frame(id, body.ToArray()));

            return this;
        }

        public Id3Builder Picture(string mime, string description, byte[] data) {
            using var body = new MemoryStream();
            body.WriteByte(3);                                   // UTF-8
            body.Write(Encoding.ASCII.GetBytes(mime));
            body.WriteByte(0);
            body.WriteByte(3);                                   // front cover
            body.Write(Encoding.UTF8.GetBytes(description));
            body.WriteByte(0);
            body.Write(data);
            _frames.Add(Frame("APIC", body.ToArray()));

            return this;
        }

        /// <summary>
        /// A stretch of bytes that starts with a real MPEG-1 Layer III
        /// frame header, sized so the reader's arithmetic has a known right
        /// answer.
        /// </summary>
        public Id3Builder Audio(int seconds, int bitrateKbps, int? xingFrames = null) {
            int[] table = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
            int index = Array.IndexOf(table, bitrateKbps);
            Assert.True(index > 0, $"unsupported test bitrate {bitrateKbps}");

            var audio = new byte[bitrateKbps * 1000 / 8 * seconds];
            audio[0] = 0xFF;
            audio[1] = 0xFB;                                     // MPEG-1, Layer III, no CRC
            audio[2] = (byte)((index << 4) | (0 << 2));          // bitrate index, 44100 Hz
            audio[3] = 0x00;                                     // stereo

            if (xingFrames is { } frames) {
                int p = 36;                                      // where LAME puts it in a stereo frame
                Encoding.ASCII.GetBytes("Xing").CopyTo(audio, p);
                BinaryPrimitives.WriteInt32BigEndian(audio.AsSpan(p + 4), 1);   // "frame count present"
                BinaryPrimitives.WriteInt32BigEndian(audio.AsSpan(p + 8), frames);
            }
            _audio = audio;

            return this;
        }

        /// <summary>A frame whose bytes are supplied exactly, encoding byte and all.</summary>
        public Id3Builder Raw(string id, byte encoding, byte[] body) {
            using var frame = new MemoryStream();
            frame.WriteByte(encoding);
            frame.Write(body);
            _frames.Add(Frame(id, frame.ToArray()));

            return this;
        }

        public Id3Builder V1Raw(byte[] tag) {
            _v1 = tag;

            return this;
        }

        public Id3Builder V1(string title, string artist, string album, string year) {
            var tag = new byte[128];
            Encoding.ASCII.GetBytes("TAG").CopyTo(tag, 0);
            Encoding.Latin1.GetBytes(title).CopyTo(tag, 3);
            Encoding.Latin1.GetBytes(artist).CopyTo(tag, 33);
            Encoding.Latin1.GetBytes(album).CopyTo(tag, 63);
            Encoding.Latin1.GetBytes(year).CopyTo(tag, 93);
            _v1 = tag;

            return this;
        }

        public byte[] Build() {
            using var file = new MemoryStream();

            if (_frames.Count > 0) {
                int size = _frames.Sum(f => f.Length);
                file.Write(Encoding.ASCII.GetBytes("ID3"));
                file.WriteByte(_version);
                file.WriteByte(0);                               // revision
                file.WriteByte(0);                               // flags
                file.Write(SynchSafe(size));
                foreach (byte[] frame in _frames) {
                    file.Write(frame);
                }
            }

            file.Write(_audio);
            if (_v1 is not null) {
                file.Write(_v1);
            }

            return file.ToArray();
        }


        private byte[] Frame(string id, byte[] body) {
            using var frame = new MemoryStream();

            if (_version <= 2) {
                frame.Write(Encoding.ASCII.GetBytes(id[..3]));
                frame.WriteByte((byte)(body.Length >> 16));
                frame.WriteByte((byte)((body.Length >> 8) & 0xFF));
                frame.WriteByte((byte)(body.Length & 0xFF));
            } else {
                frame.Write(Encoding.ASCII.GetBytes(id));
                if (_version >= 4) {
                    frame.Write(SynchSafe(body.Length));
                } else {
                    Span<byte> be = stackalloc byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(be, body.Length);
                    frame.Write(be);
                }
                frame.WriteByte(0);
                frame.WriteByte(0);
            }
            frame.Write(body);

            return frame.ToArray();
        }

        private static byte[] SynchSafe(int value) {
            return new[] {
                (byte)((value >> 21) & 0x7F),
                (byte)((value >> 14) & 0x7F),
                (byte)((value >> 7) & 0x7F),
                (byte)(value & 0x7F),
            };
        }

        private static byte[] Concat(byte[] a, byte[] b) {
            var result = new byte[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);

            return result;
        }
    }
}
