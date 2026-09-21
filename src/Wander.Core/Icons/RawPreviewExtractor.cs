using System.Buffers.Binary;
using System.Text;

namespace Wander.Core.Icons;

/// <summary>
/// Pulls the display-ready JPEG that every RAW file carries inside it.
///
/// <para>
/// The reason this exists rather than "just let WIC decode the RAW": WIC
/// decodes the actual sensor data, which for a 33 MB CR3 measures ~1.2 s
/// on a warm cache — and neither <c>DecodePixelWidth</c> nor the decoder's
/// own <c>Thumbnail</c> property short-circuits it (both measured within
/// 20 % of the full decode). Reading the embedded preview out of the
/// container and handing those bytes to a plain JPEG decoder is ~10 ms for
/// the same file. That is the difference between a preview pane that feels
/// instant and one that visibly stalls, and it's the same trick every fast
/// RAW browser uses.
/// </para>
///
/// <para>
/// Two container shapes cover the formats Wander lists:
/// </para>
/// <list type="bullet">
///   <item><b>ISO-BMFF</b> (Canon CR3) — an MP4-style box tree with the
///   preview in a Canon-specific <c>uuid</c> box and a full-size JPEG as
///   the first track.</item>
///   <item><b>TIFF</b> (CR2, NEF, ARW, DNG, most others) — IFDs, one of
///   which points at a JPEG.</item>
/// </list>
///
/// <para>
/// Nothing here is load-bearing: a null return means the caller falls back
/// to the ordinary decode path, so a format we misread costs performance,
/// never correctness. Everything is bounds-checked against the stream
/// length — these are files from a camera, a card reader or the internet,
/// and a malformed one must not turn into a huge allocation.
/// </para>
/// </summary>
public static class RawPreviewExtractor {
    /// <summary>
    /// Upper bound on a preview we're willing to buffer. Real embedded
    /// previews are hundreds of kilobytes; anything past this is a
    /// misparse, and a misparse must not allocate half a gigabyte.
    /// </summary>
    private const int MaxPreviewBytes = 32 * 1024 * 1024;

    /// <summary>Enough of a JPEG to reach its frame header.</summary>
    private const int JpegProbeBytes = 4096;


    /// <summary>
    /// Embedded preview as JPEG bytes, or null when there is none we can
    /// use. <paramref name="stream"/> must be seekable; its position is not
    /// preserved.
    /// </summary>
    /// <param name="fullSize">
    /// The biggest JPEG the file carries rather than the quickest one to
    /// read. The same thing in a TIFF-shaped RAW, which gives its biggest
    /// either way; in a CR3 the quick one is the 1620-px <c>PRVW</c> and the
    /// big one the full-size JPEG of the first track - 1.7 MB against
    /// 0.3 MB, 60-110 ms to decode against ~10 (6000x4000, 2026-09-21). The
    /// preview pane asks for it: at 1:1 zoom the small one is a third of the
    /// picture blown up. A thumbnail does not need it.
    /// </param>
    public static byte[]? Extract(Stream stream, bool fullSize = false) {
        try {
            if (stream.Length < 16) {
                return null;
            }

            var head = new byte[8];
            stream.Position = 0;
            if (stream.Read(head, 0, 8) < 8) {
                return null;
            }

            if (Ascii(head, 4) == "ftyp") {
                return FromBmff(stream, fullSize);
            }
            if (head[0] == 'I' && head[1] == 'I') {
                return FromTiff(stream, littleEndian: true);
            }
            if (head[0] == 'M' && head[1] == 'M') {
                return FromTiff(stream, littleEndian: false);
            }

            return null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            return null;
        }
    }


    // --- ISO-BMFF (Canon CR3) ------------------------------------------

    /// <summary>Canon's preview <c>uuid</c> box, the one holding <c>PRVW</c>.</summary>
    private static readonly byte[] _canonPreviewUuid = {
        0xea, 0xf4, 0x2b, 0x5e, 0x1c, 0x98, 0x4b, 0x88,
        0xb9, 0xfb, 0xb7, 0xdc, 0x40, 0x6e, 0x4d, 0x16,
    };

    private static byte[]? FromBmff(Stream s, bool fullSize) {
        // Top-level boxes, and for the full-size JPEG one path down the
        // movie box: walking the rest would mean understanding the rest of
        // the tree. In a CR3 the movie box comes first, the preview after.
        foreach (var box in Boxes(s, 0, s.Length)) {
            if (fullSize && box.Type == "moov" && FromFirstTrack(s, box) is { } full) {
                return full;
            }
            if (box.Type == "uuid" && IsCanonPreview(s, box)) {
                return FromPrvw(s, box.Body + 16, box.End);
            }
        }

        return null;
    }

