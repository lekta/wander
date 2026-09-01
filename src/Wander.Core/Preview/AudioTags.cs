using System.Buffers.Binary;
using System.Text;

namespace Wander.Core.Preview;

/// <summary>
/// What a music file says about itself: title, performer, album, how long
/// it runs, and the cover art if it carries one.
///
/// <para>
/// Written here rather than taken from a package because the two formats
/// that matter are two small, stable, fully documented byte layouts —
/// ID3 in front of an MP3, and Vorbis comments inside a FLAC — and the
/// alternative was a dependency for a few hundred lines of field reading.
/// The package Wander already has (MetadataExtractor) reads neither: it
/// exposes an MP3's bitrate and nothing of its tags, and does not know
/// FLAC at all.
/// </para>
///
/// <para>
/// Nothing here is load-bearing. Every reader returns what it managed to
/// find and null for the rest; a file with no tags previews as a file with
/// no tags, and a malformed one is a file with no tags too. Every length
/// read out of the file is checked against what is actually there, because
/// these arrive from the internet and a four-byte length must not become a
/// four-gigabyte allocation.
/// </para>
/// </summary>
public static class AudioTags {
    /// <summary>
    /// Upper bound on embedded cover art. Real covers are hundreds of
    /// kilobytes; past this the length was misread, and a misread length
    /// must not allocate.
    /// </summary>
    private const int MaxCoverBytes = 16 * 1024 * 1024;

    /// <summary>How much of the front of the file the tag readers may buffer.</summary>
    private const int MaxTagBytes = 32 * 1024 * 1024;


    /// <summary>
    /// What the preview pane treats as music.
    ///
    /// <para>
    /// This is the <em>playable</em> set, not the set we can read tags
    /// from — the two are different and it is the first that decides
    /// whether a file gets the card and the transport. Media Foundation
    /// plays all of these on Windows 10 and later; measured, one file each,
    /// rather than assumed. Ogg and Opus depend on the Web Media
    /// Extensions, which ship with the system but can be removed — the same
    /// caveat the video list carries for MKV and WEBM, and the same
    /// outcome: the pane says the file cannot be played.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".mp3", ".flac", ".m4a", ".m4b", ".aac", ".wav", ".wma", ".ogg", ".opus",
    };

    /// <summary>
    /// Picture files that count as cover art when they sit beside a track.
    /// </summary>
    private static readonly string[] _coverExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    /// <summary>
    /// The names ripping tools give a cover, best first. A folder can hold
    /// several pictures — a scan of the back, the disc, the booklet — so
    /// the name is what tells the front cover from the rest.
    /// </summary>
    private static readonly string[] _coverNames = { "cover", "folder", "front", "album", "albumart", "artwork" };


    public static bool IsAudio(string path) {
        return Extensions.Contains(Path.GetExtension(path));
    }


    /// <summary>
    /// Reads what <paramref name="path"/>'s container says about the track.
    /// Null when the file is not one of the two formats, or cannot be read
    /// at all.
    /// </summary>
    public static AudioTrackInfo? Read(string path) {
        try {
            using var stream = File.OpenRead(path);

            return Read(stream, Path.GetExtension(path));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            return null;
        }
    }


    /// <summary>
    /// Same, from an open seekable stream. Split out so the readers can be
    /// tested against bytes built in the test rather than against files.
    /// </summary>
    public static AudioTrackInfo? Read(Stream stream, string extension) {
        try {
            return extension.ToLowerInvariant() switch {
                ".flac" => FlacTags.Read(stream),
                ".mp3" => Mp3Tags.Read(stream),
                // Raw AAC carries no container of its own, only an ID3 tag
                // if the writer bothered. Its frames must *not* be measured
                // as MPEG ones: an ADTS frame starts with the same eleven
                // set bits, passes the layer and bitrate checks by
                // coincidence, and yields a plausible, wrong length — a
                // 37-second recording measured as 53.
                ".aac" => Mp3Tags.Read(stream, readFrames: false),
                ".m4a" or ".m4b" or ".mp4" => Mp4Tags.Read(stream),
                ".wav" => WavTags.Read(stream),
                // Playable, but their tags are not read: ASF (.wma) and Ogg
                // framing are each another container to walk, and neither
                // is where music with covers usually lives. The card still
                // shows the file name, the length from the player and a
                // cover lying next to the track.
                _ => null,
            };
        } catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) {
            return null;
        }
    }


    /// <summary>
    /// The cover picture lying next to <paramref name="path"/>, for a track
    /// that carries none inside it. Null when there is nothing to show.
    ///
    /// <para>
    /// Worth doing because the common case is not the tagged one: a FLAC
    /// rip of a CD is ten tracks and one <c>Cover.jpg</c>, and an MP3 from
    /// the days before embedded art has no picture in it at all. Every
    /// music player looks beside the file for this reason.
    /// </para>
    ///
    /// <para>
    /// Preference order: a file named like a cover, then one named after
    /// the track itself, then — only if there is exactly one picture in the
    /// folder — that one. The last rule is deliberately narrow: with two
    /// unnamed pictures there is no way to tell the front cover from the
    /// back, and showing the back of the sleeve as the cover is worse than
    /// showing nothing.
    /// </para>
    /// </summary>
    public static string? CoverBeside(string path) {
        try {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) {
                return null;
            }

            var pictures = new List<string>();
            foreach (string file in Directory.EnumerateFiles(directory)) {
                if (_coverExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) {
                    pictures.Add(file);
                }
            }
            if (pictures.Count == 0) {
                return null;
            }

            foreach (string wanted in _coverNames) {
                foreach (string picture in pictures) {
                    string name = Path.GetFileNameWithoutExtension(picture);
                    if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)) {
                        return picture;
                    }
                }
            }

            string track = Path.GetFileNameWithoutExtension(path);
            foreach (string picture in pictures) {
                if (Path.GetFileNameWithoutExtension(picture).Equals(track, StringComparison.OrdinalIgnoreCase)) {
                    return picture;
                }
            }

            return pictures.Count == 1 ? pictures[0] : null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
            return null;
        }
    }


    // --- shared helpers -------------------------------------------------

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes, or returns null. Used
    /// everywhere a length came out of the file, so a truncated file ends
    /// the parse instead of throwing halfway through it.
    /// </summary>
    internal static byte[]? ReadExact(Stream stream, long count) {
        if (count < 0 || count > MaxTagBytes || stream.Length - stream.Position < count) {
            return null;
        }

        var buffer = new byte[count];
        int read = 0;
        while (read < buffer.Length) {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) {
                return null;
            }
            read += n;
        }

        return buffer;
    }

    internal static byte[]? ReadCover(Stream stream, long count) {
        return count is < 0 or > MaxCoverBytes ? null : ReadExact(stream, count);
    }

    /// <summary>Empty and whitespace-only tags are absent tags, not empty strings on screen.</summary>
    internal static string? Clean(string? value) {
        value = value?.Trim().Trim('\0').Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }
}


