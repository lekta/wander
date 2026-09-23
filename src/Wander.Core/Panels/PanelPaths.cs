namespace Wander.Core.Panels;

/// <summary>
/// Paths as the folder panels compare them: without case, without a
/// trailing separator - "C:\" and "C:" are the same drive, "D:\Photos\" and
/// "d:\photos" the same folder.
/// </summary>
public static class PanelPaths {
    /// <summary>The same path, or both null.</summary>
    public static bool Same(string? a, string? b) {
        return a is null || b is null
            ? a is null && b is null
            : string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A path as a key: the trailing separators gone.</summary>
    public static string Key(string path) {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// A key back as a path to read: a drive's root gets its separator back -
    /// "C:" alone names the current folder of drive C, not its root.
    /// </summary>
    public static string FromKey(string key) {
        return key.Length == 2 && key[1] == Path.VolumeSeparatorChar ? key + Path.DirectorySeparatorChar : key;
    }

    /// <summary><paramref name="path"/> is <paramref name="folder"/> or somewhere inside it.</summary>
    public static bool IsUnderOrSelf(string path, string folder) {
        string p = Key(path);
        string f = Key(folder);
        if (f.Length == 0) {
            return false;
        }

        return string.Equals(p, f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><paramref name="path"/> is strictly inside <paramref name="folder"/>.</summary>
    public static bool IsInside(string path, string folder) {
        return IsUnderOrSelf(path, folder) && !Same(path, folder);
    }

    /// <summary>The folder above, or null at a root and for a shell path.</summary>
    public static string? Parent(string path) {
        string key = Key(path);
        if (key.Length == 0 || key.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return Path.GetDirectoryName(key) is { Length: > 0 } parent ? parent : null;
    }

    /// <summary>
    /// The chain from <paramref name="root"/> down to <paramref name="path"/>,
    /// both included, top first. Empty when the path is not under the root.
    /// </summary>
    public static IReadOnlyList<string> Chain(string root, string path) {
        if (!IsUnderOrSelf(path, root)) {
            return Array.Empty<string>();
        }

        var chain = new List<string>();
        for (string? step = path; step is not null && IsUnderOrSelf(step, root); step = Parent(step)) {
            chain.Add(step);
            if (Same(step, root)) {
                break;
            }
        }
        chain.Reverse();

        return chain;
    }

    /// <summary>
    /// <paramref name="path"/> after the folder <paramref name="from"/> became
    /// <paramref name="to"/>; unchanged when it was not inside it.
    /// </summary>
    public static string Follow(string path, string from, string to) {
        if (!IsUnderOrSelf(path, from)) {
            return path;
        }

        string rest = Key(path)[Key(from).Length..];

        return Key(to) + rest;
    }
}
