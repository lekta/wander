namespace Wander.Core.FileSystem;

/// <summary>
/// Deny-list for destructive operations (delete, move away, rename,
/// replace-overwrite) on system-critical paths. ACLs and UAC already stop
/// most of this, but an elevated Wander would happily recycle
/// <c>C:\Windows</c> — this guard makes such operations fail fast with a
/// clear reason instead.
///
/// <para>
/// Blocked: drive roots, the special folders themselves (Windows, Program
/// Files (x86/x64), ProgramData, the Users folder, the current user's
/// profile root) and everything inside the Windows directory — that tree
/// never holds user content. Contents of Program Files / other profiles
/// are intentionally NOT blocked (ordinary uninstall-leftover cleanup);
/// a warn-instead-of-block tier for those is a possible later step.
/// </para>
///
/// <para>
/// Pure function of the input path and machine environment — no locator,
/// no I/O beyond path normalization — so both FileOperationService and
/// BatchExecutor call it statically and tests hit it directly.
/// </para>
/// </summary>
public static class SystemPathGuard {
    private static readonly Lazy<IReadOnlyList<string>> _protectedRoots = new(BuildProtectedRoots);
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

        if (IsDriveRoot(norm)) {
            reason = $"'{path}' is a drive root and cannot be moved or deleted";
            return true;
        }

        foreach (string root in _protectedRoots.Value) {
            if (norm.Equals(root, StringComparison.OrdinalIgnoreCase)) {
                reason = $"'{path}' is a protected system location";
                return true;
            }
        }

        if (_windowsTree.Value is { } win
            && norm.StartsWith(win + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            reason = $"'{path}' is inside the Windows system directory";
            return true;
        }

        return false;
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

    private static bool IsDriveRoot(string normalized) {
        // After trimming separators a drive root is exactly "C:".
        return normalized.Length == 2 && normalized[1] == ':';
    }
}
