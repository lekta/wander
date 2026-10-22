using System.Buffers.Binary;
using System.Text;

namespace Wander.Core.Preview;

/// <summary>
/// What a file is by its first bytes, as the extension it should have had:
/// for a file whose extension says nothing the pane knows, has none, or
/// promised a format the file is not. Signatures, not guesses - a format
/// without one (TGA, binary STL, RAW past their TIFF header) is not named
/// here; text is sorted into markup, JSON and RTF, and plain text is left
/// to the caller (<see cref="TextProbe"/>).
/// </summary>
public static class ContentSniffer {
    /// <summary>How much of a file is read to name it: a zip's first entries, a text's opening.</summary>
    public const int HeadBytes = 64 * 1024;


    /// <summary>The extension, with its dot, of the format the bytes start; null when none is recognised.</summary>
    public static string? Extension(ReadOnlySpan<byte> head) {
        if (head.Length < 4) {
            return null;
        }

        return Binary(head) ?? Markup(head);
    }

    /// <summary>
    /// A WebP with the animation flag of its extended header (<c>VP8X</c>,
    /// the flags byte right after the chunk size). A simple WebP - lossy
    /// <c>VP8 </c> or lossless <c>VP8L</c> - is a single frame by its format.
    /// </summary>
    public static bool IsAnimatedWebp(ReadOnlySpan<byte> head) {
        const byte animationFlag = 0x02;

        return head.Length >= 21 && Ascii(head, 0, "RIFF") && Ascii(head, 8, "WEBP") && Ascii(head, 12, "VP8X")
            && (head[20] & animationFlag) != 0;
    }

    /// <summary>
    /// A GIF with a second frame. The format has no flag for it: the blocks
    /// are walked - colour tables and the length-prefixed sub-blocks of
    /// extensions and pictures skipped - up to the second image descriptor
    /// or the trailer. A file that is not a GIF is a still; one that breaks
    /// off after its first picture, too.
    /// </summary>
    public static bool IsAnimatedGif(Stream stream) {
        const int imageDescriptor = 0x2C, extension = 0x21;

        var header = new byte[13];
        stream.Position = 0;
        if (stream.Read(header, 0, header.Length) < header.Length || !Ascii(header, 0, "GIF8")) {
            return false;
        }

        SkipColourTable(stream, header[10]);
        int frames = 0;
        while (true) {
            int block = stream.ReadByte();
            if (block == extension) {
                stream.ReadByte();                  // label
            } else if (block == imageDescriptor) {
                if (++frames > 1) {
                    return true;
                }
                var descriptor = new byte[9];
                if (stream.Read(descriptor, 0, descriptor.Length) < descriptor.Length) {
                    return false;
                }
                SkipColourTable(stream, descriptor[8]);
                stream.ReadByte();                  // LZW minimum code size
            } else {
                return false;                       // the trailer, the end, or not a block
            }

            for (int size = stream.ReadByte(); size > 0; size = stream.ReadByte()) {
                stream.Seek(size, SeekOrigin.Current);
            }
        }
    }