/// <summary>
/// What a music file told us. Every field is optional because every field
/// is optional in both formats.
/// </summary>
/// <param name="Cover">Embedded cover art, as the bytes the file stores — a JPEG or a PNG.</param>
public sealed record AudioTrackInfo(
    string? Title,
    string? Artist,
    string? Album,
    string? Year,
    string? TrackNumber,
    TimeSpan? Duration,
    int? SampleRate,
    int? Channels,
    int? BitrateKbps,
    byte[]? Cover) {
    /// <summary>True when there is nothing here worth drawing a panel for.</summary>
    public bool IsEmpty =>
        Title is null && Artist is null && Album is null
        && Duration is null && Cover is null && BitrateKbps is null;
}


/// <summary>
/// MP4 metadata — what an <c>.m4a</c> carries, and therefore what a phone
/// recording or an iTunes rip carries.
///
/// <para>
/// The format is a tree of boxes, each an eight-byte header (length, then
/// a four-character type) followed by its payload. The tags live at
/// <c>moov → udta → meta → ilst</c>, and each one is itself a box whose
/// type is the field name — <c>©nam</c> for the title, with a non-ASCII
/// first byte that is part of the name, not a mistake. Inside each is a
/// <c>data</c> box with a type code saying whether the bytes are text or a
/// picture.
/// </para>
///
/// <para>
/// Only the path down to <c>ilst</c> is walked; every other branch is
/// stepped over by its own length, which is what makes this cheap on a
/// file whose audio is the other ninety-nine per cent of it.
/// </para>
/// </summary>
internal static class Mp4Tags {
    /// <summary>Boxes that hold other boxes on the way to the tags.</summary>
    private static readonly string[] _path = { "moov", "udta", "meta", "ilst" };


