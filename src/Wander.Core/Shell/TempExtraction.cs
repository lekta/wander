using Wander.Core.FileSystem;
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
/// departure is written down in ARCHITECTURE.md.
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
    /// <summary>
    /// Copies <paramref name="source"/> - a path inside an archive - into
    /// <paramref name="tempFolder"/> and returns where it landed. An
    /// earlier copy under the same name is replaced: the archive may have
    /// been rebuilt since, and a stale copy would be shown as the current
    /// one.
    /// </summary>
    public static async Task<string> CopyOutAsync(
        IShellNamespace ns, IFileSystem fs, ILogger log,
        string source, string tempFolder, CancellationToken ct) {

        fs.CreateDirectory(tempFolder);
        string destination = Path.Combine(tempFolder, NameOf(source));
        if (fs.FileExists(destination)) {
            fs.DeleteFile(destination);
        }

        await ns.CopyOut(new[] { new CopyOutItem(source) }, tempFolder, null, ct).ConfigureAwait(false);
        log.Info($"Extract (temporary copy): {source} -> {destination}");

        return destination;
    }

    /// <summary>The same, into the folder that belongs to this source.</summary>
    public static Task<string> CopyOutAsync(
        IShellNamespace ns, IFileSystem fs, ILogger log, string source, CancellationToken ct) {
        return CopyOutAsync(ns, fs, log, source, TempFiles.FolderFor(source), ct);
    }

    /// <summary>
    /// Where the overload above puts the copy of <paramref name="source"/>,
    /// whether or not it has been made yet: a caller can look before it asks.
    /// </summary>
    public static string CopyPathFor(string source) {
        return Path.Combine(TempFiles.FolderFor(source), NameOf(source));
    }


    private static string NameOf(string path) {
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
