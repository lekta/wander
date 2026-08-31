namespace Wander.Core.FileSystem;

/// <summary>
/// The folders Windows keeps in the root of every volume for its own
/// bookkeeping. They are not user content, the user has no access to their
/// insides anyway (ACLs on <c>System Volume Information</c> deny even an
/// administrator without taking ownership), and touching them breaks
/// restore points or the recycle bin.
///
/// <para>
/// Distinct from <see cref="SystemPathGuard"/>: that one refuses to
/// <em>modify</em> critical paths, this one decides whether to <em>show</em>
/// them. Independent of the Hidden/System attribute switches too — someone
/// who turns those on wants to see their own hidden files, not
/// <c>$RECYCLE.BIN</c> in every drive listing.
/// </para>
///
/// <para>
/// Only the root of a volume is checked: a user folder that happens to be
/// called <c>Recovery</c> is ordinary content and stays visible.
/// </para>
/// </summary>
internal static class SystemRootFolders {
    private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase) {
        // Per-volume recycle bin store.
        "$RECYCLE.BIN",
        "RECYCLER",
        // Restore points, shadow copies, the indexer's per-volume database.
        "System Volume Information",
        // WinRE image and its servicing scratch space.
        "Recovery",
        "$WinREAgent",
        "$SysReset",
        // Windows Installer's rollback scratch during an install.
        "Config.Msi",
        // Feature-update staging, left behind for ten days after an upgrade.
        "$Windows.~BT",
        "$Windows.~WS",
        "$GetCurrent",
        // Delivery Optimization / Windows Update download cache on data drives.
        "$Windows.~LS",
    };


    /// <summary>
    /// True when <paramref name="path"/> is one of the well-known system
    /// folders sitting directly in a volume root.
    /// </summary>
    public static bool IsSystemRoot(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        string full;
        try {
            full = TrimSeparators(Path.GetFullPath(path));
        } catch {
            return false;
        }

        string? parent = Path.GetDirectoryName(full);
        if (parent is null) {
            return false;
        }

        // The parent must itself be a volume root: "C:\" for a drive letter,
        // "\\server\share\" for a UNC path. Path.GetPathRoot answers both.
        string? root = Path.GetPathRoot(full);
        if (root is null || !TrimSeparators(parent).Equals(TrimSeparators(root), StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return _names.Contains(Path.GetFileName(full));
    }


    private static string TrimSeparators(string path) {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