    private static string? Binary(ReadOnlySpan<byte> h) {
        if (Starts(h, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) {
            return ".png";
        }
        if (Starts(h, 0xFF, 0xD8, 0xFF)) {
            return ".jpg";
        }
        if (Ascii(h, 0, "GIF87a") || Ascii(h, 0, "GIF89a")) {
            return ".gif";
        }
        if (Ascii(h, 0, "8BPS")) {
            return ".psd";
        }
        if (Starts(h, 0x49, 0x49, 0x2A, 0x00) || Starts(h, 0x4D, 0x4D, 0x00, 0x2A)) {
            return ".tif";
        }
        if (Ascii(h, 0, "BM") && h.Length >= 18 && BinaryPrimitives.ReadUInt32LittleEndian(h[14..]) is 12 or 40 or 52 or 56 or 108 or 124) {
            return ".bmp";
        }
        if (Ascii(h, 0, "RIFF") && h.Length >= 12) {
            return Ascii(h, 8, "WEBP") ? ".webp" : Ascii(h, 8, "AVI ") ? ".avi" : Ascii(h, 8, "WAVE") ? ".wav" : null;
        }
        if (h.Length >= 12 && Ascii(h, 4, "ftyp")) {
            string brand = Encoding.ASCII.GetString(h.Slice(8, 4));

            return brand switch {
                "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1" => ".heic",
                "avif" or "avis" => ".avif",
                "crx " => ".cr3",
                "qt  " => ".mov",
                "M4A " or "M4B " => ".m4a",
                _ => ".mp4",
            };
        }
        if (h.Length >= 8 && (Ascii(h, 4, "moov") || Ascii(h, 4, "mdat") || Ascii(h, 4, "wide"))) {
            return ".mov";
        }
        if (Starts(h, 0x1A, 0x45, 0xDF, 0xA3)) {
            return ".mkv";
        }
        if (Ascii(h, 0, "OggS")) {
            return Contains(h, "\u0080theora") ? ".ogv" : Contains(h, "OpusHead") ? ".opus" : ".ogg";
        }
        if (Ascii(h, 0, "fLaC")) {
            return ".flac";
        }
        if (Ascii(h, 0, "ID3")) {
            return ".mp3";
        }
        if (Ascii(h, 0, "%PDF-")) {
            return ".pdf";
        }
        if (Ascii(h, 0, "PK") && h[2] == 3 && h[3] == 4) {
            return Zip(h);
        }
        if (Starts(h, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C)) {
            return ".7z";
        }
        if (Ascii(h, 0, "Rar!")) {
            return ".rar";
        }
        if (Ascii(h, 0, "glTF")) {
            return ".glb";
        }
        if (Ascii(h, 0, "Kaydara FBX Binary")) {
            return ".fbx";
        }
        if (Ascii(h, 0, "ply\n") || Ascii(h, 0, "ply\r\n")) {
            return ".ply";
        }
        if (Ascii(h, 0, "MZ") && h.Length >= 64) {
            int pe = BinaryPrimitives.ReadInt32LittleEndian(h[60..]);

            return pe > 0 && pe + 4 <= h.Length && Ascii(h, pe, "PE\0\0") ? ".exe" : null;
        }
        if (Starts(h, 0x00, 0x01, 0x00, 0x00) && h.Length >= 12 && BinaryPrimitives.ReadUInt16BigEndian(h[4..]) is > 0 and < 100) {
            return ".ttf";
        }
        if (Ascii(h, 0, "OTTO")) {
            return ".otf";
        }
        if (Ascii(h, 0, "ttcf")) {
            return ".ttc";
        }

        return null;
    }


    /// <summary>
    /// A zip by what it carries: the stored <c>mimetype</c> of OpenDocument
    /// and EPUB, the part folders of Office, the model part of 3MF.
    /// </summary>
    private static string Zip(ReadOnlySpan<byte> h) {
        if (Contains(h, "mimetypeapplication/epub+zip")) {
            return ".epub";
        }
        if (Contains(h, "mimetypeapplication/vnd.oasis.opendocument.text")) {
            return ".odt";
        }
        if (Contains(h, "mimetypeapplication/vnd.oasis.opendocument.spreadsheet")) {
            return ".ods";
        }
        if (Contains(h, "mimetypeapplication/vnd.oasis.opendocument.presentation")) {
            return ".odp";
        }
        if (Contains(h, "3D/3dmodel.model")) {
            return ".3mf";
        }
        if (Contains(h, "[Content_Types].xml") || Contains(h, "_rels/.rels")) {
            if (Contains(h, "word/")) {
                return ".docx";
            }
            if (Contains(h, "xl/")) {
                return ".xlsx";
            }
            if (Contains(h, "ppt/")) {
                return ".pptx";
            }
        }

        return ".zip";
    }


    /// <summary>Text that says what it is: markup, JSON, RTF. Plain text is not named.</summary>
    private static string? Markup(ReadOnlySpan<byte> h) {
        if (h.IndexOf((byte)0) >= 0 && !(h.Length >= 2 && h[0] == 0xFF && h[1] == 0xFE)) {
            return null;
        }

        string text = EncodingProbe.Decode(h.Length > 4096 ? h[..4096] : h).TrimStart();
        if (text.StartsWith("{\\rtf", StringComparison.Ordinal)) {
            return ".rtf";
        }
        if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)) {
            return text.Contains("<svg", StringComparison.OrdinalIgnoreCase) ? ".svg" : ".xml";
        }
        if (text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)) {
            return ".svg";
        }
        if (text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) {
            return ".html";
        }
        if (text.Length > 1 && text.Contains('"')
            && (text[0] == '{' && text.Contains(':') || text[0] == '[' && OpensJsonArray(text))) {
            return ".json";
        }

        return null;
    }


    /// <summary>
    /// The array goes on with an object, a string or an array: an INI file's
    /// <c>[section]</c> with a quote further on is not JSON.
    /// </summary>
    private static bool OpensJsonArray(string text) {
        string rest = text[1..].TrimStart();

        return rest.Length > 0 && rest[0] is '{' or '"' or '[';
    }


    private static bool Starts(ReadOnlySpan<byte> h, params byte[] signature) {
        return h.Length >= signature.Length && h[..signature.Length].SequenceEqual(signature);
    }

    /// <summary>Steps over the colour table a GIF's packed byte announces: 3 * 2^(n+1) bytes when its top bit is set.</summary>
    private static void SkipColourTable(Stream stream, byte packed) {
        if ((packed & 0x80) != 0) {
            stream.Seek(3 << ((packed & 0x07) + 1), SeekOrigin.Current);
        }
    }

    private static bool Ascii(ReadOnlySpan<byte> h, int at, string text) {
        if (at < 0 || at + text.Length > h.Length) {
            return false;
        }

        for (int i = 0; i < text.Length; i++) {
            if (h[at + i] != (byte)text[i]) {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(ReadOnlySpan<byte> h, string text) {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++) {
            bytes[i] = (byte)text[i];
        }

        return h.IndexOf(bytes) >= 0;
    }
}