    public static AudioTrackInfo? Read(Stream stream) {
        stream.Position = 0;

        var ilst = Descend(stream, 0, stream.Length, 0);
        if (ilst is null) {
            return null;
        }

        string? title = null, artist = null, album = null, year = null, track = null;
        byte[]? cover = null;

        long end = ilst.Value.End;
        stream.Position = ilst.Value.Start;

        while (stream.Position + 8 <= end) {
            var (size, type) = ReadHeader(stream);
            if (size < 8 || stream.Position + size - 8 > end) {
                break;
            }

            long next = stream.Position + size - 8;
            var value = ReadData(stream, next);
            if (value is not null) {
                switch (type) {
                    case "\u00A9nam": title ??= AudioTags.Clean(value.Value.Text); break;
                    case "\u00A9ART": artist ??= AudioTags.Clean(value.Value.Text); break;
                    case "aART": artist ??= AudioTags.Clean(value.Value.Text); break;
                    case "\u00A9alb": album ??= AudioTags.Clean(value.Value.Text); break;
                    case "\u00A9day": year ??= AudioTags.Clean(value.Value.Text); break;
                    case "trkn": track ??= value.Value.TrackNumber?.ToString(); break;
                    case "covr": cover ??= value.Value.Bytes; break;
                }
            }

            stream.Position = next;
        }

        var info = new AudioTrackInfo(title, artist, album, year, track, null, null, null, null, cover);

        return info.IsEmpty ? null : info;
    }


    /// <summary>Walks moov → udta → meta → ilst, returning where the tag list lives.</summary>
    private static (long Start, long End)? Descend(Stream stream, int depth, long end, long start) {
        if (depth >= _path.Length) {
            return (start, end);
        }

        stream.Position = start == 0 ? 0 : start;
        while (stream.Position + 8 <= end) {
            var (size, type) = ReadHeader(stream);
            if (size < 8 || stream.Position + size - 8 > end) {
                return null;
            }

            long payload = stream.Position;
            long next = payload + size - 8;

            if (type == _path[depth]) {
                // "meta" is a full box: four bytes of version and flags sit
                // between its header and its children. Missing that reads
                // the first child at the wrong offset and finds nothing.
                long childStart = type == "meta" ? payload + 4 : payload;

                return Descend(stream, depth + 1, next, childStart);
            }

            stream.Position = next;
        }

        return null;
    }

    private static (long Size, string Type) ReadHeader(Stream stream) {
        byte[]? header = AudioTags.ReadExact(stream, 8);
        if (header is null) {
            return (0, "");
        }

        long size = ((long)header[0] << 24) | ((long)header[1] << 16) | ((long)header[2] << 8) | header[3];

        // Latin-1, not ASCII: the field names begin with a copyright sign.
        return (size, Encoding.Latin1.GetString(header, 4, 4));
    }

    /// <summary>
    /// The <c>data</c> box inside a tag: a type code, four reserved bytes,
    /// then the value. Type 1 is UTF-8 text, 13 and 14 are JPEG and PNG.
    /// </summary>
    private static (string? Text, byte[]? Bytes, int? TrackNumber)? ReadData(Stream stream, long end) {
        while (stream.Position + 8 <= end) {
            var (size, type) = ReadHeader(stream);
            if (size < 16 || stream.Position + size - 8 > end) {
                return null;
            }

            long next = stream.Position + size - 8;
            if (type != "data") {
                stream.Position = next;

                continue;
            }

            byte[]? head = AudioTags.ReadExact(stream, 8);
            if (head is null) {
                return null;
            }

            int kind = (head[1] << 16) | (head[2] << 8) | head[3];
            int length = (int)(next - stream.Position);

            if (kind is 13 or 14) {
                return (null, AudioTags.ReadCover(stream, length), null);
            }
            if (kind == 1) {
                byte[]? text = AudioTags.ReadExact(stream, length);

                return text is null ? null : (Encoding.UTF8.GetString(text), null, null);
            }

            // Numeric fields (a track number is a little record of shorts).
            byte[]? raw = AudioTags.ReadExact(stream, length);

            return raw is { Length: >= 4 } ? (null, null, (raw[2] << 8) | raw[3]) : null;
        }

        return null;
    }
}


/// <summary>
/// WAV, which is RIFF: a header and then chunks. What tags it has live in a
/// <c>LIST</c> chunk of type <c>INFO</c> — four-character fields, one
/// null-terminated string each. Cheap to read and worth reading, because
/// recorders and editors do write them.
/// </summary>
internal static class WavTags {
    public static AudioTrackInfo? Read(Stream stream) {
        stream.Position = 0;

        byte[]? header = AudioTags.ReadExact(stream, 12);
        if (header is null
            || Encoding.ASCII.GetString(header, 0, 4) != "RIFF"
            || Encoding.ASCII.GetString(header, 8, 4) != "WAVE") {
            return null;
        }

        string? title = null, artist = null, album = null, year = null, track = null;
        int? sampleRate = null, channels = null;

        while (stream.Position + 8 <= stream.Length) {
            byte[]? chunk = AudioTags.ReadExact(stream, 8);
            if (chunk is null) {
                break;
            }

            string id = Encoding.ASCII.GetString(chunk, 0, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            long next = stream.Position + size + (size % 2);        // chunks are word-aligned

            if (id == "fmt " && AudioTags.ReadExact(stream, Math.Min(size, 16)) is { Length: >= 8 } fmt) {
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4));
            } else if (id == "LIST" && AudioTags.ReadExact(stream, size) is { } list) {
                ReadInfo(list, ref title, ref artist, ref album, ref year, ref track);
            }

            if (next <= stream.Position && id != "fmt " && id != "LIST") {
                break;
            }
            stream.Position = Math.Min(next, stream.Length);
        }

