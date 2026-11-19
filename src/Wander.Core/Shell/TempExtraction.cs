using System.Collections.Concurrent;
using System.Globalization;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Persistence;

namespace Wander.Core.Shell;

/// <summary>
/// One entry copied out of an archive into scratch space Wander owns and
/// sweeps itself. Three callers want the same copy for different reasons -
/// "Open" hands it to another program, the preview pane reads it, and the
/// conflict window compares its bytes with the file it would overwrite -
/// and they share both the code and the folder, so selecting an entry that
/// was already opened costs nothing.
///
/// <para>
/// Deliberately outside the rules a file operation follows (guard, log,
/// undo, conflict dialog): a scratch copy of somebody's file is not the
/// user's data. It carries no undo step and asks about no conflicts,
/// because the folder it goes into was made for it and nothing else. The
/// departure is written down in FILEOPS.md.
/// </para>
///
/// <para>
/// One folder per source path (<see cref="TempFiles.FolderFor"/>), named by
/// a hash of it: two entries called <c>readme.txt</c> from different places
/// never collide, and the copy keeps its own name - which is what the title
/// bar of whatever opens it shows. Swept at startup, a day old.
/// </para>
/// </summary>
public static class TempExtraction {
    // Each copy made this session, by its path: its size and time as made.
    // A copy that no longer matches was edited by whoever it was opened in
    // (audit 2026-11-18, F4). In memory only - a copy from an earlier
    // session has no record, and is replaced as before.
    private static readonly ConcurrentDictionary<string, TempCopyStamp> _made = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// Copies <paramref name="source"/> - a path inside an archive - into
    /// <paramref name="tempFolder"/> and says where it landed. An earlier
    /// copy under the same name is replaced: the archive may have been
    /// rebuilt since, and a stale copy would be shown as the current one.
    /// One edited since this session made it is the user's work, not
    /// scratch: it goes to the bin, not into oblivion, and the result says
    /// so; a bin that will not take it stops the extraction, the copy kept.
    /// </summary>
    public static async Task<TempCopy> CopyOutAsync(
        IShellNamespace ns, IFileSystem fs, IRecycleBin bin, ILogger log,
        string source, string tempFolder, CancellationToken ct) {

        fs.CreateDirectory(tempFolder);
        string destination = Path.Combine(tempFolder, NameOf(source));
        var state = StateOf(fs, destination);
        if (state == TempCopyState.Edited) {
            bin.Send(destination);
            log.Info($"Extract (temporary copy): {destination} was edited since it was made - to the bin");
        } else if (state != TempCopyState.Missing) {
            fs.DeleteFile(destination);
        }
        // The copy the record was about is gone: a copy cut short below is
        // not to be taken for it edited.
        _made.TryRemove(destination, out _);

        await ns.CopyOut(new[] { new CopyOutItem(source) }, tempFolder, null, ct).ConfigureAwait(false);
        if (StampOf(fs, destination) is { } made) {
            _made[destination] = made;
        }
        log.Info($"Extract (temporary copy): {source} -> {destination}");

        return new TempCopy(destination, state == TempCopyState.Edited);
    }

    /// <summary>The same, into the folder that belongs to this source.</summary>
    public static Task<TempCopy> CopyOutAsync(
        IShellNamespace ns, IFileSystem fs, IRecycleBin bin, ILogger log, string source, CancellationToken ct) {
        return CopyOutAsync(ns, fs, bin, log, source, TempFiles.FolderFor(source), ct);
    }

    /// <summary>
    /// Whether the copy at <paramref name="copyPath"/> is still the one this
    /// session made (<see cref="Judge"/>) - what the preview pane asks before
    /// it shows the copy instead of unpacking again.
    /// </summary>
    public static TempCopyState StateOf(IFileSystem fs, string copyPath) {
        return Judge(_made.TryGetValue(copyPath, out var made) ? made : null, StampOf(fs, copyPath));
    }

