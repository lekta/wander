using Wander.Core.FileSystem;

namespace Wander.Core.Shell;

/// <summary>
/// Read-only access to Windows shell namespaces (Recycle Bin, in the
/// current iteration; later potentially This PC's "Computer" virtual
/// folder, Libraries, etc).
///
/// Shell namespaces look like folders to the user but are not real
/// filesystem paths — their contents are <c>IShellItem</c>s rather than
/// directory entries, and <see cref="IFileSystem"/> operations like
/// <c>DirectoryExists</c> or <c>Enumerate</c> don't work on them.
/// Callers must guard with <see cref="IsShellPath"/> before deciding
/// which back end to consult.
///
/// Currently exposes enumeration only — restore / empty / delete on
/// shell items will be added once the broader bin-management story is
/// designed (see PLAN.md D5).
/// </summary>
public interface IShellNamespace {
    /// <summary>True for paths this provider recognises and can enumerate.</summary>
    bool IsShellPath(string path);

    /// <summary>
    /// Lists the contents of <paramref name="shellPath"/> as filesystem-shaped
    /// entries. Children's <see cref="FileSystemEntry.FullPath"/> may be real
    /// on-disk paths (when the shell item happens to have a backing file —
    /// the case for every Recycle Bin entry, which lives inside
    /// <c>C:\$Recycle.Bin\…</c>) or other shell sentinels. They are stable
    /// enough to feed into the icon provider, but not necessarily into
    /// <see cref="IFileSystem"/> operations.
    /// </summary>
    IReadOnlyList<FileSystemEntry> Enumerate(string shellPath);

    /// <summary>
    /// Human-readable label for the namespace itself (e.g. "Корзина" for
    /// the Recycle Bin root). Used for window title and similar surfaces
    /// where falling back to <c>Path.GetFileName</c> on a sentinel string
    /// produces gibberish.
    /// </summary>
    string? GetDisplayName(string shellPath);
}


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