        var info = new AudioTrackInfo(title, artist, album, year, track, null, sampleRate, channels, null, null);

        return info.IsEmpty && sampleRate is null ? null : info;
    }


    private static void ReadInfo(
        byte[] list, ref string? title, ref string? artist, ref string? album,
        ref string? year, ref string? track) {
        if (list.Length < 4 || Encoding.ASCII.GetString(list, 0, 4) != "INFO") {
            return;
        }

        // Same codepage question as an ID3 tag, and the same answer: the
        // specification says Latin-1, recorders write whatever the machine
        // uses, and one field is too short to judge. The whole block is
        // judged at once — see Mp3Tags.GuessCodepage.
        var codepage = EncodingProbe.Detect(list);

        int p = 4;
        while (p + 8 <= list.Length) {
            string id = Encoding.ASCII.GetString(list, p, 4);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(p + 4));
            p += 8;
            if (size < 0 || p + size > list.Length) {
                return;
            }

            string value = EncodingProbe.Decode(list.AsSpan(p, size), codepage);
            p += size + (size % 2);

            switch (id) {
                case "INAM": title ??= AudioTags.Clean(value); break;
                case "IART": artist ??= AudioTags.Clean(value); break;
                case "IPRD": album ??= AudioTags.Clean(value); break;
                case "ICRD": year ??= AudioTags.Clean(value); break;
                case "ITRK": track ??= AudioTags.Clean(value); break;
            }
        }
    }
}


/// <summary>
/// FLAC's metadata blocks. The format is a four-byte magic followed by a
/// chain of blocks, each with a one-byte type (top bit says "last one") and
/// a three-byte big-endian length — which makes skipping what we don't read
/// exact rather than a guess.
/// </summary>
internal static class FlacTags {
    private const byte StreamInfo = 0;
    private const byte VorbisComment = 4;
    private const byte Picture = 6;


    public static AudioTrackInfo? Read(Stream stream) {
        stream.Position = 0;
        byte[]? magic = AudioTags.ReadExact(stream, 4);
        if (magic is null || magic[0] != 'f' || magic[1] != 'L' || magic[2] != 'a' || magic[3] != 'C') {
            return null;
        }

        string? title = null, artist = null, album = null, year = null, track = null;
        TimeSpan? duration = null;
        int? sampleRate = null, channels = null;
        byte[]? cover = null;

        while (true) {
            byte[]? header = AudioTags.ReadExact(stream, 4);
            if (header is null) {
                break;
            }

            bool last = (header[0] & 0x80) != 0;
            byte type = (byte)(header[0] & 0x7F);
            int length = (header[1] << 16) | (header[2] << 8) | header[3];

            long next = stream.Position + length;

            switch (type) {
                case StreamInfo:
                    if (AudioTags.ReadExact(stream, length) is { Length: >= 18 } info) {
                        // Bit-packed from byte 10: 20 bits sample rate,
                        // 3 bits channel count minus one, 5 bits depth,
                        // then 36 bits of total sample count.
                        int rate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
                        int ch = ((info[12] >> 1) & 0x07) + 1;
                        long samples = ((long)(info[13] & 0x0F) << 32)
                            | ((long)info[14] << 24) | ((long)info[15] << 16)
                            | ((long)info[16] << 8) | info[17];

                        if (rate > 0) {
                            sampleRate = rate;
                            channels = ch;
                            if (samples > 0) {
                                duration = TimeSpan.FromSeconds(samples / (double)rate);
                            }
                        }
                    }
                    break;

                case VorbisComment:
                    if (AudioTags.ReadExact(stream, length) is { } comment) {
                        ReadComments(comment, ref title, ref artist, ref album, ref year, ref track);
                    }
                    break;

                case Picture:
                    cover ??= ReadPicture(stream, length);
                    break;
            }

            if (last || next > stream.Length) {
                break;
            }
            stream.Position = next;
        }

        int? bitrate = duration is { TotalSeconds: > 0 } d
            ? (int)Math.Round(stream.Length * 8 / d.TotalSeconds / 1000)
            : null;

        var info2 = new AudioTrackInfo(title, artist, album, year, track, duration, sampleRate, channels, bitrate, cover);

        return info2.IsEmpty ? null : info2;
    }


