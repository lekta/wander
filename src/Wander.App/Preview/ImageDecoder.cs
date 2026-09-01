using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wander.Core.Icons;

namespace Wander.App.Preview;

/// <summary>
/// Turning bytes on disk into something WPF can draw. Every rule here is
/// about the decoder rather than about the preview pane — which cache
/// option keeps a re-opened file from showing its previous contents, how a
/// cover is decoded at the size it will be drawn at, what an EXIF
/// orientation value means — so it sits apart from the controller that
/// decides <em>which</em> file to decode.
/// </summary>
internal static class ImageDecoder {
    /// <summary>
    /// Decodes a file by path. <c>IgnoreImageCache</c> is what keeps a
    /// re-opened file from showing its previous contents — WPF's image
    /// cache is keyed by URI and does not notice the bytes changed.
    /// </summary>
    public static BitmapImage? File(string path) {
        return Decode(bi => {
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.UriSource = new Uri(path);
        });
    }


    /// <summary>
    /// Same, but decoded down to <paramref name="width"/> pixels. A JPEG
    /// decoder asked for a smaller result does less work rather than the
    /// same work followed by a resize, which is the difference between
    /// reading a cover and reading a photograph.
    /// </summary>
    public static BitmapImage? File(string path, int width) {
        return Decode(bi => {
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.DecodePixelWidth = width;
            bi.UriSource = new Uri(path);
        });
    }


    /// <summary>
    /// Decodes bytes already in memory — the preview pulled out of a RAW
    /// container, or a cover lifted out of a music file's tags.
    ///
    /// <para>
    /// Deliberately without <c>IgnoreImageCache</c>: that flag makes
    /// <c>BitmapImage.FinalizeCreation</c> evict the URI it was loaded
    /// from, and a stream-sourced bitmap has no URI — on .NET 10 that is an
    /// <c>ArgumentNullException</c> from inside WPF. It cost the whole RAW
    /// fast path: the decode threw, the caller read the <c>null</c> as "no
    /// embedded preview" and quietly fell back to a full sensor decode.
    /// The flag is meaningless here anyway — there is no cache entry to
    /// bypass when the source is a private <c>MemoryStream</c>.
    /// </para>
    /// </summary>
    public static BitmapImage? Stream(byte[] bytes) {
        return Decode(bi => bi.StreamSource = new MemoryStream(bytes));
    }


    /// <summary>
    /// RAW files get their embedded JPEG preview rather than a full sensor
    /// decode — measured at ~10 ms against ~1200 ms for a 33 MB CR3. Null
    /// when the file has no usable preview; the caller then falls back to
    /// the ordinary decode, so an unrecognised container costs nothing but
    /// the old behaviour.
    /// </summary>
    public static BitmapImage? RawPreview(string path) {
        byte[]? jpeg;
        try {
            using var file = System.IO.File.OpenRead(path);
            jpeg = RawPreviewExtractor.Extract(file);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }

        return jpeg is null ? null : Stream(jpeg);
    }


    /// <summary>
    /// Turns an EXIF orientation value (1..8) into the rotation and mirror
    /// it stands for. Values Wander cannot act on — and the identity value
    /// 1 — return the bitmap untouched.
    /// </summary>
    public static BitmapSource ApplyOrientation(BitmapSource source, int? orientation) {
        var transform = orientation switch {
            2 => Mirror(0),
            3 => new RotateTransform(180),
            4 => Mirror(180),
            5 => Mirror(90),
            6 => new RotateTransform(90),
            7 => Mirror(270),
            8 => new RotateTransform(270),
            _ => (Transform?)null,
        };
        if (transform is null) {
            return source;
        }

        var rotated = new TransformedBitmap(source, transform);
        rotated.Freeze();

        return rotated;
    }


    /// <summary>Horizontal flip, then <paramref name="degrees"/> of rotation.</summary>
    private static Transform Mirror(double degrees) {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(-1, 1));
        group.Children.Add(new RotateTransform(degrees));

        return group;
    }


    /// <summary>
    /// Shared decode settings. <c>OnLoad</c> matters for both callers: it
    /// makes the bitmap independent of the stream (so the file handle and
    /// the buffer can go) and lets us freeze it for the UI thread.
    /// </summary>
    private static BitmapImage? Decode(Action<BitmapImage> setSource) {
        try {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            setSource(bi);
            bi.EndInit();
            bi.Freeze();

            return bi;
        } catch {
            // No codec for this format, truncated file, or a preview whose
            // bytes turned out not to be a JPEG after all.
            return null;
        }
    }
}
