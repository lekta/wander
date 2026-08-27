using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class EncodingProbeTests {
    private const string Russian =
        "Привет, мир! Это обычный русский текст, набранный в старом редакторе " +
        "и сохранённый однобайтовой кодировкой. Проверяем, что определение " +
        "работает и на прописных, и на строчных буквах.";


    /// <summary>
    /// The two codepage tables are written out in Core (see
    /// <see cref="EncodingProbe"/> for why), so the tests encode with them
    /// rather than with <c>Encoding.GetEncoding</c> — which in .NET Core
    /// does not know these codepages without an extra package.
    /// </summary>
    private static byte[] Encode(string text, bool dos) {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++) {
            bytes[i] = (byte)(text[i] < 0x80 ? text[i] : Map(text[i], dos));
        }

        return bytes;
    }

    private static int Map(char c, bool dos) {
        // Only the letters the fixtures use — enough to build a sample.
        if (dos) {
            if (c is >= 'А' and <= 'П') { return 0x80 + (c - 'А'); }
            if (c is >= 'Р' and <= 'Я') { return 0x90 + (c - 'Р'); }
            if (c is >= 'а' and <= 'п') { return 0xA0 + (c - 'а'); }
            if (c is >= 'р' and <= 'я') { return 0xE0 + (c - 'р'); }
            if (c == 'Ё') { return 0xF0; }
            if (c == 'ё') { return 0xF1; }
        } else {
            if (c is >= 'А' and <= 'я') { return 0xC0 + (c - 'А'); }
            if (c == 'Ё') { return 0xA8; }
            if (c == 'ё') { return 0xB8; }
        }

        throw new ArgumentOutOfRangeException(nameof(c), c, "not in the fixture alphabet");
    }


    // --- Detect --------------------------------------------------------

    [Fact]
    public void PlainAscii_IsUtf8() {
        Assert.Equal(TextEncodingKind.Utf8, EncodingProbe.Detect("hello world"u8));
    }


    [Fact]
    public void Utf8WithoutBom_IsRecognisedByItsSequences() {
        Assert.Equal(TextEncodingKind.Utf8, EncodingProbe.Detect(Encoding.UTF8.GetBytes(Russian)));
    }


    [Fact]
    public void Utf8WithBom_IsUtf8() {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Russian)).ToArray();

        Assert.Equal(TextEncodingKind.Utf8, EncodingProbe.Detect(bytes));
    }


    [Fact]
    public void Utf16Boms_AreRecognised() {
        Assert.Equal(
            TextEncodingKind.Utf16LittleEndian,
            EncodingProbe.Detect(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hi")).ToArray()));
        Assert.Equal(
            TextEncodingKind.Utf16BigEndian,
            EncodingProbe.Detect(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("hi")).ToArray()));
    }


    [Fact]
    public void Windows1251_IsToldFromDos866() {
        Assert.Equal(TextEncodingKind.Windows1251, EncodingProbe.Detect(Encode(Russian, dos: false)));
    }


    [Fact]
    public void Dos866_IsToldFromWindows1251() {
        Assert.Equal(TextEncodingKind.Dos866, EncodingProbe.Detect(Encode(Russian, dos: true)));
    }


    /// <summary>
    /// The floor under the Cyrillic count: <c>ä ö ü</c> are perfectly good
    /// Cyrillic letters in 1251, and without a minimum a German file would
    /// be "detected" as Russian and mangled.
    /// </summary>
    [Fact]
    public void WesternTextWithAFewAccents_IsNotCalledCyrillic() {
        var bytes = Encoding.Latin1.GetBytes("Grüße aus München, schöne Straße.");

        Assert.Equal(TextEncodingKind.Latin1, EncodingProbe.Detect(bytes));
    }


    /// <summary>
    /// The case the floor must not break: a source file that is ASCII apart
    /// from one Russian comment. That comment is the only part a wrong
    /// guess can damage, so it has to be enough to decide on.
    /// </summary>
    [Fact]
    public void AsciiFileWithOneRussianComment_IsDetected() {
        var bytes = Encoding.ASCII.GetBytes("rem ")
            .Concat(Encode("собираем проект в релизной конфигурации", dos: true))
            .Concat(Encoding.ASCII.GetBytes("\r\ndotnet build -c Release\r\n"))
            .ToArray();

        Assert.Equal(TextEncodingKind.Dos866, EncodingProbe.Detect(bytes));
    }


    /// <summary>
    /// A UTF-8 file longer than the sample can have a multi-byte character
    /// straddling the cut. Reading that as a defect would send every long
    /// UTF-8 file down the codepage path.
    /// </summary>
    [Fact]
    public void LongUtf8File_IsNotBrokenByTheSampleBoundary() {
        var text = new StringBuilder();
        while (Encoding.UTF8.GetByteCount(text.ToString()) < EncodingProbe.SampleSize + 100) {
            text.Append("ы");
        }

        Assert.Equal(TextEncodingKind.Utf8, EncodingProbe.Detect(Encoding.UTF8.GetBytes(text.ToString())));
    }


    [Fact]
    public void EmptyInput_IsUtf8() {
        Assert.Equal(TextEncodingKind.Utf8, EncodingProbe.Detect(ReadOnlySpan<byte>.Empty));
    }


    // --- Decode --------------------------------------------------------

    [Fact]
    public void Decode_ReadsWindows1251BackAsWritten() {
        Assert.Equal(Russian, EncodingProbe.Decode(Encode(Russian, dos: false)));
    }


    [Fact]
    public void Decode_ReadsDos866BackAsWritten() {
        Assert.Equal(Russian, EncodingProbe.Decode(Encode(Russian, dos: true)));
    }


    [Fact]
    public void Decode_ReadsUtf8BackAsWritten() {
        Assert.Equal(Russian, EncodingProbe.Decode(Encoding.UTF8.GetBytes(Russian)));
    }


    /// <summary>The mark itself must not survive into the text as an invisible first character.</summary>
    [Fact]
    public void Decode_SwallowsTheByteOrderMark() {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("текст")).ToArray();

        Assert.Equal("текст", EncodingProbe.Decode(bytes));
    }


    [Fact]
    public void Decode_ReadsUtf16BackAsWritten() {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Russian)).ToArray();

        Assert.Equal(Russian, EncodingProbe.Decode(bytes));
    }


    /// <summary>
    /// The symptom this whole thing exists to remove: a codepaged file read
    /// as UTF-8 comes out as a wall of replacement characters.
    /// </summary>
    [Fact]
    public void Decode_LeavesNoReplacementCharacters() {
        string decoded = EncodingProbe.Decode(Encode(Russian, dos: true));

        Assert.DoesNotContain('�', decoded);
    }
}
