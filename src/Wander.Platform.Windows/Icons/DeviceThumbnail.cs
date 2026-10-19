using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Preview;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A thumbnail for a picture or a video on a phone or a camera (PLAN Y4b).
/// Not the shell's: it would list the whole folder over the cable to find
/// the item first (a phone's camera folder - minutes cold, stand Y4a), and
/// keep a picture of a file that is not on any disk in the system's
/// thumbcache.
///
/// <para>
/// The device keeps a small picture of its own for most files
/// (<see cref="IDeviceContent.ReadThumbnail"/>: ~14 KB at 9 ms on a Canon
/// R8, stand Y4a) - enough for a row or a small tile. A gallery tile wants
/// more than that, and a picture that is not a RAW is read whole instead,
/// up to <see cref="MaxPictureBytes"/> - a phone's JPEG is a few megabytes;
/// a RAW and a video keep the device's own. The preview pane's copy, when
/// there is one, is read rather than the device. One read at a time: the
/// device answers one request at a time anyway, and four cells at once
/// would hold up the copy running beside them.
/// </para>
/// </summary>
internal static class DeviceThumbnail {
    /// <summary>The device's own pictures are about this wide: past it, a picture is read whole.</summary>
    private const int DeviceThumbnailSide = 160;

    /// <summary>The preview pane's ceiling for a copy off a device, as for an archive entry.</summary>
    private const long MaxPictureBytes = 32L * 1024 * 1024;

    /// <summary>How many times a RAW's picture is asked of a busy device before the tile is left for AsyncIcon's retry.</summary>
    private const int BusyAttempts = 4;

    /// <summary>The pause between them.</summary>
    private const int BusyPauseMs = 100;

    private static readonly SemaphoreSlim _gate = new(1, 1);


    /// <summary>A picture or a video on a device - what this draws, and what gets a thumbnail of its own in the cache.</summary>
    public static bool Supports(string path) {
        return DevicePath.IsInside(path) && (ImageFormats.IsImage(path) || PreviewRouter.ForExtension(Path.GetExtension(path)) == PreviewRoute.Video);
    }


    /// <summary>A PNG of at most <paramref name="side"/> pixels on its long side, or null - the icon then stands.</summary>
    public static byte[]? Render(string path, int side) {
        if (!Supports(path) || ServiceLocator.TryGet<IDeviceContent>() is not { } devices) {
            return null;
        }

        string shared = TempExtraction.CopyPathFor(path);
        if (File.Exists(shared)) {
            return RawThumbnail.Render(shared, side) ?? RawThumbnail.RenderPicture(shared, side);
        }

        _gate.Wait();
        try {
            bool whole = side > DeviceThumbnailSide && ImageFormats.IsImage(path) && !ImageFormats.IsRaw(path)
                && devices.GetEntry(path) is { Size: { } size } && size <= MaxPictureBytes;
            if (whole && ReadAll(devices, path) is { } bytes && RawThumbnail.RenderPicture(bytes, side) is { } picture) {
                return picture;
            }

            if (Own(devices, path) is { } own && RawThumbnail.RenderPicture(own, side) is { } small) {
                return small;
            }

            // No picture of its own - a phone may keep none: a small picture
            // is read whole for a row too.
            return !whole && ImageFormats.IsImage(path) && !ImageFormats.IsRaw(path)
                && devices.GetEntry(path) is { Size: { } bytesLeft } && bytesLeft <= MaxPictureBytes
                && ReadAll(devices, path) is { } read
                    ? RawThumbnail.RenderPicture(read, side)
                    : null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) {
            Log.Info($"[icon] no thumbnail off the device for {path} ({ex.Message})");
            // A RAW has no other picture here than the device's own: one the
            // device did not give now - busy with the preview's read beside
            // it - is asked for again (AsyncIcon, a second later) rather than
            // drawn as the type's icon for the rest of the session (R8 after
            // a fast PgDn, 2026-10-19). Nothing is remembered of a throw.
            if (ImageFormats.IsRaw(path)) {
                throw;
            }

            return null;
        } finally {
            _gate.Release();
        }
    }


    /// <summary>
    /// The device's own picture of a file. Anything but a RAW goes on
    /// without one the device did not give, to its bytes or its type's
    /// icon, as when it keeps none. A RAW is asked again a few times: the
    /// camera turns it away only while it reads something beside it - the
    /// preview's head of the next picture, a tenth of a second
    /// (0x800700AA, R8, 2026-10-19).
    /// </summary>
    private static byte[]? Own(IDeviceContent devices, string path) {
        for (int attempt = 1; ; attempt++) {
            try {
                return devices.ReadThumbnail(path);
            } catch (IOException ex) when (!ImageFormats.IsRaw(path)) {
                Log.Info($"[icon] no picture of the device's own for {path} ({ex.Message})");

                return null;
            } catch (IOException) when (attempt < BusyAttempts) {
                Thread.Sleep(BusyPauseMs);
            }
        }
    }

    private static byte[]? ReadAll(IDeviceContent devices, string path) {
        using var stream = devices.OpenRead(path);
        using var bytes = new MemoryStream((int)Math.Min(stream.Length, MaxPictureBytes));
        stream.CopyTo(bytes, 1 << 20);

        return bytes.Length > 0 ? bytes.ToArray() : null;
    }
}
