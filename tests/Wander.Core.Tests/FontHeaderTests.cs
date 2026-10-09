using System.Buffers.Binary;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>A font file's family, weight and slant, as Windows is asked for the face by them.</summary>
public class FontHeaderTests {
    private const ushort NameFamily = 1;


    [Fact]
    public void Family_IsTheWindowsEnglishName() {
        byte[] font = Font(
            Name(1, 0, 0, NameFamily, Encoding.Latin1.GetBytes("Mac Name")),
            Name(3, 1, 0x0419, NameFamily, Utf16("Русское")),
            Name(3, 1, 0x0409, NameFamily, Utf16("Test Sans")),
            Name(3, 1, 0x0409, 2, Utf16("Regular")));

        var header = FontHeader.Read(new MemoryStream(font));

        Assert.Equal(new FontHeader("Test Sans", 400, false), header);
    }

    [Fact]
    public void Family_FallsBackToAnyWindowsLanguage_ThenMac() {
        byte[] russian = Font(Name(1, 0, 0, NameFamily, Encoding.Latin1.GetBytes("Mac Name")), Name(3, 1, 0x0419, NameFamily, Utf16("Русское")));
        byte[] mac = Font(Name(1, 0, 0, NameFamily, Encoding.Latin1.GetBytes("Mac Name")));

        Assert.Equal("Русское", FontHeader.Read(new MemoryStream(russian))?.Family);
        Assert.Equal("Mac Name", FontHeader.Read(new MemoryStream(mac))?.Family);
    }

    [Fact]
    public void WeightAndItalic_ComeFromOs2() {
        byte[] font = Font(os2: Os2(weight: 700, italic: true), names: Name(3, 1, 0x0409, NameFamily, Utf16("Test Sans")));

        Assert.Equal(new FontHeader("Test Sans", 700, true), FontHeader.Read(new MemoryStream(font)));
    }

    [Fact]
    public void Collection_IsReadAtItsFirstFace() {
        byte[] face = Font(Name(3, 1, 0x0409, NameFamily, Utf16("Collected")));
        byte[] collection = Collection(face);

        Assert.Equal("Collected", FontHeader.Read(new MemoryStream(collection))?.Family);
    }

    [Fact]
    public void NotAFont_IsNull() {
        Assert.Null(FontHeader.Read(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nhello world"))));
        Assert.Null(FontHeader.Read(new MemoryStream(new byte[] { 0, 1, 0, 0, 0, 1 })));
    }

    [Fact]
    public void FontWithoutFamily_IsNull() {
        byte[] font = Font(Name(3, 1, 0x0409, 2, Utf16("Regular")));

        Assert.Null(FontHeader.Read(new MemoryStream(font)));
    }


    private static NameRecord Name(ushort platform, ushort encoding, ushort language, ushort id, byte[] text) {
        return new NameRecord(platform, encoding, language, id, text);
    }

    private static byte[] Utf16(string text) {
        return Encoding.BigEndianUnicode.GetBytes(text);
    }

    private static byte[] Font(params NameRecord[] names) {
        return Font(null, names);
    }

    /// <summary>A TrueType file with a name table and, when given, an OS/2 one.</summary>
    private static byte[] Font(byte[]? os2, params NameRecord[] names) {
        var tables = new List<(string Tag, byte[] Data)> { ("name", NameTable(names)) };
        if (os2 is not null) {
            tables.Add(("OS/2", os2));
        }

        int offset = 12 + (tables.Count * 16);
        var file = new byte[offset + tables.Sum(t => t.Data.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(file, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), (ushort)tables.Count);
        for (int i = 0; i < tables.Count; i++) {
            var record = file.AsSpan(12 + (i * 16));
            Encoding.ASCII.GetBytes(tables[i].Tag).CopyTo(record);
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)tables[i].Data.Length);
            tables[i].Data.CopyTo(file, offset);
            offset += tables[i].Data.Length;
        }

        return file;
    }

    private static byte[] NameTable(NameRecord[] names) {
        int stringsAt = 6 + (names.Length * 12);
        var table = new byte[stringsAt + names.Sum(n => n.Text.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)names.Length);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)stringsAt);
        int text = 0;
        for (int i = 0; i < names.Length; i++) {
            var record = table.AsSpan(6 + (i * 12));
            BinaryPrimitives.WriteUInt16BigEndian(record, names[i].Platform);
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], names[i].Encoding);
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], names[i].Language);
            BinaryPrimitives.WriteUInt16BigEndian(record[6..], names[i].Id);
            BinaryPrimitives.WriteUInt16BigEndian(record[8..], (ushort)names[i].Text.Length);
            BinaryPrimitives.WriteUInt16BigEndian(record[10..], (ushort)text);
            names[i].Text.CopyTo(table, stringsAt + text);
            text += names[i].Text.Length;
        }

        return table;
    }

    private static byte[] Os2(ushort weight, bool italic) {
        var table = new byte[78];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), weight);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(62), (ushort)(italic ? 1 : 0x40));

        return table;
    }

    /// <summary>A collection of one face: the ttcf header, then the face moved past it.</summary>
    private static byte[] Collection(byte[] face) {
        const int header = 16;
        var file = new byte[header + face.Length];
        Encoding.ASCII.GetBytes("ttcf").CopyTo(file, 0);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(12), header);
        face.CopyTo(file, header);
        // Table offsets in a collection count from the start of the file.
        int tables = BinaryPrimitives.ReadUInt16BigEndian(face.AsSpan(4));
        for (int i = 0; i < tables; i++) {
            var offset = file.AsSpan(header + 12 + (i * 16) + 8);
            BinaryPrimitives.WriteUInt32BigEndian(offset, BinaryPrimitives.ReadUInt32BigEndian(offset) + header);
        }

        return file;
    }


    private sealed record NameRecord(ushort Platform, ushort Encoding, ushort Language, ushort Id, byte[] Text);
}
