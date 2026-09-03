namespace Wander.Core.Navigation;

/// <summary>
/// One clickable segment of the address bar: what to show and where it
/// leads. <see cref="Path"/> is always a full path, not a fragment.
/// </summary>
public readonly record struct PathCrumb(string Label, string Path);


/// <summary>
/// Splits a path into breadcrumb segments, root first:
/// <c>D:\Dev\Wander</c> → <c>D:\</c> › <c>Dev</c> › <c>Wander</c>.
///
/// <para>
/// Pure string logic, no I/O — the address bar renders whatever it gets,
/// and whether a segment still exists on disk is the navigation guard's
/// business, not ours. The root keeps its full text as the label
/// (<c>D:\</c>, <c>\server\share</c>) because a bare drive letter or
/// share name tells the user nothing.
/// </para>
/// </summary>
public static class PathCrumbs {
    public static IReadOnlyList<PathCrumb> Split(string? path) {
        var crumbs = new List<PathCrumb>();
        if (string.IsNullOrWhiteSpace(path)) {
            return crumbs;
        }

        // The walk stops at the path root rather than at "no parent left":
        // Path.GetDirectoryName keeps peeling a UNC path down to the server
        // name and then to a bare separator, which are not places the user can go.
        string root = TrimTrailingSeparators(Path.GetPathRoot(path) ?? "");
        string current = path;
        while (true) {
            string trimmed = TrimTrailingSeparators(current);
            string? parent = Path.GetDirectoryName(trimmed);
            bool isRoot = string.IsNullOrEmpty(parent)
                || string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase);

            // Non-root segments show their own name; a shell sentinel
            // ("shell:RecycleBinFolder") has no separators at all and lands
            // here as a single root-ish crumb — the caller substitutes its
            // display name.
            string label = isRoot ? current : Path.GetFileName(trimmed);
            crumbs.Insert(0, new PathCrumb(string.IsNullOrEmpty(label) ? current : label, current));

            if (isRoot) {
                return crumbs;
            }
            current = parent!;
        }
    }


    /// <summary>
    /// The deepest segment of <paramref name="path"/> that still exists,
    /// or null when even its root is gone. What a bookmark pointing at
    /// "A:\B\C\D" opens the folder picker on once D has been deleted: C,
    /// or B if C went with it.
    ///
    /// <para>
    /// Takes the existence test rather than asking the disk: Core does not
    /// touch the filesystem directly, and a rule with no I/O in it is one a
    /// test can put any tree in front of.
    /// </para>
    /// </summary>
    public static string? NearestExisting(string? path, Func<string, bool> exists) {
        var crumbs = Split(path);
        for (int i = crumbs.Count - 1; i >= 0; i--) {
            if (exists(crumbs[i].Path)) {
                return crumbs[i].Path;
            }
        }

        return null;
    }


    private static string TrimTrailingSeparators(string path) {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }
}
