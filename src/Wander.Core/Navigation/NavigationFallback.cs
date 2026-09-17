using Wander.Core.FileSystem;

namespace Wander.Core.Navigation;

/// <summary>
/// Where the listing goes when a delete took away the folder on screen, or
/// one above it: to the nearest ancestor that is still there, instead of a
/// "folder is gone" panel over a path the user removed on purpose.
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
            candidate = Path.GetDirectoryName(
                candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return candidate;
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
}
