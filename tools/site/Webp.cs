using System.Text;

namespace Wander.Site;

/// <summary>
/// The pixel size of a WebP file, read from its header: every picture on the
/// site gets width and height, so the page does not jump when it arrives. The
/// three chunk kinds libwebp writes: lossy (VP8), lossless (VP8L), extended
/// (VP8X). The generator converts nothing - the files are made by hand.
/// </summary>
internal static class Webp {
    public static (int Width, int Height)? Size(string path) {
        byte[] b = new byte[30];
        using (var stream = File.OpenRead(path)) {
            if (stream.ReadAtLeast(b, b.Length, throwOnEndOfStream: false) < b.Length) {
                return null;
            }
        }
        if (Tag(b, 0) != "RIFF" || Tag(b, 8) != "WEBP") {
            return null;
        }

        uint bits = (uint)(b[21] | b[22] << 8 | b[23] << 16 | b[24] << 24);

        return Tag(b, 12) switch {
            "VP8 " when b[23] == 0x9d && b[24] == 0x01 && b[25] == 0x2a
                => ((b[26] | b[27] << 8) & 0x3fff, (b[28] | b[29] << 8) & 0x3fff),
            "VP8L" when b[20] == 0x2f
                => (1 + (int)(bits & 0x3fff), 1 + (int)(bits >> 14 & 0x3fff)),
            "VP8X" => (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16)),
            _ => null,
        };
    }


    private static string Tag(byte[] b, int offset) {
        return Encoding.ASCII.GetString(b, offset, 4);
    }
}
