using System.Buffers.Binary;
using System.Text;
using Wander.Core.FileSystem;
using Wander.Core.Icons;

namespace Wander.Core.Companions;

/// <summary>
/// The rating written into the photo itself: by the camera (a Canon R body
/// puts <c>xmp:Rating</c> into the CR3), by Windows' own stars, by an editor
/// on export. Wander reads it and never writes it (decision 2026-10-01): a
/// sidecar with the field overrides it, see <see cref="Merge"/>.
///
/// <para>
/// Only the header is read - the metadata of a 30 MB RAW sits in its first
/// hundred kilobytes, a few small reads (stand 2026-10-07: 0.13 ms a CR3
/// warm, MetadataExtractor 2 ms). Where it lives depends on the container,
/// not the maker: an ISO box file (CR3, HEIF) carries XMP in a uuid box and
/// EXIF in Canon's CMT1; a JPEG in its APP1 segments; a TIFF-shaped RAW
/// (CR2, NEF, ARW, DNG, ORF, RW2, PEF) in IFD0, tag 0x02BC for XMP and
/// 0x4746 for the EXIF rating; a RAF wraps a JPEG. XMP wins over EXIF.
/// </para>
/// </summary>
public static class EmbeddedRating {
    private const int MaxPacket = 1 << 20;
    private const int MaxBlocks = 64;
    private const int TagRating = 0x4746;
    private const int TagXmp = 0x02BC;

    private static readonly byte[] _xmpUuid = {
        0xbe, 0x7a, 0xcf, 0xcb, 0x97, 0xa9, 0x42, 0xe8,
        0x9c, 0x71, 0x99, 0x94, 0x91, 0xe3, 0xaf, 0xac,
    };

    private static readonly byte[] _canonMetaUuid = {
        0x85, 0xc0, 0xb6, 0x87, 0x82, 0x0f, 0x11, 0xe0,
        0x81, 0x11, 0xf4, 0xce, 0x46, 0x2b, 0x6a, 0x48,
    };

    private static readonly byte[] _jpegXmp = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");
    private static readonly byte[] _jpegExif = Encoding.ASCII.GetBytes("Exif\0\0");
    private static readonly byte[] _raf = Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW");

    /// <summary>What a camera writes besides its RAW: JPEG, TIFF, HEIF.</summary>
    private static readonly HashSet<string> _cameraFormats = new(StringComparer.OrdinalIgnoreCase) {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".tif", ".tiff", ".heic", ".heif", ".hif",
    };


    /// <summary>Whether a file of this name may carry a rating of its own: a RAW, or a format a camera writes.</summary>
    public static bool Reads(string path) {
        return ImageFormats.IsRaw(path) || _cameraFormats.Contains(Path.GetExtension(path));
    }


    /// <summary>
    /// The rating in the photo behind <paramref name="stream"/>, or null when
    /// it says nothing: no rating and no label, a 0 - what a camera writes
    /// into every frame it was not asked to rate, so a folder of them is
    /// not a folder of rated photos - or a file it cannot make out.
    /// </summary>
    public static SidecarRating? Read(Stream stream) {
        byte[]? xmp = null;
        int? exif = null;

        byte[] head = ReadAt(stream, 0, 16);
        if (head.Length < 16) {
            return null;
        }
        if (head.AsSpan(4, 4).SequenceEqual("ftyp"u8)) {
            ReadBoxes(stream, ref xmp, ref exif);
        } else if (head[0] == 0xFF && head[1] == 0xD8) {
            ReadJpeg(stream, 0, ref xmp, ref exif);
        } else if (IsTiff(head)) {
            ReadTiff(stream, 0, ref xmp, ref exif);
        } else if (head.AsSpan().StartsWith(_raf)) {
            byte[] pointer = ReadAt(stream, 84, 4);
            if (pointer.Length == 4) {
                ReadJpeg(stream, BinaryPrimitives.ReadUInt32BigEndian(pointer), ref xmp, ref exif);
            }
        }

        var fromXmp = xmp is null ? null : XmpSidecar.Read(xmp);
        int? rank = fromXmp?.Rank ?? exif;
        bool labelled = (fromXmp?.ColorLabel ?? 0) > 0 || fromXmp?.ColorLabelName is not null;
        if (rank is not > 0 && !labelled) {
            return null;
        }

        return new SidecarRating(
            rank > 0 ? Math.Min(rank.Value, XmpSidecar.MaxRating) : null,
            labelled ? fromXmp!.ColorLabel : null,
            labelled ? fromXmp!.ColorLabelName : null,
            InPhoto: true);
    }


