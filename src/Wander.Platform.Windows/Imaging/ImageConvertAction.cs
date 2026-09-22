using System.Runtime.InteropServices.WindowsRuntime;
using Wander.Core.Actions;
using Wander.Core.Icons;
using Wander.Core.Localization;
using Wander.Core.Logging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Wander.Platform.Windows.Imaging;

/// <summary>
/// The picture presets' encoder: WIC through WinRT's imaging
/// (<c>Windows.Graphics.Imaging</c>), the same codecs Explorer uses, and
/// nothing installed for JPEG, PNG, BMP, TIFF and GIF. In Platform rather
/// than App on purpose: it makes a file, not a picture for the screen, and
/// WinRT is the imaging API that does not drag WPF along (ARCHITECTURE,
/// "Platform без WPF").
///
/// <para>
/// The picture is turned upright by its EXIF orientation and the file says
/// orientation 1 afterwards, so no viewer turns it twice. What it was shot
/// with, when and where crosses into JPEG and TIFF as EXIF written by
/// number (<see cref="ExifTags"/>) from what MetadataExtractor read off the
/// source - RAW included, which is what makes a converted RAW keep its
/// position. The words a person typed in (title, keywords) cross when the
/// source decoder can hand them over. PNG, BMP and GIF get no metadata:
/// their WIC encoders have nowhere to put EXIF.
/// </para>
///
/// <para>
/// A source that keeps its container and its pixels - a JPEG asked for
/// JPEG at its own size and already upright, or a RAW's preview taken out
/// as it is - goes through WIC's transcoding path, which copies the
/// compressed data rather than decoding and encoding it (measured
/// byte-identical, ImagingProbe stand, 2026-09-16); only its metadata is
/// rewritten. Everything else is decoded, scaled and turned, and encoded
/// at the asked quality.
/// </para>
///
/// <para>
/// A RAW file goes to the system decoder first; when that cannot open it,
/// the JPEG preview inside the container is converted instead - the same
/// fallback thumbnails use - with the container's own metadata.
/// </para>
/// </summary>
public sealed class ImageConvertAction : IBuiltinAction {
    private readonly IImageMetadataReader _metadata;
    private readonly ILogger _log;


    public ImageConvertAction(IImageMetadataReader metadata, ILogger log) {
        _metadata = metadata;
        _log = log;
    }


    public string Name => ActionPresets.ImageConvert;


    public async Task RunAsync(string input, string? output, string arguments, CancellationToken ct) {
        if (output is null) {
            throw new InvalidOperationException($"Built-in action '{Name}' writes a file and needs a declared output.");
        }

        var options = ImageConvertOptions.Parse(arguments);
        byte[] encoded = await ConvertAsync(input, options, ct).ConfigureAwait(false);
        // The last moment a cancel costs nothing: nothing is on disk yet.
        ct.ThrowIfCancellationRequested();

        // CreateNew: the name was made unique a moment ago, and a file that
        // appeared since is not ours to replace.
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.WriteAsync(encoded, ct).ConfigureAwait(false);
    }