    private static bool IsCanonPreview(Stream s, Box box) {
        var uuid = new byte[16];
        s.Position = box.Body;

        return s.Read(uuid, 0, 16) == 16 && uuid.AsSpan().SequenceEqual(_canonPreviewUuid);
    }

    /// <summary>
    /// The full-size JPEG of a CR3: the one sample of the first track
    /// (moov / trak / mdia / minf / stbl), its length from <c>stsz</c> and
    /// its place from <c>co64</c> - or <c>stco</c>, the short form. The
    /// other tracks hold sensor data, which the JPEG check turns away should
    /// a camera ever put one first.
    /// </summary>
    private static byte[]? FromFirstTrack(Stream s, Box moov) {
        if (Child(s, moov, "trak") is not { } trak
            || Child(s, trak, "mdia") is not { } mdia
            || Child(s, mdia, "minf") is not { } minf
            || Child(s, minf, "stbl") is not { } stbl) {
            return null;
        }

        long length = 0;
        long offset = 0;
        foreach (var box in Boxes(s, stbl.Body, stbl.End)) {
            switch (box.Type) {
                case "stsz":
                    length = FirstSampleSize(s, box);
                    break;

                case "co64":
                    offset = FirstChunkOffset(s, box, wide: true);
                    break;

                case "stco":
                    offset = FirstChunkOffset(s, box, wide: false);
                    break;
            }
        }

        return length > 0 && offset > 0 ? ReadJpeg(s, offset, length) : null;
    }

    /// <summary><c>stsz</c>: one size for every sample, or zero and a table after the count.</summary>
    private static long FirstSampleSize(Stream s, Box stsz) {
        var buf = new byte[16];
        int room = (int)Math.Min(stsz.End - stsz.Body, buf.Length);
        s.Position = stsz.Body;
        if (room < 12 || s.Read(buf, 0, room) < room) {
            return 0;
        }

        uint common = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(4));
        if (common != 0) {
            return common;
        }

