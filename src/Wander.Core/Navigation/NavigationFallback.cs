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


    /// <summary>
    /// Where the listing goes when the volume under it went away - a flash
    /// drive pulled out with a folder on it open: the most recent place
    /// that is on a volume still there, else the first of the machine's own
    /// drives. Null when the folder on screen is not on a drive letter, or
    /// its volume is still there.
    /// </summary>
    /// <param name="roots">The drive roots there now (<c>C:\</c>), in drive order.</param>
    /// <param name="recent">The places visited, most recent first.</param>
    /// <param name="kindOf">What kind a root is: the fallback is a fixed drive, not another flash drive.</param>
    public static string? AfterVolumeRemoved(
        string? current, IReadOnlyList<string> roots, IReadOnlyList<string> recent, Func<string, VolumeKind> kindOf) {
        if (current is null || DriveOf(current) is not { } gone || roots.Any(r => SameRoot(r, gone))) {
            return null;
        }

        foreach (string place in recent) {
            if (DriveOf(place) is { } root && roots.Any(r => SameRoot(r, root))) {
                return place;
            }
        }

        return roots.FirstOrDefault(r => kindOf(r) == VolumeKind.Fixed) ?? roots.FirstOrDefault();
    }


    /// <summary>
    /// The same for a phone or a camera that went away with a folder on it
    /// open (PLAN Y4b): the most recent place still there - on a drive
    /// still there, or on a device still plugged in - else the first of the
    /// machine's own drives. Null when the folder on screen is not on a
    /// device, or its device is still there.
    /// </summary>
    /// <param name="deviceThere">Whether the device a path is on is plugged in.</param>
    public static string? AfterDeviceRemoved(
        string? current, Func<string, bool> deviceThere, IReadOnlyList<string> roots, IReadOnlyList<string> recent,
        Func<string, VolumeKind> kindOf) {
        if (current is null || !DevicePath.Is(current) || deviceThere(current)) {
            return null;
        }

        foreach (string place in recent) {
            bool there = DevicePath.Is(place)
                ? deviceThere(place)
                : DriveOf(place) is { } root && roots.Any(r => SameRoot(r, root));
            if (there) {
                return place;
            }
        }

        return roots.FirstOrDefault(r => kindOf(r) == VolumeKind.Fixed) ?? roots.FirstOrDefault();
    }


    /// <summary>"E:\" for a path on a drive letter, null for a share, a shell place or anything else.</summary>
    private static string? DriveOf(string path) {
        return path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
            ? char.ToUpperInvariant(path[0]) + @":\"
            : null;
    }

    private static bool SameRoot(string a, string b) {
        return string.Equals(DriveOf(a), DriveOf(b), StringComparison.OrdinalIgnoreCase);
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
