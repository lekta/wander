using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Wander.Platform.Windows.Icons;

/// <summary>A picture a file carries - a slicer's plate, a Photoshop preview - made into a tile.</summary>
internal static class ThumbnailPicture {
    /// <summary>The picture fitted into a square on transparency, as a PNG; null when GDI+ cannot read it.</summary>
    public static byte[]? Fit(byte[] picture, int side) {
        try {
            using var input = new MemoryStream(picture);
            using var source = new Bitmap(input);
            using var canvas = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(canvas)) {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                double scale = Math.Min((double)side / source.Width, (double)side / source.Height);
                int w = Math.Max(1, (int)Math.Round(source.Width * scale));
                int h = Math.Max(1, (int)Math.Round(source.Height * scale));
                g.DrawImage(source, new Rectangle((side - w) / 2, (side - h) / 2, w, h));
            }

            using var output = new MemoryStream();
            canvas.Save(output, ImageFormat.Png);

            return output.ToArray();
        } catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException) {
            return null;
        }
    }
}
