using Wander.Core.Localization;

namespace Wander.Core.FileSystem;

/// <summary>
/// Deny-list for destructive operations (delete, move away, rename,
/// replace-overwrite) on system-critical paths. ACLs and UAC already stop
/// most of this, but an elevated Wander would happily recycle
/// <c>C:\Windows</c> — this guard makes such operations fail fast with a
/// clear reason instead.
///
/// <para>
/// Blocked: drive roots and network share roots, the special folders
/// themselves (Windows, Program Files (x86/x64), ProgramData, the Users
/// folder, the current user's profile root), the user's own folders
/// Windows keeps in the profile (Desktop, Documents, Downloads, Pictures,
/// Music, Videos, AppData and the three inside it - decided 2026-09-24:
/// not ours to move or delete) and everything inside the Windows
/// directory - that tree never holds user content. What is inside the
/// user's folders is theirs as usual. Contents of Program Files / other
/// profiles are intentionally NOT blocked (ordinary uninstall-leftover
/// cleanup); a warn-instead-of-block tier for those is a possible later
/// step.
/// </para>
///
/// <para>
/// Writing something new into a folder is another question, answered by
/// <see cref="MayWriteInto"/>: only the Windows tree says no. A drive root
/// or the profile is where extracting and an action's output go every day.
/// </para>
///
/// <para>
/// A function of the input path and the machine's layout, no I/O beyond
/// path normalization - so both FileOperationService and BatchExecutor
/// call it statically and tests hit it directly. The one thing asked of the
/// locator is where the user's folders are (<see cref="IKnownFolders"/>,
/// once): Downloads has no <c>Environment.SpecialFolder</c>. Without it -
/// in the tests - the rest come from <c>Environment</c>.
/// </para>
/// </summary>
internal static class SystemPathGuard {
    private static readonly Lazy<IReadOnlyList<string>> _protectedRoots = new(BuildProtectedRoots);
    private static readonly Lazy<IReadOnlyList<string>> _userFolders = new(BuildUserFolders);
    private static readonly Lazy<string?> _windowsTree = new(
        () => NormalizeOrNull(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));


    /// <summary>
    /// True when <paramref name="path"/> must not be destructively touched;
    /// <paramref name="reason"/> then carries a user-displayable sentence.
    /// </summary>
    public static bool IsProtected(string path, out string reason) {
        reason = "";
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        string norm;
        try {
            norm = Normalize(path);
        } catch {
            // Unparseable path — let the actual file operation produce its
            // own error rather than mislabeling it as "protected".
            return false;
        }

        if (IsRoot(norm)) {
            reason = norm.StartsWith(@"\\", StringComparison.Ordinal)
                ? Text.Format("GuardShareRoot", path)
                : Text.Format("GuardDriveRoot", path);
            return true;
        }

        foreach (string root in _protectedRoots.Value) {
            if (norm.Equals(root, StringComparison.OrdinalIgnoreCase)) {
                reason = Text.Format("GuardSystemFolder", path);
                return true;
            }
        }

        foreach (string folder in _userFolders.Value) {
            if (norm.Equals(folder, StringComparison.OrdinalIgnoreCase)) {
                reason = Text.Format("GuardUserFolder", path);
                return true;
            }
        }

        if (InWindowsTree(norm, orItself: false)) {
            reason = Text.Format("GuardWindowsTree", path);
            return true;
        }

        return false;
    }


    /// <summary>
    /// True when something new may be written into <paramref name="folder"/> -
    /// an extraction, an action's output. Everywhere but the Windows tree,
    /// the Windows folder itself included: a drive root is where "extract
    /// here" on a flash drive goes, and <see cref="IsProtected"/> is about
    /// taking the folder itself away, not about adding to it.
    /// </summary>
    public static bool MayWriteInto(string folder, out string reason) {
        reason = "";
        if (string.IsNullOrWhiteSpace(folder)) {
            return true;
        }

        string norm;
        try {
            norm = Normalize(folder);
        } catch {
            // As above: the write itself will say what is wrong with it.
            return true;
        }

        if (InWindowsTree(norm, orItself: true)) {
            reason = Text.Format("GuardWindowsTree", folder);
            return false;
        }

        return true;
    }


    private static bool InWindowsTree(string normalized, bool orItself) {
        if (_windowsTree.Value is not { } win) {
            return false;
        }

        return (orItself && normalized.Equals(win, StringComparison.OrdinalIgnoreCase))
            || normalized.StartsWith(win + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildProtectedRoots() {
        var roots = new List<string>();

        void Add(string? raw) {
            if (NormalizeOrNull(raw) is { } norm && !roots.Contains(norm, StringComparer.OrdinalIgnoreCase)) {
                roots.Add(norm);
            }
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(profile);
        // The folder holding all profiles (typically C:\Users).
        if (!string.IsNullOrEmpty(profile)) {
            Add(Path.GetDirectoryName(profile));
        }

        return roots;
    }

    /// <summary>
    /// The user's folders Windows keeps: where they are now, moved or not.
    /// The shell's own answer where there is one (<see cref="IKnownFolders"/>
    /// knows Downloads); <c>Environment</c> for the rest, and for all of them
    /// when nothing is registered.
    /// </summary>
    private static IReadOnlyList<string> BuildUserFolders() {
        var folders = new List<string>();

        void Add(string? raw) {
            if (NormalizeOrNull(raw) is { } norm && !folders.Contains(norm, StringComparer.OrdinalIgnoreCase)) {
                folders.Add(norm);
            }
        }

        if (ServiceLocator.TryGet<IKnownFolders>() is { } known) {
            Add(known.GetDesktop());
            Add(known.GetDocuments());
            Add(known.GetDownloads());
            Add(known.GetPictures());
            Add(known.GetMusic());
            Add(known.GetVideos());
        }
        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        // AppData and the three in it. Roaming can be redirected to a
        // server; Local never is, so AppData is taken as Local's parent,
        // and LocalLow - it has no SpecialFolder - sits beside Local.
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(local);
        if (!string.IsNullOrEmpty(local) && Path.GetDirectoryName(local) is { Length: > 0 } appData) {
            Add(appData);
            Add(Path.Combine(appData, "LocalLow"));
        }

        return folders;
    }

    private static string Normalize(string path) {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? NormalizeOrNull(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }
        try {
            return Normalize(path);
        } catch {
            return null;
        }
    }

    private static bool IsRoot(string normalized) {
        // After trimming separators a root is whatever the path's own root
        // trims to: "C:" for a drive, "\\server\share" for a network share.
        string root = (Path.GetPathRoot(normalized) ?? "")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return root.Length > 0 && root.Equals(normalized, StringComparison.OrdinalIgnoreCase);
    }
}
