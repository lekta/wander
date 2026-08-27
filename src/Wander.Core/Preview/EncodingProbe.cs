using System.Text;

namespace Wander.Core.Preview;

/// <summary>Which encoding a text file turned out to be in.</summary>
public enum TextEncodingKind {
    Utf8,
    Utf16LittleEndian,
    Utf16BigEndian,
    /// <summary>Windows-1251 — the Cyrillic codepage of Windows itself.</summary>
    Windows1251,
    /// <summary>Codepage 866 — the Cyrillic codepage of DOS, still in .bat files and old notes.</summary>
    Dos866,
    /// <summary>Latin-1 — the byte-preserving fallback when nothing else fits.</summary>
    Latin1,
}


/// <summary>
/// Works out what encoding a text file is in, then decodes it.
///
/// <para>
/// The default decode assumes UTF-8, which turns every byte it does not
/// understand into <c>U+FFFD</c> — the black diamond with a question mark
/// in it. A folder of old notes or a <c>.bat</c> written in DOS days is
/// then a wall of those, and the file looks corrupt when it is merely
/// codepaged.
/// </para>
///
/// <para>
/// The order is: byte-order mark, then strict UTF-8 (invalid sequences are
/// what rule it out), then the single-byte codepages by scoring. Only two
/// Cyrillic codepages are candidates — 1251 and 866 — because those are the
/// two that actually turn up, and every extra candidate makes the scoring
/// worse at telling the useful ones apart.
/// </para>
///
/// <para>
/// The tables are written out here rather than taken from
/// <see cref="Encoding.GetEncoding(int)"/>: .NET ships only Unicode, ASCII
/// and Latin-1, and everything else needs the
/// <c>System.Text.Encoding.CodePages</c> package. Two hundred and fifty six
/// characters of data are cheaper than a dependency in
/// <c>Wander.Core</c>, which has none.
/// </para>
/// </summary>
public static class EncodingProbe {
    /// <summary>Bytes to look at when guessing. The rest of the file is decoded with the answer.</summary>
    public const int SampleSize = 8192;

    /// <summary>
    /// Cyrillic letters a candidate must produce before it is believed.
    /// Without a floor, a German file with three umlauts scores as Cyrillic
    /// (<c>ä ö ü</c> are Cyrillic letters in 1251) and gets mangled. With
    /// it, a source file carrying one Russian comment is still detected —
    /// which is the case that matters, because that comment is the only
    /// part of the file the guess can get wrong.
    /// </summary>
    private const int MinCyrillicLetters = 8;

    /// <summary>0x80…0xFF in Windows-1251. Index 0 is byte 0x80.</summary>
    private const string Cp1251 =
        "ЂЃ‚ѓ„…†‡€‰Љ‹ЊЌЋЏ" +
        "ђ‘’“”•–—�™љ›њќћџ" +
        " ЎўЈ¤Ґ¦§Ё©Є«¬­®Ї" +
        "°±Ііґµ¶·ё№є»јЅѕї" +
        "АБВГДЕЖЗИЙКЛМНОП" +
        "РСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдежзийклмноп" +
        "рстуфхцчшщъыьэюя";

    /// <summary>0x80…0xFF in codepage 866.</summary>
    private const string Cp866 =
        "АБВГДЕЖЗИЙКЛМНОП" +
        "РСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдежзийклмноп" +
        "░▒▓│┤╡╢╖╕╣║╗╝╜╛┐" +
        "└┴┬├─┼╞╟╚╔╩╦╠═╬╧" +
        "╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀" +
        "рстуфхцчшщъыьэюя" +
        "ЁёЄєЇїЎў°∙·√№¤■ ";


    /// <summary>
    /// Decodes the whole buffer, guessing the encoding from its start. Any
    /// byte-order mark is consumed rather than left in the text as an
    /// invisible first character.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes) {
        var kind = Detect(bytes);

