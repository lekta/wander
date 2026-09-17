namespace Wander.Core.FileSystem;

/// <summary>
/// A folder moved or was renamed: where do the paths that were under it
/// live now? One rule for everything that remembers a path - the folder on
/// screen, the history behind it, a bookmark - so they all follow the same
/// way, or not at all.
/// </summary>
public static class PathRewrite {
    /// <summary>
    /// <paramref name="path"/> with <paramref name="oldRoot"/> replaced by
    /// <paramref name="newRoot"/>, or null when the path is neither the old
    /// root nor inside it. Trailing separators do not matter, and neither
    /// does case; a sibling that merely starts with the same letters
    /// ("photos-old" next to "photos") is not inside.
    /// </summary>
    public static string? Under(string? path, string oldRoot, string newRoot) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        string trimmed = Trim(path);
        string old = Trim(oldRoot);
        if (old.Length == 0) {
            return null;
        }
        if (string.Equals(trimmed, old, StringComparison.OrdinalIgnoreCase)) {
            return Trim(newRoot);
        }

        string prefix = old + Path.DirectorySeparatorChar;
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return Path.Combine(Trim(newRoot), trimmed[prefix.Length..]);
    }


    private static string Trim(string path) {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
