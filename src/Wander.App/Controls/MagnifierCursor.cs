using System.IO;
using System.Windows.Input;

namespace Wander.App.Controls;

/// <summary>
/// Generates a 32×32 magnifier-glass cursor at process start so we don't
/// need to ship a binary <c>.cur</c> asset. The cursor is built by:
///   1. drawing the glyph into an in-memory 32-bpp BGRA buffer,
///   2. serialising that buffer as a Windows <c>.cur</c> file (essentially
///      an ICO with a hotspot field),
///   3. constructing a WPF <see cref="Cursor"/> from the byte stream.
///
/// The .cur file format:
///   ICONDIR    (6 bytes)   — reserved / type=2 / count=1
///   ICONDIRENTRY (16 bytes) — size, hotspot, byte offset / size of the image
///   BITMAPINFOHEADER (40 bytes) — biHeight = 2 × height (XOR + AND mask)
///   XOR pixels (32-bpp BGRA, bottom-up)  — 4096 bytes for 32×32
///   AND mask  (1-bpp, padded to 32-bit rows, bottom-up) — 128 bytes
///
/// Total file: ~4290 bytes, generated once and cached as a static field.
/// </summary>
internal static class MagnifierCursor {
    private const int Size = 32;
    private const int HotspotX = 12;        // lens centre
    private const int HotspotY = 12;

    private static Cursor? _cached;

    /// <summary>The shared magnifier cursor instance. Built lazily on first access.</summary>
    public static Cursor Instance => _cached ??= Build();


    private static Cursor Build() {
        // 1. Paint pixels into a top-down BGRA buffer for ease of indexing.
        var bgraTopDown = new byte[Size * Size * 4];
        Paint(bgraTopDown);

        // 2. Flip vertically — .cur stores rows bottom-up.
        var bgra = FlipVertical(bgraTopDown);

        // 3. AND mask: 1 for transparent pixels, 0 for opaque. 1 bpp,
        //    row-aligned to 32 bits (4 bytes/row), bottom-up to match BGRA.
        var andMask = BuildAndMask(bgraTopDown);

        // 4. Serialise.
        var ms = new MemoryStream();
        // leaveOpen: BinaryWriter's default Dispose closes the underlying
        // stream, which then throws on `ms.Position = 0` below. Disposing
        // the writer only flushes; the stream is handed to `new Cursor(ms)`
        // which reads it lazily, so we keep ownership of the lifetime.
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) {
            // ICONDIR
            w.Write((ushort)0);             // reserved
            w.Write((ushort)2);             // type = cursor
            w.Write((ushort)1);             // count

            // ICONDIRENTRY
            int imageDataSize = 40 + bgra.Length + andMask.Length;
            w.Write((byte)Size);            // width
            w.Write((byte)Size);            // height
            w.Write((byte)0);               // colour count (0 for >=8-bpp)
            w.Write((byte)0);               // reserved
            w.Write((ushort)HotspotX);
            w.Write((ushort)HotspotY);
            w.Write((uint)imageDataSize);
            w.Write((uint)(6 + 16));        // offset to image data

            // BITMAPINFOHEADER
            w.Write((uint)40);              // biSize
            w.Write(Size);                  // biWidth
            w.Write(Size * 2);              // biHeight (XOR + AND mask combined)
            w.Write((ushort)1);             // biPlanes
            w.Write((ushort)32);            // biBitCount
            w.Write((uint)0);               // biCompression (BI_RGB)
            w.Write((uint)0);               // biSizeImage
            w.Write(0);                     // biXPelsPerMeter
            w.Write(0);                     // biYPelsPerMeter
            w.Write((uint)0);               // biClrUsed
            w.Write((uint)0);               // biClrImportant

            w.Write(bgra);
            w.Write(andMask);
        }

