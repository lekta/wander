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