    /// <summary>
    /// Vorbis comments: a vendor string, a count, then that many
    /// <c>KEY=value</c> strings — all lengths little-endian, all text UTF-8.
    /// Little-endian in a big-endian container is not a mistake here; it is
    /// what the specification says, and getting it backwards reads a
    /// four-byte length as a few hundred megabytes.
    /// </summary>
    private static void ReadComments(
        byte[] block, ref string? title, ref string? artist, ref string? album,
        ref string? year, ref string? track) {
        int p = 0;
        if (!TryLength(block, ref p, out int vendor)) {
            return;
        }
        p += vendor;
        if (!TryLength(block, ref p, out int count)) {
            return;
        }

        for (int i = 0; i < count; i++) {
            if (!TryLength(block, ref p, out int length) || p + length > block.Length) {
                return;
            }

            string entry = Encoding.UTF8.GetString(block, p, length);
            p += length;

            int eq = entry.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            string key = entry[..eq].ToUpperInvariant();
            string value = entry[(eq + 1)..];

            switch (key) {
                case "TITLE": title ??= AudioTags.Clean(value); break;
                case "ARTIST": artist ??= AudioTags.Clean(value); break;
                case "ALBUMARTIST": artist ??= AudioTags.Clean(value); break;
                case "ALBUM": album ??= AudioTags.Clean(value); break;
                case "DATE": year ??= AudioTags.Clean(value); break;
                case "YEAR": year ??= AudioTags.Clean(value); break;
                case "TRACKNUMBER": track ??= AudioTags.Clean(value); break;
            }
        }
    }

    private static bool TryLength(byte[] block, ref int p, out int value) {
        value = 0;
        if (p < 0 || p + 4 > block.Length) {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(p));
        p += 4;

        return value >= 0 && value <= block.Length;
    }


    /// <summary>
    /// A PICTURE block: type, MIME, description, four dimensions, then the
    /// image. Everything big-endian here, unlike the comment block above.
    /// </summary>
    private static byte[]? ReadPicture(Stream stream, int length) {
        byte[]? block = AudioTags.ReadExact(stream, length);
        if (block is null) {
            return null;
        }

        int p = 4;                                  // picture type
        if (!Skip(block, ref p)) {                  // MIME
            return null;
        }
        if (!Skip(block, ref p)) {                  // description
            return null;
        }
        p += 16;                                    // width, height, depth, colours
        if (p + 4 > block.Length) {
            return null;
        }

        int size = BinaryPrimitives.ReadInt32BigEndian(block.AsSpan(p));
        p += 4;

        return size > 0 && p + size <= block.Length ? block[p..(p + size)] : null;
    }

    /// <summary>Steps over one length-prefixed field, big-endian.</summary>
    private static bool Skip(byte[] block, ref int p) {
        if (p + 4 > block.Length) {
            return false;
        }

        int length = BinaryPrimitives.ReadInt32BigEndian(block.AsSpan(p));
        p += 4 + length;

        return length >= 0 && p <= block.Length;
    }
}


/// <summary>
/// ID3 in front of an MP3, plus enough of the first audio frame to say how
/// long the track runs.
///
/// <para>
/// Three tag layouts are in circulation and all three are still met in a
/// music folder: ID3v2.2 with three-character frame names, ID3v2.3 and
/// ID3v2.4 with four, and the 128-byte ID3v1 block at the very end of the
/// file, which is all some older rips have.
/// </para>
/// </summary>
internal static class Mp3Tags {
    private static readonly int[] _bitratesV1L3 = {
        0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0,
    };

    private static readonly int[] _bitratesV2L3 = {
        0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0,
    };

    private static readonly int[] _sampleRatesV1 = { 44100, 48000, 32000, 0 };
    private static readonly int[] _sampleRatesV2 = { 22050, 24000, 16000, 0 };
    private static readonly int[] _sampleRatesV25 = { 11025, 12000, 8000, 0 };