    private async Task<byte[]> ConvertAsync(string input, ImageConvertOptions options, CancellationToken ct) {
        // The container's own metadata, whatever the pixels come from: the
        // preview inside a RAW carries none of it.
        var shot = _metadata.Read(input);
        int orientation = shot?.Orientation is int o and >= 1 and <= 8 ? o : 1;

        using var file = File.OpenRead(input);
        using var stream = file.AsRandomAccessStream();
        ct.ThrowIfCancellationRequested();

        if (options.FromPreview) {
            return await ConvertPreviewAsync(file, shot, orientation, options, ct).ConfigureAwait(false);
        }

        BitmapDecoder decoder;
        try {
            decoder = await BitmapDecoder.CreateAsync(stream);
        } catch (Exception ex) when (ImageFormats.IsRaw(input)) {
            _log.Info($"image-convert: no system decoder for {input} ({ex.Message}), converting its preview");

            return await ConvertPreviewAsync(file, shot, orientation, options, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            throw new NotSupportedException(Text.Get("ImageConvertNoDecoder"), ex);
        }

        return await EncodeAsync(decoder, ContainerOf(decoder), shot, orientation, options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The JPEG the camera put inside the RAW. Kept as it is when nothing
    /// asks to change it (<see cref="ImageConvertOptions.KeepsPreviewAsIs"/>):
    /// the pixels stay the way the camera stored them and the file says how
    /// to turn them, exactly as the camera writes its own JPEGs.
    /// </summary>
    private async Task<byte[]> ConvertPreviewAsync(
        FileStream file, ImageMetadata? shot, int orientation, ImageConvertOptions options, CancellationToken ct) {

        file.Position = 0;
        byte[] jpeg = RawPreviewExtractor.Extract(file, options.FullSizePreview)
            ?? throw new NotSupportedException(Text.Get("ImageConvertNoPreview"));
        ct.ThrowIfCancellationRequested();

        using var preview = new InMemoryRandomAccessStream();
        await preview.WriteAsync(jpeg.AsBuffer());
        preview.Seek(0);
        BitmapDecoder decoder;
        try {
            decoder = await BitmapDecoder.CreateAsync(preview);
        } catch (Exception ex) {
            throw new NotSupportedException(Text.Get("ImageConvertNoPreview"), ex);
        }

        if (options.KeepsPreviewAsIs) {
            return await TranscodeAsync(decoder, shot, orientation).ConfigureAwait(false);
        }

        return await EncodeAsync(decoder, "jpg", shot, orientation, options, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> EncodeAsync(
        BitmapDecoder decoder, string sourceContainer, ImageMetadata? shot, int orientation,
        ImageConvertOptions options, CancellationToken ct) {

        // Orientations 5..8 are a quarter turn, so the side that has to fit
        // is the other one; the scale is applied to the stored pixels.
        bool quarterTurn = orientation is 5 or 6 or 7 or 8;
        int storedWidth = (int)decoder.PixelWidth;
        int storedHeight = (int)decoder.PixelHeight;
        var (uprightWidth, uprightHeight) = options.Fit(
            quarterTurn ? storedHeight : storedWidth,
            quarterTurn ? storedWidth : storedHeight);
        int scaledWidth = quarterTurn ? uprightHeight : uprightWidth;
        int scaledHeight = quarterTurn ? uprightWidth : uprightHeight;

        bool pixelsStay = orientation == 1 && scaledWidth == storedWidth && scaledHeight == storedHeight;
        if (pixelsStay && options.SameContainer(sourceContainer)) {
            // Nothing to decode: the compressed data is copied as it is.
            return await TranscodeAsync(decoder, shot, orientation: 1).ConfigureAwait(false);
        }

        var transform = new BitmapTransform {
            ScaledWidth = (uint)scaledWidth,
            ScaledHeight = (uint)scaledHeight,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        // IgnoreExifOrientation: the turn is applied on the encoder below
        // from the container's value, which for a RAW preview is the only
        // place it is recorded.
        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
        byte[] pixels = data.DetachPixelData();
        ct.ThrowIfCancellationRequested();

        var texts = await ReadTextsAsync(decoder).ConfigureAwait(false);

        bool opaque = options.Format is ImageTarget.Jpeg or ImageTarget.Bmp;
        if (opaque) {
            // Transparent over white, not over whatever colour the
            // transparent pixels happen to carry.
            FlattenOnWhite(pixels);
        }

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(EncoderIdOf(options.Format), output, EncoderOptions(options));
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            opaque ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Straight,
            (uint)scaledWidth, (uint)scaledHeight, 96, 96, pixels);
        encoder.BitmapTransform.Rotation = orientation switch {
            3 or 4 => BitmapRotation.Clockwise180Degrees,
            5 or 6 => BitmapRotation.Clockwise90Degrees,
            7 or 8 => BitmapRotation.Clockwise270Degrees,
            _ => BitmapRotation.None,
        };
        encoder.BitmapTransform.Flip = orientation is 2 or 4 or 5 or 7
            ? BitmapFlip.Horizontal
            : BitmapFlip.None;

        if (options.CarriesMetadata) {
            // The pixels are upright now; the file must not ask for the turn again.
            var container = options.Format == ImageTarget.Tiff ? ExifContainer.Tiff : ExifContainer.Jpeg;
            await WritePropertiesAsync(encoder, ExifTags.Build(shot, container, orientation: 1), texts).ConfigureAwait(false);
        }

        await encoder.FlushAsync();

        return await BytesOf(output).ConfigureAwait(false);
    }

    /// <summary>
    /// The compressed data copied through, only the metadata rewritten:
    /// what the container already says stays, what the reader knows is
    /// laid over it.
    /// </summary>
    private async Task<byte[]> TranscodeAsync(BitmapDecoder decoder, ImageMetadata? shot, int orientation) {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateForTranscodingAsync(output, decoder);

        var container = decoder.DecoderInformation.CodecId == BitmapDecoder.TiffDecoderId ? ExifContainer.Tiff : ExifContainer.Jpeg;
        await WritePropertiesAsync(encoder, ExifTags.Build(shot, container, orientation), texts: null).ConfigureAwait(false);
        await encoder.FlushAsync();

        return await BytesOf(output).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the properties in one call, and one by one when the encoder
    /// refuses the batch: a property the target cannot hold costs that
    /// property only, never the picture.
    /// </summary>
    private async Task WritePropertiesAsync(BitmapEncoder encoder, BitmapPropertySet tags, BitmapPropertySet? texts) {
        var all = new BitmapPropertySet();
        foreach (var kv in tags) {
            all[kv.Key] = kv.Value;
        }
        if (texts is not null) {
            foreach (var kv in texts) {
                all.TryAdd(kv.Key, kv.Value);
            }
        }

        try {
            await encoder.BitmapProperties.SetPropertiesAsync(all);

            return;
        } catch (Exception ex) {
            _log.Warn($"image-convert: metadata refused as a whole, writing it property by property: {ex.Message}");
        }

        foreach (var kv in all) {
            try {
                await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet { [kv.Key] = kv.Value });
            } catch (Exception ex) {
                _log.Warn($"image-convert: property {kv.Key} dropped: {ex.Message}");
            }
        }
    }

    /// <summary>Title, keywords and the like, as the source's decoder gives them; none when it cannot.</summary>
    private static async Task<BitmapPropertySet?> ReadTextsAsync(BitmapDecoder decoder) {
        try {
            var read = await decoder.BitmapProperties.GetPropertiesAsync(ExifTags.TextPolicies);
            var texts = new BitmapPropertySet();
            foreach (var kv in read) {
                if (kv.Value is not null) {
                    texts[kv.Key] = kv.Value;
                }
            }

            return texts.Count > 0 ? texts : null;
        } catch (Exception) {
            // A container without a property store, or a decoder that
            // answers the question with an exception: nothing to carry.
            return null;
        }
    }


    private static Guid EncoderIdOf(ImageTarget format) {
        return format switch {
            ImageTarget.Jpeg => BitmapEncoder.JpegEncoderId,
            ImageTarget.Png => BitmapEncoder.PngEncoderId,
            ImageTarget.Bmp => BitmapEncoder.BmpEncoderId,
            ImageTarget.Tiff => BitmapEncoder.TiffEncoderId,
            _ => BitmapEncoder.GifEncoderId,
        };
    }

    private static BitmapPropertySet EncoderOptions(ImageConvertOptions options) {
        var props = new BitmapPropertySet();
        if (options.Format == ImageTarget.Jpeg) {
            props["ImageQuality"] = new BitmapTypedValue(options.Quality / 100f, PropertyType.Single);
        }

        return props;
    }

    /// <summary>The source's container as an extension, for <see cref="ImageConvertOptions.SameContainer"/>; empty for the rest.</summary>
    private static string ContainerOf(BitmapDecoder decoder) {
        var codec = decoder.DecoderInformation.CodecId;
        if (codec == BitmapDecoder.JpegDecoderId) {
            return "jpg";
        }
        if (codec == BitmapDecoder.TiffDecoderId) {
            return "tiff";
        }
        if (codec == BitmapDecoder.PngDecoderId) {
            return "png";
        }
        if (codec == BitmapDecoder.BmpDecoderId) {
            return "bmp";
        }
        if (codec == BitmapDecoder.GifDecoderId) {
            return "gif";
        }

        return string.Empty;
    }

    /// <summary>Straight-alpha BGRA composited over white, in place; alpha set opaque.</summary>
    private static void FlattenOnWhite(byte[] bgra) {
        for (int i = 0; i + 3 < bgra.Length; i += 4) {
            int alpha = bgra[i + 3];
            if (alpha == 255) {
                continue;
            }
            int white = 255 - alpha;
            bgra[i] = (byte)((bgra[i] * alpha + 255 * white) / 255);
            bgra[i + 1] = (byte)((bgra[i + 1] * alpha + 255 * white) / 255);
            bgra[i + 2] = (byte)((bgra[i + 2] * alpha + 255 * white) / 255);
            bgra[i + 3] = 255;
        }
    }

    private static async Task<byte[]> BytesOf(InMemoryRandomAccessStream stream) {
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);

        return bytes;
    }
}