        return kind switch {
            TextEncodingKind.Utf8 => new UTF8Encoding(false).GetString(StripBom(bytes, kind)),
            TextEncodingKind.Utf16LittleEndian => Encoding.Unicode.GetString(StripBom(bytes, kind)),
            TextEncodingKind.Utf16BigEndian => Encoding.BigEndianUnicode.GetString(StripBom(bytes, kind)),
            TextEncodingKind.Windows1251 => DecodeSingleByte(bytes, Cp1251),
            TextEncodingKind.Dos866 => DecodeSingleByte(bytes, Cp866),
            _ => Encoding.Latin1.GetString(bytes),
        };
    }


    /// <summary>
    /// The encoding the bytes look like. Only the first
    /// <see cref="SampleSize"/> bytes are examined: an encoding does not
    /// change halfway through a file, and reading all of a large one to
    /// answer would cost more than the decode it is deciding.
    /// </summary>
    public static TextEncodingKind Detect(ReadOnlySpan<byte> bytes) {
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF)) {
            return TextEncodingKind.Utf8;
        }
        if (StartsWith(bytes, 0xFF, 0xFE)) {
            return TextEncodingKind.Utf16LittleEndian;
        }
        if (StartsWith(bytes, 0xFE, 0xFF)) {
            return TextEncodingKind.Utf16BigEndian;
        }

        var sample = bytes.Length > SampleSize ? bytes[..SampleSize] : bytes;
        if (IsValidUtf8(TrimPartialUtf8(sample, truncated: bytes.Length > SampleSize))) {
            // Covers plain ASCII too — same bytes, same result, and calling
            // it UTF-8 keeps one fewer case in the enum.
            return TextEncodingKind.Utf8;
        }

        int windows = Score(sample, Cp1251, out int windowsCyrillic);
        int dos = Score(sample, Cp866, out int dosCyrillic);

        if (windows >= dos && windowsCyrillic >= MinCyrillicLetters) {
            return TextEncodingKind.Windows1251;
        }
        if (dos > windows && dosCyrillic >= MinCyrillicLetters) {
            return TextEncodingKind.Dos866;
        }

        return TextEncodingKind.Latin1;
    }


    // --- Scoring -------------------------------------------------------

    /// <summary>
    /// How much the sample looks like Cyrillic text under one codepage.
    /// Lower case counts for more than upper: real prose is nine parts
    /// lower case, and that ratio is what separates 1251 from 866 — the
    /// bytes one of them reads as lower-case letters, the other reads as
    /// box drawing.
    /// </summary>
    private static int Score(ReadOnlySpan<byte> sample, string table, out int cyrillicLetters) {
        int score = 0;
        cyrillicLetters = 0;

        foreach (byte b in sample) {
            if (b < 0x80) {
                continue;
            }

            char c = table[b - 0x80];
            if (IsCyrillicLower(c)) {
                score += 3;
                cyrillicLetters++;
            } else if (IsCyrillicUpper(c)) {
                score += 1;
                cyrillicLetters++;
            } else {
                // Box drawing, currency signs, maths — legal characters
                // that prose does not contain by the hundred.
                score -= 2;
            }
        }

        return score;
    }

    private static bool IsCyrillicLower(char c) {
        return c is >= 'а' and <= 'я' or 'ё' or 'є' or 'і' or 'ї' or 'ў' or 'ђ' or 'љ' or 'њ';
    }

    private static bool IsCyrillicUpper(char c) {
        return c is >= 'А' and <= 'Я' or 'Ё' or 'Є' or 'І' or 'Ї' or 'Ў' or 'Ђ' or 'Љ' or 'Њ';
    }


    // --- UTF-8 ---------------------------------------------------------

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes) {
        try {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

            return true;
        } catch (DecoderFallbackException) {
            return false;
        }
    }

    /// <summary>
    /// Drops a multi-byte sequence cut in half by the end of the sample.
    /// Without this, every UTF-8 file longer than the sample has a coin
    /// flip's chance of being declared invalid on its last three bytes.
    /// Only applies when the sample really was cut short — at the true end
    /// of a file, a dangling sequence is a genuine defect.
    /// </summary>
    private static ReadOnlySpan<byte> TrimPartialUtf8(ReadOnlySpan<byte> sample, bool truncated) {
        if (!truncated) {
            return sample;
        }

        // A sequence is at most four bytes, so at most three trailing ones
        // can belong to an unfinished character.
        for (int i = sample.Length - 1; i >= 0 && i >= sample.Length - 3; i--) {
            byte b = sample[i];
            if ((b & 0xC0) == 0x80) {
                continue;       // continuation byte — keep walking back
            }

            return (b & 0x80) == 0 ? sample : sample[..i];
        }

        return sample;
    }


    // --- Small helpers -------------------------------------------------

    private static string DecodeSingleByte(ReadOnlySpan<byte> bytes, string table) {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) {
            byte b = bytes[i];
            chars[i] = b < 0x80 ? (char)b : table[b - 0x80];
        }

        return new string(chars);
    }

    private static ReadOnlySpan<byte> StripBom(ReadOnlySpan<byte> bytes, TextEncodingKind kind) {
        return kind switch {
            TextEncodingKind.Utf8 when StartsWith(bytes, 0xEF, 0xBB, 0xBF) => bytes[3..],
            TextEncodingKind.Utf16LittleEndian when StartsWith(bytes, 0xFF, 0xFE) => bytes[2..],
            TextEncodingKind.Utf16BigEndian when StartsWith(bytes, 0xFE, 0xFF) => bytes[2..],
            _ => bytes,
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, params byte[] prefix) {
        return bytes.Length >= prefix.Length && bytes[..prefix.Length].SequenceEqual(prefix);
    }
}