    /// <summary>
    /// A copy as it was made (<paramref name="recorded"/>, null - not made
    /// this session) against the file there now (<paramref name="onDisk"/>,
    /// null - none): as made, edited since, or nothing known of it.
    /// </summary>
    public static TempCopyState Judge(TempCopyStamp? recorded, TempCopyStamp? onDisk) {
        if (onDisk is null) {
            return TempCopyState.Missing;
        }
        if (recorded is null) {
            return TempCopyState.Unrecorded;
        }

        return recorded == onDisk ? TempCopyState.Fresh : TempCopyState.Edited;
    }

    /// <summary>
    /// Where the overload above puts the copy of <paramref name="source"/>,
    /// whether or not it has been made yet: a caller can look before it asks.
    /// </summary>
    public static string CopyPathFor(string source) {
        return Path.Combine(TempFiles.FolderFor(source), NameOf(source));
    }

    /// <summary>
    /// The head of a RAW on a phone or a camera - as much of it as its
    /// embedded pictures need - for the preview pane, which wants nothing
    /// else of it: a CR3 has its movie box, the 1620-px preview and the
    /// full-size JPEG in the first few megabytes and the sensor's data after
    /// them (2026-10-19). Never the file: it goes into a folder of its own,
    /// named by the size, that "Open" and the conflict window never look in.
    /// A head made already is the one returned. Null when the pictures are
    /// not within <paramref name="limit"/> bytes.
    /// </summary>
    public static string? CopyHead(
        IDeviceContent devices, IFileSystem fs, ILogger log, FileSystemEntry entry, long limit, CancellationToken ct) {

        if (entry.Size is { } listed && fs.FileExists(HeadPathFor(entry.FullPath, listed))) {
            return HeadPathFor(entry.FullPath, listed);
        }

        using var device = devices.OpenRead(entry.FullPath);
        long length = entry.Size ?? device.Length;
        string destination = HeadPathFor(entry.FullPath, length);
        if (fs.FileExists(destination)) {
            return destination;
        }

        var head = new HeadStream(device, length, limit, ct);
        if (RawPreviewExtractor.Extract(head, fullSize: true) is null) {
            return null;
        }

        fs.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var kept = head.OpenHead();
        fs.WriteFromStream(kept, kept.Length, destination, null, null, ct);
        log.Info($"Extract (head for the preview): {entry.FullPath}, {kept.Length} of {length} bytes -> {destination}");

        return destination;
    }

    /// <summary>Where <see cref="CopyHead"/> puts the head of <paramref name="source"/> of <paramref name="length"/> bytes.</summary>
    public static string HeadPathFor(string source, long length) {
        return Path.Combine(TempFiles.FolderFor(source), "head-" + length.ToString(CultureInfo.InvariantCulture), NameOf(source));
    }


    private static TempCopyStamp? StampOf(IFileSystem fs, string path) {
        return fs.GetEntry(path) is { Kind: EntryKind.File, Size: { } size } entry
            ? new TempCopyStamp(size, entry.ModifiedUtc)
            : null;
    }

    private static string NameOf(string path) {
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}


/// <summary>A temporary copy's size and time of last change.</summary>
public readonly record struct TempCopyStamp(long Size, DateTime ModifiedUtc);

/// <summary>What a temporary copy is, before it is made again (<see cref="TempExtraction.Judge"/>).</summary>
public enum TempCopyState {
    /// <summary>No copy there.</summary>
    Missing,
    /// <summary>As this session made it.</summary>
    Fresh,
    /// <summary>Changed since this session made it: edited in whatever it was opened in.</summary>
    Edited,
    /// <summary>Not made this session - left by an earlier one; replaced as before.</summary>
    Unrecorded,
}

/// <summary>
/// Where a temporary copy landed, and whether an edited copy that was there
/// went to the bin to make room (<see cref="TempExtraction.CopyOutAsync(IShellNamespace, IFileSystem, IRecycleBin, ILogger, string, string, CancellationToken)"/>).
/// </summary>
public sealed record TempCopy(string Path, bool EditedCopyBinned);