    /// <param name="readFrames">
    /// Whether to measure the audio itself. False for containers that only
    /// borrow the ID3 tag — see the AAC note in <see cref="AudioTags.Read"/>.
    /// </param>
    public static AudioTrackInfo? Read(Stream stream, bool readFrames = true) {
        stream.Position = 0;

        string? title = null, artist = null, album = null, year = null, track = null;
        byte[]? cover = null;
        long audioStart = 0;

        byte[]? head = AudioTags.ReadExact(stream, 10);
        if (head is { Length: 10 } && head[0] == 'I' && head[1] == 'D' && head[2] == '3') {
            int size = SynchSafe(head, 6);
            audioStart = 10 + size;
            if (AudioTags.ReadExact(stream, size) is { } tag) {
                ReadFrames(tag, head[3], ref title, ref artist, ref album, ref year, ref track, ref cover);
            }
        }

        // The trailing block, for files that carry only that one.
        if (title is null && artist is null && album is null) {
            ReadV1(stream, ref title, ref artist, ref album, ref year, ref track);
        }

        var frame = readFrames ? Mp3Frame.FindFrom(stream, audioStart) : null;
        TimeSpan? duration = frame?.Duration(stream.Length);

        var info = new AudioTrackInfo(
            title, artist, album, year, track,
            duration, frame?.SampleRate, frame?.Channels, frame?.EffectiveBitrate(stream.Length, duration), cover);

        return info.IsEmpty ? null : info;
    }


    /// <summary>
    /// Walks the frame chain. Frame headers are ten bytes in 2.3/2.4 and
    /// six in 2.2, and the size field is a plain integer everywhere except
    /// 2.4, where it is synch-safe — read the wrong one and every frame
    /// after the first lands in the middle of the previous one's text.
    ///
    /// <para>
    /// Collected first and decoded second, because of what encoding 0
    /// means in practice. The specification says Latin-1; a great many
    /// files — every Russian rip of the CD era among them — hold the
    /// machine's own codepage instead, and read as Latin-1 they come out
    /// as <c>Áåãè ïî íåáó</c>. One field is far too short to guess a
    /// codepage from, so the guess is made once over every single-byte
    /// field in the tag together and then applied to each of them.
    /// </para>
    /// </summary>
    private static void ReadFrames(
        byte[] tag, byte version,
        ref string? title, ref string? artist, ref string? album,
        ref string? year, ref string? track, ref byte[]? cover) {
        bool short3 = version <= 2;
        int idLength = short3 ? 3 : 4;
        int headerLength = short3 ? 6 : 10;

        var texts = new List<(string Id, byte Encoding, byte[] Body)>();

        int p = 0;
        while (p + headerLength <= tag.Length) {
            string id = Encoding.ASCII.GetString(tag, p, idLength);
            if (id[0] == '\0') {
                break;                               // padding: the tag is over
            }

            int size = short3
                ? (tag[p + 3] << 16) | (tag[p + 4] << 8) | tag[p + 5]
                : version >= 4
                    ? SynchSafe(tag, p + 4)
                    : BinaryPrimitives.ReadInt32BigEndian(tag.AsSpan(p + 4));

            p += headerLength;
            if (size < 0 || p + size > tag.Length) {
                break;
            }

            var body = tag.AsSpan(p, size);
            if (id is "APIC" or "PIC") {
                cover ??= Picture(body, short3);
            } else if (size >= 2 && IsWanted(id)) {
                texts.Add((id, body[0], body[1..].ToArray()));
            }

            p += size;
        }

        var codepage = GuessCodepage(texts);

        foreach (var (id, encoding, body) in texts) {
            string? value = AudioTags.Clean(Decode(encoding, body, codepage));
            switch (id) {
                case "TIT2" or "TT2": title ??= value; break;
                case "TPE1" or "TP1": artist ??= value; break;
                case "TALB" or "TAL": album ??= value; break;
                case "TDRC" or "TYER" or "TYE": year ??= value; break;
                case "TRCK" or "TRK": track ??= value; break;
            }
        }
    }


    private static bool IsWanted(string id) {
        return id is "TIT2" or "TT2" or "TPE1" or "TP1" or "TALB" or "TAL"
            or "TDRC" or "TYER" or "TYE" or "TRCK" or "TRK";
    }


    /// <summary>
    /// Which single-byte encoding the tag's encoding-0 fields are in,
    /// decided over all of them at once. Fields that arrived in a real
    /// Unicode encoding contribute nothing to the guess — they already say
    /// what they are.
    /// </summary>
    private static TextEncodingKind GuessCodepage(List<(string Id, byte Encoding, byte[] Body)> texts) {
        var sample = new List<byte>();
        foreach (var (_, encoding, body) in texts) {
            if (encoding == 0) {
                sample.AddRange(body);
                sample.Add((byte)' ');
            }
        }

        return sample.Count == 0 ? TextEncodingKind.Latin1 : EncodingProbe.Detect(sample.ToArray());
    }


