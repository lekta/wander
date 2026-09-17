using Wander.Core.FileSystem;

namespace Wander.Core.Navigation;

/// <summary>
/// Where the listing goes when the folder it wants is gone: after a delete
/// took away the folder on screen, or one above it, and at the start of a
/// session whose last folder has since been moved. To the nearest ancestor
/// that is still there, instead of a "folder is gone" panel or a drive root.
/// </summary>
public static class NavigationFallback {
    /// <summary>
    /// The nearest ancestor of <paramref name="current"/> that none of
    /// <paramref name="deleted"/> covers, or null when the folder on screen
    /// was not touched. Case and trailing separators do not matter, and a
    /// sibling with the same prefix is not inside - the rule of
    /// <see cref="PathRewrite.Under"/>. Null also when even the root is
    /// covered, which the system path guard does not let happen.
    /// </summary>
    public static string? AfterDelete(IReadOnlyCollection<string> deleted, string? current) {
        if (current is null || !IsCovered(current, deleted)) {
            return null;
        }

        string? candidate = current;
        while (candidate is not null && IsCovered(candidate, deleted)) {
            candidate = ParentOf(candidate);
        }

        return candidate;
    }

    /// <summary>
    /// Where a place remembered from the last session - the folder that was
    /// open, a branch that was expanded - is reopened: the place itself
    /// while it is there, otherwise its nearest ancestor that is, but only
    /// on one of the machine's own drives. Anywhere else (a flash drive, a
    /// share, a disc, a drive letter that is no longer there) the answer is
    /// null: whatever carries that letter now is most likely another
    /// medium, and its folders are no place to reopen in.
    /// </summary>
    /// <param name="exists">Whether a place is there; asked from the path upwards.</param>
    /// <param name="kindOf">The volume under a path; asked only once the path itself is gone.</param>
    public static string? AfterRestore(string path, Func<string, bool> exists, Func<string, VolumeKind> kindOf) {
        if (exists(path)) {
            return path;
        }

        return kindOf(path) == VolumeKind.Fixed ? PathCrumbs.NearestExisting(path, exists) : null;
    }


    /// <summary>The path is one of the deleted ones or lies inside one.</summary>
    private static bool IsCovered(string path, IReadOnlyCollection<string> deleted) {
        foreach (string root in deleted) {
            // "Where would it be if the root moved onto itself": non-null
            // exactly when the path is the root or under it.
            if (PathRewrite.Under(path, root, root) is not null) {
                return true;
            }
        }

        return false;
    }

    private static string? ParentOf(string path) {
        return Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