    /// <summary>
    /// What a row shows: the sidecar's fields over the photo's (decision
    /// 2026-10-01: a sidecar with the field overrides the camera). A
    /// sidecar's 0 is a field too - it is how the camera's stars are taken
    /// off.
    /// </summary>
    public static SidecarRating? Merge(SidecarRating? sidecar, SidecarRating? photo) {
        if (photo is null) {
            return sidecar;
        }
        if (sidecar is null) {
            return photo;
        }

        bool rank = sidecar.Rank is null && photo.Rank is not null;
        bool label = sidecar.ColorLabel is null && photo.ColorLabel is not null;
        if (!rank && !label) {
            return sidecar;
        }

        return new SidecarRating(
            rank ? photo.Rank : sidecar.Rank,
            label ? photo.ColorLabel : sidecar.ColorLabel,
            label ? photo.ColorLabelName : sidecar.ColorLabelName,
            InPhoto: true);
    }


    // --- Containers -------------------------------------------------------

    /// <summary>ISO boxes, top level: the XMP uuid box, and <c>moov</c> for Canon's EXIF.</summary>
    private static void ReadBoxes(Stream stream, ref byte[]? xmp, ref int? exif) {
        long at = 0;
        for (int i = 0; i < MaxBlocks && Box(stream, at, stream.Length) is { } box; i++) {
            if (box.Type == "uuid" && box.Uuid(_xmpUuid)) {
                xmp ??= ReadAt(stream, box.Body + 16, (int)Math.Min(box.End - box.Body - 16, MaxPacket));
            } else if (box.Type == "moov") {
                ReadCanonExif(stream, box, ref exif);
            }
            at = box.End;
        }
    }

    /// <summary><c>moov</c> → Canon's metadata uuid → <c>CMT1</c>, which is IFD0 as a TIFF of its own.</summary>
    private static void ReadCanonExif(Stream stream, BoxInfo moov, ref int? exif) {
        long at = moov.Body;
        for (int i = 0; i < MaxBlocks && Box(stream, at, moov.End) is { } box; i++) {
            if (box.Type == "uuid" && box.Uuid(_canonMetaUuid)) {
                long inner = box.Body + 16;
                for (int j = 0; j < MaxBlocks && Box(stream, inner, box.End) is { } part; j++) {
                    if (part.Type == "CMT1") {
                        byte[]? ignored = null;
                        ReadTiff(stream, part.Body, ref ignored, ref exif);

                        return;
                    }
                    inner = part.End;
                }

                return;
            }
            at = box.End;
        }
    }

    /// <summary>One box header at <paramref name="at"/>, or null past <paramref name="limit"/> or on nonsense.</summary>
    private static BoxInfo? Box(Stream stream, long at, long limit) {
        if (at + 8 > limit) {
            return null;
        }

        byte[] h = ReadAt(stream, at, 32);
        if (h.Length < 8) {
            return null;
        }
        long size = BinaryPrimitives.ReadUInt32BigEndian(h);
        int header = 8;
        if (size == 1) {
            if (h.Length < 16) {
                return null;
            }
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(h.AsSpan(8));
            header = 16;
        } else if (size == 0) {
            size = limit - at;
        }
        if (size < header || at + size > limit) {
            return null;
        }

        string type = Encoding.ASCII.GetString(h, 4, 4);
        byte[] uuid = type == "uuid" && h.Length >= header + 16 ? h[header..(header + 16)] : Array.Empty<byte>();

        return new BoxInfo(type, at + header, at + size, uuid);
    }

