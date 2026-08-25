using System.Text;

namespace Wander.Core.Companions;

/// <summary>
/// The byte-level half of editing somebody else's text file: decode, hand
/// the caller a string, encode back exactly as the file was.
///
/// <para>
/// Both sidecar writers (<see cref="Pp3Sidecar"/>, <see cref="XmpSidecar"/>)
/// need this and need it to be conservative in the same way, so it lives in
/// one place. The rules:
/// </para>
/// <list type="bullet">
///   <item>the file travels as bytes — the encoding it was written in is
///   the encoding it is written back in, BOM included;</item>
///   <item>line endings are never normalised. Splitting on <c>\n</c> and
///   leaving each <c>\r</c> glued to the end of its line means re-joining
///   reproduces the original exactly, mixed endings and all.</item>
/// </list>
/// </summary>
internal static class SidecarText {
    /// <summary>Decode, transform, encode — the whole round trip in one call.</summary>
    public static byte[] Edit(byte[] content, Func<string, string> transform) {
        string text = Decode(content, out var encoding, out bool hasBom);

        return Encode(transform(text), encoding, hasBom);
    }


    public static string Decode(byte[] content, out Encoding encoding, out bool hasBom) {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) {
            encoding = new UTF8Encoding(true);
            hasBom = true;

            return new UTF8Encoding(false).GetString(content, 3, content.Length - 3);
        }
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE) {
            encoding = new UnicodeEncoding(false, true);
            hasBom = true;

            return new UnicodeEncoding(false, false).GetString(content, 2, content.Length - 2);
        }
        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF) {
            encoding = new UnicodeEncoding(true, true);
            hasBom = true;

            return new UnicodeEncoding(true, false).GetString(content, 2, content.Length - 2);
        }

        // Both formats are UTF-8 by specification when they say nothing.
        encoding = new UTF8Encoding(false);
        hasBom = false;

        return encoding.GetString(content);
    }


    public static byte[] Encode(string text, Encoding encoding, bool hasBom) {
        byte[] body = encoding.GetBytes(text);
        if (!hasBom) {
            return body;
        }

        byte[] preamble = encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);

        return result;
    }


    /// <summary>The newline this text uses, for the rare case where a line has to be added.</summary>
    public static string NewlineOf(string text) {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }
}
