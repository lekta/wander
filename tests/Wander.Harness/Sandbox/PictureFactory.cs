using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wander.Harness.Sandbox;

/// <summary>
/// Synthetic photographs: a gradient, a shape and a caption naming the file
/// and its orientation, so a screenshot shows at a glance which picture is
/// which and whether it was rotated. Encoded as JPEG with an EXIF
/// orientation tag, because that tag is what the gallery, the preview and
/// the thumbnail path all have to honour.
/// </summary>
public static class PictureFactory {
    public static byte[] Jpeg(int width, int height, int orientation, string label, int seed, int quality = 85) {
        var bitmap = Render(width, height, orientation, label, seed);

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        var metadata = new BitmapMetadata("jpg");
        SetExif(metadata, orientation);
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    public static void SaveJpeg(string path, int width, int height, int orientation, string label, int seed) {
        File.WriteAllBytes(path, Jpeg(width, height, orientation, label, seed));
    }

    /// <summary>The same picture as top-down BGRA: what a reader of any file made from it has to give back.</summary>
    public static byte[] Bgra(int width, int height, string label, int seed) {
        var pixels = new byte[width * height * 4];
        Render(width, height, 1, label, seed).CopyPixels(pixels, width * 4, 0);

        return pixels;
    }

    /// <summary>
    /// A TGA texture the way games and 3D tools write one: 32 bits,
    /// run-length encoded, rows from the bottom up. The two are what a TGA
    /// reader gets wrong - the picture comes out garbled or upside down -
    /// and the arrow drawn upright shows which.
    /// </summary>
    public static byte[] Tga(byte[] bgra, int width, int height) {
        using var stream = new MemoryStream();
        var header = new byte[18];
        header[2] = 10;  // true colour, run-length encoded
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
        header[16] = 32;
        header[17] = 8;  // eight bits of alpha; origin bottom left
        stream.Write(header);
        int stride = width * 4;
        for (int y = height - 1; y >= 0; y--) {
            WriteRunLengthRow(stream, bgra.AsSpan(y * stride, stride));
        }

        return stream.ToArray();
    }


    private static RenderTargetBitmap Render(int width, int height, int orientation, string label, int seed) {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) {
            var random = new Random(seed);
            var from = Color.FromRgb((byte)random.Next(40, 220), (byte)random.Next(40, 220), (byte)random.Next(40, 220));
            var to = Color.FromRgb((byte)random.Next(40, 220), (byte)random.Next(40, 220), (byte)random.Next(40, 220));
            var brush = new LinearGradientBrush(from, to, random.Next(0, 360));
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));

            double radius = Math.Min(width, height) * 0.22;
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null,
                new Point(width * 0.68, height * 0.36), radius, radius);

            // The scene is drawn the way a sensor would record it for this
            // orientation, so once a viewer applies the tag the arrow
            // points up and the caption reads upright. A sideways arrow on
            // a screenshot therefore means the orientation was ignored.
            double sceneAngle = orientation switch {
                6 => -90,
                8 => 90,
                3 => 180,
                _ => 0,
            };
            dc.PushTransform(new RotateTransform(sceneAngle, width / 2.0, height / 2.0));

            var arrow = new StreamGeometry();
            using (var g = arrow.Open()) {
                double cx = width / 2.0;
                double cy = height / 2.0;
                double s = Math.Min(width, height) * 0.2;
                g.BeginFigure(new Point(cx, cy - s), true, true);
                g.LineTo(new Point(cx + s * 0.6, cy + s * 0.2), true, false);
                g.LineTo(new Point(cx + s * 0.25, cy + s * 0.2), true, false);
                g.LineTo(new Point(cx + s * 0.25, cy + s), true, false);
                g.LineTo(new Point(cx - s * 0.25, cy + s), true, false);
                g.LineTo(new Point(cx - s * 0.25, cy + s * 0.2), true, false);
                g.LineTo(new Point(cx - s * 0.6, cy + s * 0.2), true, false);
            }
            dc.DrawGeometry(Brushes.White, null, arrow);

            var text = new FormattedText(
                $"{label}  o={orientation}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), Math.Max(18, Math.Min(width, height) / 16.0), Brushes.White, 1.0);
            dc.DrawText(text, new Point(width / 2.0 - text.Width / 2, height / 2.0 + Math.Min(width, height) * 0.24));
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        return bitmap;
    }

    /// <summary>
    /// One row as packets that do not run into the next: a run of equal
    /// pixels is one packet, anything else goes raw up to where the next
    /// run starts. The gradient gives raw stretches, the white arrow and
    /// caption give runs - both kinds of packet end up in the file.
    /// </summary>
    private static void WriteRunLengthRow(Stream stream, ReadOnlySpan<byte> row) {
        int count = row.Length / 4;
        int x = 0;
        while (x < count) {
            int run = 1;
            while (x + run < count && run < 128 && SamePixel(row, x, x + run)) {
                run++;
            }
            if (run > 1) {
                stream.WriteByte((byte)(0x80 | (run - 1)));
                stream.Write(row.Slice(x * 4, 4));
                x += run;

                continue;
            }

            int raw = 1;
            while (x + raw < count && raw < 128 && !(x + raw + 1 < count && SamePixel(row, x + raw, x + raw + 1))) {
                raw++;
            }
            stream.WriteByte((byte)(raw - 1));
            stream.Write(row.Slice(x * 4, raw * 4));
            x += raw;
        }
    }

    private static bool SamePixel(ReadOnlySpan<byte> row, int a, int b) {
        return row.Slice(a * 4, 4).SequenceEqual(row.Slice(b * 4, 4));
    }

    private static void SetExif(BitmapMetadata metadata, int orientation) {
        // WIC accepts the raw IFD query on a fresh JPEG container; the
        // System.Photo policy is the documented fallback if it ever stops.
        try {
            metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
            metadata.SetQuery("/app1/ifd/{ushort=271}", "Wander Harness");
            metadata.SetQuery("/app1/ifd/{ushort=272}", "Synthetic Camera");
            metadata.SetQuery("/app1/ifd/exif/{ushort=36867}", DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture));
        } catch (Exception) {
            metadata.SetQuery("System.Photo.Orientation", (ushort)orientation);
            metadata.SetQuery("System.Photo.CameraManufacturer", "Wander Harness");
            metadata.SetQuery("System.Photo.CameraModel", "Synthetic Camera");
        }
    }
}