        uint count = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(8));

        return count > 0 && room == buf.Length ? BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12)) : 0;
    }

    /// <summary><c>co64</c> / <c>stco</c>: the count, then 8- or 4-byte offsets.</summary>
    private static long FirstChunkOffset(Stream s, Box box, bool wide) {
        var buf = new byte[wide ? 16 : 12];
        s.Position = box.Body;
        if (box.End - box.Body < buf.Length || s.Read(buf, 0, buf.Length) < buf.Length
            || BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(4)) == 0) {
            return 0;
        }

        // An offset past long.MaxValue turns negative here, and ReadJpeg
        // turns a negative offset away.
        return wide
            ? (long)BinaryPrimitives.ReadUInt64BigEndian(buf.AsSpan(8))
            : BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(8));
    }

    private static Box? Child(Stream s, Box parent, string type) {
        foreach (var box in Boxes(s, parent.Body, parent.End)) {
            if (box.Type == type) {
                return box;
            }
        }

        return null;
    }

    /// <summary>
    /// The boxes from <paramref name="from"/> to <paramref name="to"/>, one
    /// level deep. The walk ends at the first box that does not fit: a
    /// truncated or corrupt tree is read up to the damage and no further.
    /// </summary>
    private static IEnumerable<Box> Boxes(Stream s, long from, long to) {
        var header = new byte[16];
        long pos = from;
        while (pos + 8 <= to) {
            s.Position = pos;
            if (s.Read(header, 0, 8) < 8) {
                yield break;
            }

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = Ascii(header, 4);
            long body = pos + 8;

            if (size == 1) {
                if (s.Read(header, 8, 8) < 8) {
                    yield break;
                }
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                body = pos + 16;
            } else if (size == 0) {
                size = to - pos;
            }

            // A box that doesn't advance would spin this loop forever; one
            // that claims more than is left - or a 64-bit size that went
            // negative - ends it. Compared as "size > to - pos" so a huge
            // size cannot overflow the sum.
            if (size < body - pos || size > to - pos) {
                yield break;
            }

            yield return new Box(type, body, pos + size);
            pos += size;
        }
    }

    /// <summary>
    /// Inside the preview uuid box: 8 bytes of header, then a <c>PRVW</c>
    /// box whose last field before the payload is the JPEG's length.
    /// </summary>
    private static byte[]? FromPrvw(Stream s, long from, long boxEnd) {
        const int PrvwHeader = 24;
        var buf = new byte[PrvwHeader];
        s.Position = from + 8;
        if (s.Read(buf, 0, PrvwHeader) < PrvwHeader || Ascii(buf, 4) != "PRVW") {
            return null;
        }

        long length = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(20));
        long start = from + 8 + PrvwHeader;

        return start + length <= boxEnd ? ReadJpeg(s, start, length) : null;
    }


    // --- TIFF (CR2 / NEF / ARW / DNG) ----------------------------------

    private const ushort TagCompression = 0x0103;
    private const ushort TagStripOffsets = 0x0111;
    private const ushort TagStripByteCounts = 0x0117;
    private const ushort TagSubIfds = 0x014A;
    private const ushort TagJpegOffset = 0x0201;
    private const ushort TagJpegLength = 0x0202;

    /// <summary>Cap on the IFD walk — a corrupt file must not make us chase pointers all day.</summary>
    private const int MaxIfds = 32;

    private static byte[]? FromTiff(Stream s, bool littleEndian) {
        var head = new byte[8];
        s.Position = 0;
        if (s.Read(head, 0, 8) < 8) {
            return null;
        }

        var pending = new Queue<long>();
        var visited = new HashSet<long>();
        pending.Enqueue(U32(head.AsSpan(4), littleEndian));

        // Every JPEG the file points at, biggest first: the biggest one is
        // the full-size preview in a CR2 or ARW, but in a DNG it can be the
        // raw payload itself (lossless JPEG, undecodable) — so this is a
        // list of candidates to try, not a single answer.
        var candidates = new List<(long Offset, long Length)>();

        while (pending.Count > 0 && visited.Count < MaxIfds) {
            long ifd = pending.Dequeue();
            if (ifd < 8 || ifd >= s.Length || !visited.Add(ifd)) {
                continue;
            }

            var entries = ReadIfd(s, ifd, littleEndian, out long next);
            if (next > 0) {
                pending.Enqueue(next);
            }
            foreach (long sub in SubIfds(s, entries, littleEndian)) {
                pending.Enqueue(sub);
            }

            var found = JpegPointer(entries);
            if (found.Length > 0 && found.Offset > 0 && found.Offset + found.Length <= s.Length) {
                candidates.Add(found);
            }
        }

        foreach (var (offset, length) in candidates.OrderByDescending(c => c.Length)) {
            if (ReadJpeg(s, offset, length) is { } jpeg) {
                return jpeg;
            }
        }

        return null;
    }

    private static List<IfdEntry> ReadIfd(Stream s, long offset, bool little, out long next) {
        next = 0;
        var result = new List<IfdEntry>();

        var countBuf = new byte[2];
        s.Position = offset;
        if (s.Read(countBuf, 0, 2) < 2) {
            return result;
        }

        int count = U16(countBuf, little);
        // 12 bytes per entry + 4 for the next-IFD pointer. A bogus count
        // here is the classic way a fuzzed TIFF asks for a huge buffer.
        if (count == 0 || offset + 2 + (12L * count) + 4 > s.Length) {
            return result;
        }

        var buf = new byte[12 * count + 4];
        if (s.Read(buf, 0, buf.Length) < buf.Length) {
            return result;
        }

        for (int i = 0; i < count; i++) {
            var e = buf.AsSpan(i * 12, 12);
            result.Add(new IfdEntry(U16(e, little), U32(e[4..], little), U32(e[8..], little)));
        }
        next = U32(buf.AsSpan(12 * count), little);

        return result;
    }

    private static IEnumerable<long> SubIfds(Stream s, List<IfdEntry> entries, bool little) {
        int index = entries.FindIndex(x => x.Tag == TagSubIfds);
        if (index < 0) {
            yield break;
        }

        var entry = entries[index];
        // One sub-IFD fits in the entry's own value slot; several are a
        // pointer to an array of offsets.
        if (entry.Count == 1) {
            yield return entry.Value;
            yield break;
        }
        if (entry.Count == 0 || entry.Count > MaxIfds || entry.Value + 4L * entry.Count > s.Length) {
            yield break;
        }

        var buf = new byte[4 * entry.Count];
        s.Position = entry.Value;
        if (s.Read(buf, 0, buf.Length) < buf.Length) {
            yield break;
        }
        for (int i = 0; i < entry.Count; i++) {
            yield return U32(buf.AsSpan(i * 4), little);
        }
    }

    /// <summary>Where this IFD says a JPEG lives — the dedicated tags first, then a single JPEG strip.</summary>
    private static (long Offset, long Length) JpegPointer(List<IfdEntry> entries) {
        var offset = Find(entries, TagJpegOffset);
        var length = Find(entries, TagJpegLength);
        if (offset is { } o && length is { Value: > 0 } l) {
            return (o.Value, l.Value);
        }

        // Compression 6 (old-style JPEG) / 7 (JPEG) with the whole image in
        // one strip is how CR2 stores its full-size preview. Multiple strips
        // or tiles mean actual raw data, which is not a JPEG we can show.
        var compression = Find(entries, TagCompression);
        if (compression is not { Value: 6 or 7 }) {
            return (0, 0);
        }

        var strips = Find(entries, TagStripOffsets);
        var counts = Find(entries, TagStripByteCounts);
        if (strips is not { Count: 1 } || counts is not { Count: 1, Value: > 0 }) {
            return (0, 0);
        }

        return (strips.Value.Value, counts.Value.Value);
    }

    private static IfdEntry? Find(List<IfdEntry> entries, ushort tag) {
        int i = entries.FindIndex(x => x.Tag == tag);

        return i < 0 ? null : entries[i];
    }


    // --- Shared --------------------------------------------------------

    /// <summary>
    /// Reads a candidate preview, but only after its header proves it's a
    /// JPEG an ordinary decoder can handle — so a wrong guess costs a 4 KB
    /// read rather than a multi-megabyte one.
    /// </summary>
    private static byte[]? ReadJpeg(Stream s, long offset, long length) {
        // "offset > Length - length", not a sum: a CR3's 64-bit chunk offset
        // comes from the file, and one near long.MaxValue would wrap the sum.
        if (length <= 4 || length > MaxPreviewBytes || offset < 0 || offset > s.Length - length) {
            return null;
        }

        var probe = new byte[(int)Math.Min(length, JpegProbeBytes)];
        s.Position = offset;
        if (s.ReadAtLeast(probe, probe.Length, throwOnEndOfStream: false) < probe.Length || !IsDisplayableJpeg(probe)) {
            return null;
        }

        var jpeg = new byte[length];
        s.Position = offset;

        return s.ReadAtLeast(jpeg, jpeg.Length, throwOnEndOfStream: false) == jpeg.Length ? jpeg : null;
    }

    /// <summary>
    /// True for a JPEG whose frame is baseline / extended / progressive.
    /// The point of the check is SOF3 and friends: a DNG's or NEF's raw
    /// payload is *also* a JPEG stream, just a lossless one no viewer can
    /// render, and it is usually the biggest one in the file.
    /// </summary>
    private static bool IsDisplayableJpeg(ReadOnlySpan<byte> data) {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) {
            return false;
        }

        int i = 2;
        while (i + 3 < data.Length && data[i] == 0xFF) {
            byte marker = data[i + 1];

            // Standalone markers carry no length field.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8) {
                i += 2;
                continue;
            }
            // Start of scan / end of image before any frame header: not a
            // frame we can classify, so don't claim we can show it.
            if (marker is 0xDA or 0xD9) {
                return false;
            }
            if (marker is 0xC0 or 0xC1 or 0xC2) {
                return true;
            }
            // Every other SOFn — lossless, arithmetic-coded, hierarchical.
            if (marker is 0xC3 or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF)) {
                return false;
            }

            int segment = (data[i + 2] << 8) | data[i + 3];
            if (segment < 2) {
                return false;
            }
            i += 2 + segment;
        }

        // Ran out of probe before the frame header — a JPEG with a huge
        // comment or colour profile. Let the decoder be the judge.
        return i >= data.Length;
    }

    private static string Ascii(byte[] buf, int offset) {
        return Encoding.ASCII.GetString(buf, offset, 4);
    }

    private static ushort U16(ReadOnlySpan<byte> s, bool little) {
        return little ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s);
    }

    private static uint U32(ReadOnlySpan<byte> s, bool little) {
        return little ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
    }


    private readonly record struct IfdEntry(ushort Tag, uint Count, uint Value);

    /// <summary>An ISO-BMFF box: its four-letter type, where its body starts and where the box ends.</summary>
    private readonly record struct Box(string Type, long Body, long End);
}
