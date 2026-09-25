using Wander.Core;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Persistence;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A thumbnail for a picture inside an archive (PLAN AL). The shell has
/// none to give: both archive handlers answer an entry with its type's icon
/// and never with its picture (stand 2026-09-25, zip and 7z, every flag of
/// <c>IShellItemImageFactory</c>). So the entry's bytes are read and drawn
/// by WinRT's decoder, as <see cref="RawThumbnail"/> draws a RAW: not by the
/// shell, whose thumbcache would keep a picture of a file that is gone a
/// moment later. A zip entry comes through the handler's own stream
/// (<see cref="IShellNamespace.ReadEntry"/>), in memory; an entry of a 7z,
/// rar or tar has no stream (decision of 2026-09-25) and is copied out
/// with the shell's copy engine - the only reader of an <c>ArchiveFolder</c>
/// entry - and drawn from the copy.
///
/// <para>
/// The copy is scratch space outside every rule a file operation follows,
/// as the preview's copy of an entry is (<see cref="TempExtraction"/>, the
/// exception written down in ARCHITECTURE): a folder of its own under <see cref="AppPaths.Tmp"/>,
/// deleted as soon as the thumbnail is drawn - a gallery of five hundred
/// photographs in a zip would otherwise leave five hundred copies behind
/// until the next start's sweep. The preview pane's copy, when there is one
/// already, is read instead and left where it is.
/// </para>
///
/// <para>
/// Budget: pictures only, and only up to <see cref="MaxEntryBytes"/> as the
/// archive states the entry's size - an entry that states none keeps its
/// icon. Only the cells on screen ask (AsyncIcon), and a cell scrolled past
/// before its turn at the gate never does. One entry is unpacked at a time:
/// the engine cannot be stopped inside one, and an entry of a solid 7z or
/// RAR costs the whole block before it - four cells at once would pay for
/// it four times.
/// </para>
/// </summary>
internal static class ArchiveThumbnail {
    /// <summary>The preview pane's ceiling for an entry too (PreviewController.MaxArchivePreviewBytes).</summary>
    private const long MaxEntryBytes = 32L * 1024 * 1024;

    private static readonly SemaphoreSlim _gate = new(1, 1);


    /// <summary>A picture inside an archive - the paths this draws, and that get a thumbnail of their own in the cache.</summary>
    public static bool Supports(string path) {
        return ImageFormats.IsImage(path) && Archives.Inside(path);
    }


    /// <summary>A PNG of at most <paramref name="side"/> pixels on its long side, or null - the icon then stands.</summary>
    public static byte[]? Render(string path, int side) {
        if (!Supports(path) || ServiceLocator.TryGet<IShellNamespace>() is not { } shell) {
            return null;
        }

        // The preview pane's copy of this entry, when it made one: read, not taken.
        string shared = TempExtraction.CopyPathFor(path);
        if (File.Exists(shared)) {
            return Draw(shared, side);
        }

        if (shell.SizeOf(path) is not { } size || size > MaxEntryBytes) {
            return null;
        }

        // A zip hands the entry over as a stream: read into memory, no copy
        // on disk and no turn at the gate. RAW and TGA still go through the
        // copy - their readers take a file.
        if (!ImageFormats.IsRaw(path) && !TgaThumbnail.Supports(path) && shell.ReadEntry(path) is { } bytes) {
            return RawThumbnail.RenderPicture(bytes, side);
        }

        string folder = TempFiles.FolderFor("thumbnail|" + path);
        _gate.Wait();
        try {
            Directory.CreateDirectory(folder);
            shell.CopyOut(new[] { new CopyOutItem(path) }, folder, null, CancellationToken.None).GetAwaiter().GetResult();
            string copy = Path.Combine(folder, Path.GetFileName(path));

            return File.Exists(copy) ? Draw(copy, side) : null;
        } catch (Exception ex) {
            // A password, a broken archive: the icon it is.
            Log.Info($"[icon] archive entry not unpacked for its thumbnail: {path} ({ex.Message})");

            return null;
        } finally {
            _gate.Release();
            TryDelete(folder);
        }
    }


    private static byte[]? Draw(string copy, int side) {
        return RawThumbnail.Render(copy, side)
            ?? TgaThumbnail.Render(copy, side)
            ?? RawThumbnail.RenderPicture(copy, side);
    }

    private static void TryDelete(string folder) {
        try {
            if (Directory.Exists(folder)) {
                Directory.Delete(folder, recursive: true);
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Held a moment longer by whatever read it: the start-up sweep takes it.
        }
    }
}