    /// <summary>
    /// A text frame's body, given the encoding byte that preceded it and
    /// the codepage the tag as a whole turned out to be in. Encodings 1..3
    /// name themselves; 0 is the one that needs the guess.
    /// </summary>
    private static string Decode(byte encoding, ReadOnlySpan<byte> bytes, TextEncodingKind codepage) {
        return encoding switch {
            1 => Utf16(bytes),
            2 => Encoding.BigEndianUnicode.GetString(bytes),
            3 => Encoding.UTF8.GetString(bytes),
            _ => EncodingProbe.Decode(bytes, codepage),
        };
    }

    /// <summary>UTF-16 with a byte-order mark in front, which decides the endianness.</summary>
    private static string Utf16(ReadOnlySpan<byte> bytes) {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) {
            return Encoding.Unicode.GetString(bytes[2..]);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        return Encoding.Unicode.GetString(bytes);
    }


    /// <summary>
    /// Cover art. In 2.3/2.4 the image type is a null-terminated MIME
    /// string; in 2.2 it is a fixed three-character format code, which is
    /// the only structural difference between the two.
    /// </summary>
    private static byte[]? Picture(ReadOnlySpan<byte> body, bool short3) {
        if (body.Length < 4) {
            return null;
        }

        byte encoding = body[0];
        int p = 1;

        if (short3) {
            p += 3;                                  // "JPG" / "PNG"
        } else {
            while (p < body.Length && body[p] != 0) {
                p++;
            }
            p++;                                     // the terminator
        }

        if (p >= body.Length) {
            return null;
        }
        p++;                                         // picture type

        // The description is terminated the way its encoding terminates
        // strings: one zero byte for the single-byte encodings, two for the
        // UTF-16 ones.
        int step = encoding is 1 or 2 ? 2 : 1;
        while (p + step <= body.Length) {
            bool zero = step == 1 ? body[p] == 0 : body[p] == 0 && body[p + 1] == 0;
            p += step;
            if (zero) {
                break;
            }
        }

        return p < body.Length ? body[p..].ToArray() : null;
    }


    /// <summary>
    /// The 128-byte block at the end of the file: fixed-width fields, no
    /// encoding byte, Latin-1 by convention. Read only when the ID3v2 tag
    /// said nothing, because where both exist the newer one is the one the
    /// user has edited.
    /// </summary>
    private static void ReadV1(
        Stream stream, ref string? title, ref string? artist, ref string? album,
        ref string? year, ref string? track) {
        if (stream.Length < 128) {
            return;
        }

        stream.Position = stream.Length - 128;
        byte[]? tag = AudioTags.ReadExact(stream, 128);
        if (tag is null || tag[0] != 'T' || tag[1] != 'A' || tag[2] != 'G') {
            return;
        }

        // The same codepage question as the v2 frames, and the same
        // answer: one guess across all three text fields, because ninety
        // characters is enough to tell Cyrillic from Latin and thirty is
        // not.
        var sample = new byte[90];
        Array.Copy(tag, 3, sample, 0, 90);
        var codepage = EncodingProbe.Detect(sample);

        title ??= Field(tag, 3, 30, codepage);
        artist ??= Field(tag, 33, 30, codepage);
        album ??= Field(tag, 63, 30, codepage);
        year ??= Field(tag, 93, 4, codepage);

        // ID3v1.1 stole the last two bytes of the comment for a track
        // number: a zero at 125 is the marker that byte 126 means one.
        if (tag[125] == 0 && tag[126] != 0) {
            track ??= tag[126].ToString();
        }
    }

    private static string? Field(byte[] tag, int offset, int length, TextEncodingKind codepage) {
        return AudioTags.Clean(EncodingProbe.Decode(tag.AsSpan(offset, length), codepage));
    }


    /// <summary>
    /// Four bytes holding 28 bits — ID3 leaves the top bit of each clear so
    /// a size can never look like a frame sync to a decoder skipping the
    /// tag.
    /// </summary>
    private static int SynchSafe(byte[] bytes, int offset) {
        if (offset + 4 > bytes.Length) {
            return 0;
        }

        return ((bytes[offset] & 0x7F) << 21)
            | ((bytes[offset + 1] & 0x7F) << 14)
            | ((bytes[offset + 2] & 0x7F) << 7)
            | (bytes[offset + 3] & 0x7F);
    }


