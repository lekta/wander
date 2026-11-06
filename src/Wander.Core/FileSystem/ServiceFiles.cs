namespace Wander.Core.FileSystem;

/// <summary>
/// The files Windows - and a Mac - leave in ordinary folders for their own
/// bookkeeping: a folder's look, a thumbnail cache, a window's state, a
/// file's Mac-only attributes. Not the user's content, wherever they lie;
/// unlike <see cref="SystemRootFolders"/>, which looks only at a volume root.
/// One switch hides both (<see cref="EntryVisibility"/>).
///
/// <para>
/// The attributes are not enough to go by: <c>desktop.ini</c> is hidden
/// and system when Explorer writes it, plain when an installer or a sync
/// client does, and <c>.DS_Store</c> carries neither.
/// </para>
/// </summary>
internal static class ServiceFiles {
    // A file's Mac-only attributes and resource fork, written beside it as
    // "._name" on any disk that cannot hold them (FAT, exFAT, NTFS, SMB).
    private const string AppleDoublePrefix = "._";

    private static readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase) {
        // A folder's icon, name and view (Explorer).
        "desktop.ini",
        // Thumbnail caches: Explorer's, still written on network shares,
        // and Media Center's.
        "Thumbs.db",
        "ehthumbs.db",
        "ehthumbs_vista.db",
        // Finder's window state, left on drives and in archives that
        // passed through a Mac.
        ".DS_Store",
    };

    // The Mac's archiver gathers the "._name" halves of a zip's files here.
    private static readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase) {
        "__MACOSX",
    };


    /// <summary>True when the entry is a file or a folder by one of the names above.</summary>
    public static bool Is(FileSystemEntry entry) {
        if (entry.Kind == EntryKind.Directory) {
            return IsFolderName(entry.Name);
        }

        return entry.Kind == EntryKind.File && IsFileName(entry.Name);
    }


    /// <summary>True when a file by this name is one of the above - for a walk that has names, not listing rows.</summary>
    public static bool IsFileName(string name) {
        return _files.Contains(name)
            || (name.Length > AppleDoublePrefix.Length && name.StartsWith(AppleDoublePrefix, StringComparison.Ordinal));
    }


    /// <summary>True when a folder by this name is one of the above.</summary>
    public static bool IsFolderName(string name) {
        return _folders.Contains(name);
    }
}
