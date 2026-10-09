namespace Wander.Core.Search;

/// <summary>
/// Where a found item is, said from the folder the search started in: the
/// part of the path the results do not share. A result list of
/// <c>D:\Photos\2024\...</c> said in full is the same prefix on every row,
/// and the column cut the useful end off. Not under that folder - a path
/// from somewhere else - it is said in full.
/// </summary>
public static class ResultPath {
    /// <summary>The item's path from <paramref name="root"/>: <c>2024\May\IMG_0001.CR3</c>.</summary>
    public static string Relative(string? root, string path) {
        string trimmed = Trim(path);
        if (string.IsNullOrEmpty(root)) {
            return trimmed;
        }

        string prefix = Trim(root);
        if (!prefix.EndsWith(Path.DirectorySeparatorChar)) {
            prefix += Path.DirectorySeparatorChar;
        }

        return trimmed.Length > prefix.Length && trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }

    /// <summary>The folder the item is in, from <paramref name="root"/>: <c>2024\May</c>; empty for an item right in it.</summary>
    public static string Folder(string? root, string path) {
        string? parent = Path.GetDirectoryName(Trim(path));
        if (parent is null) {
            return "";
        }
        if (root is not null && string.Equals(Trim(parent), Trim(root), StringComparison.OrdinalIgnoreCase)) {
            return "";
        }

        return Relative(root, parent);
    }


    private static string Trim(string path) {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // "C:" alone is the current folder on that drive, not its root.
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + Path.DirectorySeparatorChar : trimmed;
    }
}