    /// <summary>
    /// The first audio frame's header, and the VBR table that may sit
    /// inside it. Between them they answer "how long is this".
    /// </summary>
    private sealed record Mp3Frame(
        int SampleRate, int Channels, int BitrateKbps, int SamplesPerFrame,
        long AudioStart, int? VbrFrames) {
        /// <summary>
        /// Scans forward for the eleven set bits that begin an MPEG frame,
        /// starting where the tag ended. A search rather than a read
        /// because the tag length is not always honest and some files
        /// carry junk in front of the audio.
        /// </summary>
        public static Mp3Frame? FindFrom(Stream stream, long start) {
            const int window = 64 * 1024;

            stream.Position = Math.Max(0, Math.Min(start, stream.Length));
            byte[]? buffer = AudioTags.ReadExact(stream, Math.Min(window, stream.Length - stream.Position));
            if (buffer is null) {
                return null;
            }

            for (int i = 0; i + 4 <= buffer.Length; i++) {
                if (buffer[i] != 0xFF || (buffer[i + 1] & 0xE0) != 0xE0) {
                    continue;
                }
                if (Parse(buffer, i, start + i) is { } frame) {
                    return frame.WithVbr(buffer, i);
                }
            }

            return null;
        }

        public TimeSpan? Duration(long fileLength) {
            if (VbrFrames is { } frames) {
                return TimeSpan.FromSeconds((double)frames * SamplesPerFrame / SampleRate);
            }

            long audioBytes = fileLength - AudioStart;

            return audioBytes > 0
                ? TimeSpan.FromSeconds(audioBytes * 8.0 / (BitrateKbps * 1000.0))
                : null;
        }

        /// <summary>
        /// The bitrate worth showing: the average across the file when it
        /// is variable, the header's figure when it is not.
        /// </summary>
        public int? EffectiveBitrate(long fileLength, TimeSpan? duration) {
            if (VbrFrames is not null && duration is { TotalSeconds: > 0 } d) {
                return (int)Math.Round((fileLength - AudioStart) * 8 / d.TotalSeconds / 1000);
            }

            return BitrateKbps;
        }


        private static Mp3Frame? Parse(byte[] b, int i, long position) {
            int versionBits = (b[i + 1] >> 3) & 0x03;
            int layerBits = (b[i + 1] >> 1) & 0x03;
            int bitrateIndex = (b[i + 2] >> 4) & 0x0F;
            int rateIndex = (b[i + 2] >> 2) & 0x03;
            int modeBits = (b[i + 3] >> 6) & 0x03;

            // Layer III only — 1 is the encoding for it, and everything
            // else in a folder of music is either Layer III or not an MP3.
            if (versionBits == 1 || layerBits != 1 || bitrateIndex is 0 or 15 || rateIndex == 3) {
                return null;
            }

            bool mpeg1 = versionBits == 3;
            int sampleRate = versionBits switch {
                3 => _sampleRatesV1[rateIndex],
                2 => _sampleRatesV2[rateIndex],
                _ => _sampleRatesV25[rateIndex],
            };
            if (sampleRate == 0) {
                return null;
            }

            int bitrate = mpeg1 ? _bitratesV1L3[bitrateIndex] : _bitratesV2L3[bitrateIndex];
            if (bitrate == 0) {
                return null;
            }

            return new Mp3Frame(
                sampleRate,
                modeBits == 3 ? 1 : 2,
                bitrate,
                mpeg1 ? 1152 : 576,
                position,
                VbrFrames: null);
        }

        /// <summary>
        /// A variable-bitrate file puts a frame count in its first frame —
        /// "Xing" or "Info" from LAME, "VBRI" from Fraunhofer. Without it
        /// the only length available is the first frame's bitrate applied
        /// to the whole file, which on a VBR rip is wrong by minutes.
        /// </summary>
        private Mp3Frame WithVbr(byte[] buffer, int frameStart) {
            for (int p = frameStart; p + 12 <= buffer.Length && p < frameStart + 200; p++) {
                bool xing = buffer[p] == 'X' && buffer[p + 1] == 'i' && buffer[p + 2] == 'n' && buffer[p + 3] == 'g';
                bool info = buffer[p] == 'I' && buffer[p + 1] == 'n' && buffer[p + 2] == 'f' && buffer[p + 3] == 'o';
                bool vbri = buffer[p] == 'V' && buffer[p + 1] == 'B' && buffer[p + 2] == 'R' && buffer[p + 3] == 'I';

                if (xing || info) {
                    int flags = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(p + 4));
                    if ((flags & 1) != 0 && p + 12 <= buffer.Length) {
                        int frames = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(p + 8));

                        return frames > 0 ? this with { VbrFrames = frames } : this;
                    }

                    return this;
                }
                if (vbri && p + 18 <= buffer.Length) {
                    int frames = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(p + 14));

                    return frames > 0 ? this with { VbrFrames = frames } : this;
                }
            }

            return this;
        }
    }
}
