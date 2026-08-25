using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using static Wander.Platform.Windows.Shell.ShellContextMenuInterop;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Converts the <c>HBITMAP</c> a shell extension attaches to its menu item
/// into PNG bytes, matching how <c>IIconProvider</c> already hands images
/// across the Core boundary.
///
/// <para>
/// The trap here is alpha. <c>Image.FromHbitmap</c> is the obvious call and
/// gets orientation right, but it produces a <c>Format32bppRgb</c> bitmap —
/// the alpha byte is dropped, so every icon with a transparent background
/// comes out as artwork on a solid black square. Reading the DIB's own bits
/// keeps the alpha; the price is that we then owe the row order ourselves,
/// which <see cref="BITMAPINFOHEADER.biHeight"/> tells us exactly.
/// </para>
/// </summary>
internal static class ShellMenuIcons {
    /// <summary>
    /// Menu bitmaps can be one of the <c>HBMMENU_*</c> pseudo-handles
    /// (small integers) rather than a real GDI object; anything in that
    /// range is a system-drawn glyph we cannot read.
    /// </summary>
    private const long MinRealHandle = 0x20;


    public static byte[]? ToPng(IntPtr hbitmap) {
        if (hbitmap == IntPtr.Zero || hbitmap.ToInt64() < MinRealHandle) {
            return null;
        }

        try {
            using var image = Decode(hbitmap);

            if (image is null) {
                return null;
            }

            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);

            return stream.ToArray();
        } catch (Exception) {
            // A handler that hands out a broken bitmap costs its icon, not
            // the menu. Silent by design: this runs once per item per
            // right-click and would otherwise flood the session log.
            return null;
        }
    }


    private static Bitmap? Decode(IntPtr hbitmap) {
        var dib = default(DIBSECTION);
        int size = Marshal.SizeOf<DIBSECTION>();

        // A device-dependent bitmap fills only the leading BITMAP and returns
        // the smaller size — nothing to recover there, and no alpha to lose.
        if (GetObject(hbitmap, size, ref dib) != size
            || dib.dsBm.bmBitsPixel != 32
            || dib.dsBm.bmBits == IntPtr.Zero
            || dib.dsBm.bmWidth <= 0
            || dib.dsBm.bmHeight <= 0) {
            return Image.FromHbitmap(hbitmap);
        }

        int width = dib.dsBm.bmWidth;
        int height = dib.dsBm.bmHeight;
        int stride = dib.dsBm.bmWidthBytes;

        var pixels = new byte[stride * height];
        Marshal.Copy(dib.dsBm.bmBits, pixels, 0, pixels.Length);

        // An all-zero alpha channel means the handler never filled one in —
        // treating it as real would render the icon completely invisible.
        if (!HasAlphaChannel(pixels)) {
            return Image.FromHbitmap(hbitmap);
        }

        // 32-bit menu bitmaps follow the AlphaBlend convention, i.e. colour
        // channels are already multiplied by alpha — which is exactly what
        // PArgb means to GDI+.
        var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        var target = result.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try {
            bool topDown = dib.dsBmih.biHeight < 0;
            for (int y = 0; y < height; y++) {
                int sourceRow = topDown ? y : height - 1 - y;
                Marshal.Copy(pixels, sourceRow * stride, target.Scan0 + (y * target.Stride), width * 4);
            }
        } finally {
            result.UnlockBits(target);
        }

        return result;
    }

    private static bool HasAlphaChannel(byte[] pixels) {
        for (int i = 3; i < pixels.Length; i += 4) {
            if (pixels[i] != 0) {
                return true;
            }
        }

        return false;
    }
}