    /// <summary>JPEG segments up to the scan: APP1 holds both the EXIF block and the XMP packet.</summary>
    private static void ReadJpeg(Stream stream, long start, ref byte[]? xmp, ref int? exif) {
        long at = start + 2;
        for (int i = 0; i < MaxBlocks; i++) {
            byte[] h = ReadAt(stream, at, 4);
            if (h.Length < 4 || h[0] != 0xFF) {
                return;
            }
            // Fill bytes before a marker, and markers that carry no length.
            if (h[1] == 0xFF || h[1] == 0x01 || h[1] is >= 0xD0 and <= 0xD8) {
                at += h[1] == 0xFF ? 1 : 2;
                continue;
            }
            // Start of scan or end of image: what follows is the picture.
            if (h[1] == 0xDA || h[1] == 0xD9) {
                return;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(2));
            if (length < 2) {
                return;
            }
            if (h[1] == 0xE1) {
                byte[] body = ReadAt(stream, at + 4, length - 2);
                if (body.AsSpan().StartsWith(_jpegExif)) {
                    byte[]? ignored = null;
                    ReadTiff(new MemoryStream(body, _jpegExif.Length, body.Length - _jpegExif.Length), 0, ref ignored, ref exif);
                } else if (body.AsSpan().StartsWith(_jpegXmp)) {
                    xmp ??= body[_jpegXmp.Length..];
                }
            }
            at += 2 + length;
        }
    }

    /// <summary>IFD0 of a TIFF starting at <paramref name="origin"/>: the EXIF rating and the XMP packet.</summary>
    private static void ReadTiff(Stream stream, long origin, ref byte[]? xmp, ref int? exif) {
        byte[] h = ReadAt(stream, origin, 8);
        if (h.Length < 8 || h[0] != h[1] || (h[0] != 'I' && h[0] != 'M')) {
            return;
        }

        bool little = h[0] == 'I';
        long ifd = origin + U32(h, 4, little);
        byte[] countBytes = ReadAt(stream, ifd, 2);
        if (countBytes.Length < 2) {
            return;
        }
        int count = U16(countBytes, 0, little);
        if (count is 0 or > 1000) {
            return;
        }

        byte[] entries = ReadAt(stream, ifd + 2, count * 12);
        for (int i = 0; i + 12 <= entries.Length; i += 12) {
            int tag = U16(entries, i, little);
            int type = U16(entries, i + 2, little);
            uint n = U32(entries, i + 4, little);
            if (tag == TagRating && exif is null && n == 1) {
                // SHORT or LONG, held in the entry itself.
                exif = type == 3 ? U16(entries, i + 8, little) : type == 4 ? (int)Math.Min(U32(entries, i + 8, little), 5u) : null;
            } else if (tag == TagXmp && xmp is null && n is > 4 and <= MaxPacket) {
                xmp = ReadAt(stream, origin + U32(entries, i + 8, little), (int)n);
            }
        }
    }


    // --- Bytes --------------------------------------------------------------

    /// <summary>
    /// TIFF by its byte order and magic: 42, and the ones Olympus
    /// (<c>IIRO</c>, <c>IIRS</c>) and Panasonic (<c>IIU</c>) put in its place.
    /// </summary>
    private static bool IsTiff(byte[] head) {
        if (head[0] == 'M' && head[1] == 'M') {
            return head[2] == 0 && head[3] == 42;
        }

        return head[0] == 'I' && head[1] == 'I'
            && U16(head, 2, little: true) is 42 or 0x4F52 or 0x5352 or 0x0055;
    }

    /// <summary>Up to <paramref name="count"/> bytes from <paramref name="offset"/>; fewer at the end of the file, none past it.</summary>
    private static byte[] ReadAt(Stream stream, long offset, int count) {
        if (offset < 0 || count <= 0 || offset >= stream.Length) {
            return Array.Empty<byte>();
        }

        byte[] buffer = new byte[(int)Math.Min(count, stream.Length - offset)];
        stream.Position = offset;
        int read = 0;
        while (read < buffer.Length) {
            int got = stream.Read(buffer, read, buffer.Length - read);
            if (got == 0) {
                break;
            }
            read += got;
        }

        return read == buffer.Length ? buffer : buffer[..read];
    }

    private static int U16(byte[] b, int at, bool little) {
        return little ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at));
    }

    private static uint U32(byte[] b, int at, bool little) {
        return little ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at));
    }


    /// <summary>A box: its type, where its body starts, where it ends, and the uuid of a uuid box.</summary>
    private sealed record BoxInfo(string Type, long Body, long End, byte[] UuidBytes) {
        public bool Uuid(byte[] expected) {
            return UuidBytes.AsSpan().SequenceEqual(expected);
        }
    }
}
