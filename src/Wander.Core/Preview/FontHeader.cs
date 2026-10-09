using System.Buffers.Binary;
using System.Text;

namespace Wander.Core.Preview;

/// <summary>
/// What it takes to ask Windows for a font file's first face by name: the
/// family as its name table gives it to Windows (name 1), the weight and
/// whether it slants (<c>OS/2</c>, else <c>head</c>). A TrueType collection
/// (<c>ttcf</c>) is read at its first face. Null when the bytes are not a
/// TrueType or OpenType font, or name no family.
/// </summary>
/// <param name="Family">The family name, as GDI matches a face name against it.</param>
/// <param name="Weight">100-900, 400 for a regular face.</param>
/// <param name="Italic">The face is italic or oblique.</param>
public sealed record FontHeader(string Family, int Weight, bool Italic) {
    /// <summary>Past this a header table count is not a font's.</summary>
    private const int MaxTables = 512;

    private const ushort NameFamily = 1;
    private const ushort PlatformUnicode = 0;
    private const ushort PlatformMac = 1;
    private const ushort PlatformWindows = 3;
    private const ushort LanguageEnglishUs = 0x0409;


    public static FontHeader? Read(Stream stream) {
        try {
            return ReadOrThrow(stream);
        } catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException or NotSupportedException) {
            return null;
        }
    }


    private static FontHeader? ReadOrThrow(Stream stream) {
        long face = 0;
        byte[] head = Bytes(stream, 0, 12);
        if (Ascii(head, "ttcf")) {
            face = BinaryPrimitives.ReadUInt32BigEndian(Bytes(stream, 12, 4));
            head = Bytes(stream, face, 12);
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(head);
        if (version != 0x00010000 && !Ascii(head, "OTTO") && !Ascii(head, "true")) {
            return null;
        }
        int tables = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(4));
        if (tables > MaxTables) {
            return null;
        }

        byte[] records = Bytes(stream, face + 12, tables * 16);
        long nameAt = -1;
        long os2At = -1;
        long headAt = -1;
        int os2Length = 0;
        for (int i = 0; i < tables; i++) {
            var record = records.AsSpan(i * 16, 16);
            long offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            switch (Encoding.ASCII.GetString(record[..4])) {
                case "name":
                    nameAt = offset;
                    break;
                case "OS/2":
                    os2At = offset;
                    os2Length = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(record[12..]), 64);
                    break;
                case "head":
                    headAt = offset;
                    break;
            }
        }

        string? family = nameAt >= 0 ? FamilyName(stream, nameAt) : null;
        if (string.IsNullOrWhiteSpace(family)) {
            return null;
        }

        int weight = 400;
        bool italic = false;
        if (os2At >= 0 && os2Length >= 64) {
            byte[] os2 = Bytes(stream, os2At, 64);
            weight = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(4));
            italic = (BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(62)) & 0x0001) != 0;
        } else if (headAt >= 0) {
            int macStyle = BinaryPrimitives.ReadUInt16BigEndian(Bytes(stream, headAt + 44, 2));
            weight = (macStyle & 0x0001) != 0 ? 700 : 400;
            italic = (macStyle & 0x0002) != 0;
        }
        if (weight is < 1 or > 1000) {
            weight = 400;
        }

        return new FontHeader(family.Trim(), weight, italic);
    }

    /// <summary>
    /// Name 1 in the record Windows reads: its own platform in US English,
    /// then its own in any language, then Unicode, then Mac Roman.
    /// </summary>
    private static string? FamilyName(Stream stream, long table) {
        byte[] head = Bytes(stream, table, 6);
        int count = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(2));
        long strings = table + BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(4));
        byte[] records = Bytes(stream, table + 6, count * 12);

        int best = -1;
        int bestRank = int.MaxValue;
        for (int i = 0; i < count; i++) {
            var record = records.AsSpan(i * 12, 12);
            if (BinaryPrimitives.ReadUInt16BigEndian(record[6..]) != NameFamily) {
                continue;
            }

            ushort platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            ushort encoding = BinaryPrimitives.ReadUInt16BigEndian(record[2..]);
            ushort language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            int rank = platform switch {
                PlatformWindows => language == LanguageEnglishUs ? 0 : 1,
                PlatformUnicode => 2,
                PlatformMac when encoding == 0 => 3,
                _ => int.MaxValue,
            };
            if (rank < bestRank) {
                best = i;
                bestRank = rank;
            }
        }
        if (best < 0) {
            return null;
        }

        var chosen = records.AsSpan(best * 12, 12);
        int length = BinaryPrimitives.ReadUInt16BigEndian(chosen[8..]);
        int offset = BinaryPrimitives.ReadUInt16BigEndian(chosen[10..]);
        byte[] text = Bytes(stream, strings + offset, length);

        return bestRank == 3 ? Encoding.Latin1.GetString(text) : Encoding.BigEndianUnicode.GetString(text);
    }

    private static byte[] Bytes(Stream stream, long offset, int count) {
        if (offset < 0 || offset + count > stream.Length) {
            throw new EndOfStreamException();
        }

        var buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);

        return buffer;
    }

    private static bool Ascii(ReadOnlySpan<byte> bytes, string tag) {
        return bytes.Length >= tag.Length && Encoding.ASCII.GetString(bytes[..tag.Length]) == tag;
    }
}
