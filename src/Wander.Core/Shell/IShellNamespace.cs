using Wander.Core.FileSystem;

namespace Wander.Core.Shell;

/// <summary>
/// Read-only access to Windows shell namespaces: the Recycle Bin, and
/// archives browsed as folders (zip / 7z / rar / tar.gz and whatever else
/// the machine's shell claims - see <see cref="ParseArchive"/>).
///
/// Shell namespaces look like folders to the user but are not real
/// filesystem paths - their contents are <c>IShellItem</c>s rather than
/// directory entries, and <see cref="IFileSystem"/> operations like
/// <c>DirectoryExists</c> or <c>Enumerate</c> don't work on them.
/// Callers must guard with <see cref="IsShellPath"/> before deciding
/// which back end to consult.
///
/// Writing into a namespace is not offered and will not be: the Recycle
/// Bin has its own verb (restore, via <see cref="IRecycleBin"/>) and an
/// archive is read out with <see cref="CopyOut"/>, never written back.
/// </summary>
public interface IShellNamespace {
    /// <summary>True for paths this provider recognises and can enumerate.</summary>
    bool IsShellPath(string path);

    /// <summary>
    /// Lists the contents of <paramref name="shellPath"/> as filesystem-shaped
    /// entries. Children's <see cref="FileSystemEntry.FullPath"/> may be real
    /// on-disk paths (when the shell item happens to have a backing file -
    /// the case for every Recycle Bin entry, which lives inside
    /// <c>C:\$Recycle.Bin\...</c>), paths inside an archive, or other shell
    /// sentinels. They are stable enough to feed into the icon provider and
    /// back into this interface, but not necessarily into
    /// <see cref="IFileSystem"/> operations.
    /// </summary>
    IReadOnlyList<FileSystemEntry> Enumerate(string shellPath);

    /// <summary>
    /// Human-readable label for the namespace itself (e.g. "Корзина" for
    /// the Recycle Bin root). Used for window title and similar surfaces
    /// where falling back to <c>Path.GetFileName</c> on a sentinel string
    /// produces gibberish. Null for a path whose own text already reads
    /// correctly - every archive path does.
    /// </summary>
    string? GetDisplayName(string shellPath);

    /// <summary>
    /// The archive / inner-path split for <paramref name="path"/>, or null
    /// when it does not point into an archive. The one predicate for "this
    /// is an archive" in the whole codebase: no caller matches extensions
    /// itself, because which extensions count is a property of the machine
    /// (an association handed to 7-Zip stops being browsable) and lives
    /// behind this call.
    /// </summary>
    ArchivePath? ParseArchive(string path);

    /// <summary>
    /// True when <paramref name="path"/> can be listed as a folder. Unlike
    /// <see cref="IsShellPath"/> this may hit the disk (an archive has to
    /// be opened to answer), so callers run it off the UI thread.
    /// </summary>
    bool CanNavigate(string path);

    /// <summary>
    /// Copies items out of a namespace onto the real filesystem, using the
    /// shell's own copy engine - the only thing that can read the bytes of
    /// an <c>ArchiveFolder</c> entry.
    ///
    /// <para>
    /// Overwriting is not part of the contract: the caller clears the way
    /// first (recycle or rename) and passes
    /// <see cref="CopyOutItem.NewName"/> when the target name has to change.
    /// Cancellation stops the engine between items; whatever landed before
    /// that stays on disk and is the caller's to undo.
    /// </para>
    /// </summary>
    /// <param name="progress">Reports each item's path as it is finished.</param>
    Task CopyOut(
        IReadOnlyList<CopyOutItem> items,
        string targetFolder,
        IProgress<string>? progress,
        CancellationToken ct);

    /// <summary>
    /// The shell's own data object for <paramref name="paths"/> - what
    /// Explorer puts on the clipboard and into a drag when a selection is
    /// copied out of a zip. Handed back as <c>object</c> because it is a
    /// COM interface Core has no way to name: the caller either passes it
    /// straight to the platform (the clipboard) or lets WPF wrap it (a
    /// drag).
    ///
    /// <para>
    /// It exists for the paths a file list cannot carry. An entry inside an
    /// archive has no file another program could open, and a
    /// <c>CF_HDROP</c> naming it makes the receiver report a file that is
    /// not there. The shell's object carries item ids instead, and whoever
    /// takes the drop asks the shell for the bytes.
    /// </para>
    ///
    /// <para>
    /// Null when no object could be built - a path the shell does not
    /// recognise, an archive that has gone. The caller then falls back to
    /// the ordinary file list.
    /// </para>
    /// </summary>
    object? CreateDataObject(IReadOnlyList<string> paths);
}


/// <summary>One thing to copy out of a namespace.</summary>
/// <param name="Path">Full path of the source inside the namespace.</param>
/// <param name="NewName">Name to give the copy, or null to keep its own.</param>
public sealed record CopyOutItem(string Path, string? NewName = null);


/// <summary>
/// Sentinel paths recognised by every <see cref="IShellNamespace"/>
/// implementation. Kept as plain string constants (not GUIDs / CLSIDs)
/// so they survive JSON round-trips through <c>AppState</c> and can be
/// used directly as <c>TreeNodeViewModel.FullPath</c> values.
/// </summary>
public static class ShellPaths {
    /// <summary>The Recycle Bin root. Matches the well-known shell URI
    /// that <c>explorer.exe shell:RecycleBinFolder</c> accepts.</summary>
    public const string RecycleBin = "shell:RecycleBinFolder";
}


/// <summary>
/// The single question "does this path point into an archive?", asked
/// through whichever <see cref="IShellNamespace"/> is registered. A
/// convenience over <see cref="ServiceLocator"/> so the answer reads the
/// same in the view model, the icon provider and the preview pane, and so
/// a host with no shell namespace (tests) simply gets "no".
/// </summary>
public static class Archives {
    public static ArchivePath? Of(string? path) {
        return string.IsNullOrEmpty(path)
            ? null
            : ServiceLocator.TryGet<IShellNamespace>()?.ParseArchive(path);
    }

    /// <summary>True for a path inside an archive, the archive itself included.</summary>
    public static bool Contains(string? path) {
        return Of(path) is not null;
    }

    /// <summary>
    /// True for an entry <em>within</em> an archive, false for the archive
    /// file itself. The distinction matters everywhere a rule is about the
    /// contents rather than the container: a <c>pack.zip</c> sitting in an
    /// ordinary folder is an ordinary file, and gets an ordinary file's
    /// icon, preview and menu.
    /// </summary>
    public static bool Inside(string? path) {
        return Of(path) is { IsRoot: false };
    }
}