        ms.Position = 0;
        return new Cursor(ms);
    }


    /// <summary>
    /// Draws the magnifier glyph: a hollow circle (lens) with a short
    /// diagonal handle. White outline with a dark inner line so the cursor
    /// reads on both light and dark backgrounds.
    /// </summary>
    private static void Paint(byte[] bgra) {
        const int cx = 12, cy = 12;
        const int rOuter = 9;
        const int rInner = 8;
        const int hxStart = 19, hyStart = 19;
        const int hxEnd = 28, hyEnd = 28;

        // Lens: dark ring sandwiched between two white rings for visibility.
        StrokeCircle(bgra, cx, cy, rOuter + 1, 0xFF, 0xFF, 0xFF, 0xFF);
        StrokeCircle(bgra, cx, cy, rOuter,     0x00, 0x00, 0x00, 0xFF);
        StrokeCircle(bgra, cx, cy, rInner,     0xFF, 0xFF, 0xFF, 0xFF);

        // Handle: thick line with a dark core for the same dual-bg legibility.
        StrokeLine(bgra, hxStart - 1, hyStart - 1, hxEnd, hyEnd, 0xFF, 0xFF, 0xFF, 0xFF);
        StrokeLine(bgra, hxStart,     hyStart,     hxEnd, hyEnd, 0x00, 0x00, 0x00, 0xFF);
    }


    // ---- pixel-plotting helpers ---------------------------------------

    private static void Plot(byte[] bgra, int x, int y, byte b, byte g, byte r, byte a) {
        if ((uint)x >= Size || (uint)y >= Size) {
            return;
        }
        int o = (y * Size + x) * 4;
        bgra[o + 0] = b;
        bgra[o + 1] = g;
        bgra[o + 2] = r;
        bgra[o + 3] = a;
    }

    private static void StrokeCircle(byte[] bgra, int cx, int cy, int radius, byte b, byte g, byte r, byte a) {
        // Midpoint circle, single-pixel-thick outline. Good enough at 32×32.
        int x = radius, y = 0, err = 0;
        while (x >= y) {
            Plot(bgra, cx + x, cy + y, b, g, r, a);
            Plot(bgra, cx + y, cy + x, b, g, r, a);
            Plot(bgra, cx - y, cy + x, b, g, r, a);
            Plot(bgra, cx - x, cy + y, b, g, r, a);
            Plot(bgra, cx - x, cy - y, b, g, r, a);
            Plot(bgra, cx - y, cy - x, b, g, r, a);
            Plot(bgra, cx + y, cy - x, b, g, r, a);
            Plot(bgra, cx + x, cy - y, b, g, r, a);
            y++;
            err += 1 + 2 * y;
            if (2 * (err - x) + 1 > 0) {
                x--;
                err += 1 - 2 * x;
            }
        }
    }

    private static void StrokeLine(byte[] bgra, int x0, int y0, int x1, int y1, byte b, byte g, byte r, byte a) {
        // Bresenham. Draws a 1-pixel stroke; for the handle we call this
        // twice with offset endpoints to get a 2-pixel "thick" stroke.
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true) {
            Plot(bgra, x0, y0, b, g, r, a);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }


    // ---- buffer reshaping for the .cur format -------------------------

    private static byte[] FlipVertical(byte[] topDownBgra) {
        var dst = new byte[topDownBgra.Length];
        int rowBytes = Size * 4;
        for (int y = 0; y < Size; y++) {
            int srcRow = y * rowBytes;
            int dstRow = (Size - 1 - y) * rowBytes;
            Buffer.BlockCopy(topDownBgra, srcRow, dst, dstRow, rowBytes);
        }
        return dst;
    }

    private static byte[] BuildAndMask(byte[] topDownBgra) {
        // 1 bpp, 32-bit-aligned rows, bottom-up.
        int rowBytes = ((Size + 31) / 32) * 4;     // = 4 for Size = 32
        var mask = new byte[rowBytes * Size];

        for (int y = 0; y < Size; y++) {
            int dstRowBase = (Size - 1 - y) * rowBytes;
            for (int x = 0; x < Size; x++) {
                byte alpha = topDownBgra[(y * Size + x) * 4 + 3];
                bool transparent = alpha == 0;
                if (transparent) {
                    int byteIndex = dstRowBase + (x / 8);
                    int bit = 7 - (x % 8);
                    mask[byteIndex] |= (byte)(1 << bit);
                }
            }
        }
        return mask;
    }
}
