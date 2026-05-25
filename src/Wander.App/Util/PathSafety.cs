using System.IO;

namespace Wander.App.Util;

public enum SelfDropReason {
    None,
    Same,
    AlreadyInTarget,
    IntoOwnDescendant,
}

/// <summary>
/// Pre-flight checks shared by drag/drop and paste: deciding whether a copy/move
/// makes sense before we touch the filesystem, and formatting the user-facing
/// reason text.
/// </summary>
public static class PathSafety {
    public static SelfDropReason DetectSelfDrop(IReadOnlyList<string> sources, string target, out string? offender) {
        offender = null;
        string targetNorm = Normalize(target);

        foreach (string p in sources) {
            string pNorm = Normalize(p);

            if (string.Equals(pNorm, targetNorm, StringComparison.OrdinalIgnoreCase)) {
                offender = pNorm;
                return SelfDropReason.Same;
            }

            string parent = Normalize(Path.GetDirectoryName(pNorm) ?? "");
            if (string.Equals(parent, targetNorm, StringComparison.OrdinalIgnoreCase)) {
                offender = pNorm;
                return SelfDropReason.AlreadyInTarget;
            }

            string prefix = pNorm + Path.DirectorySeparatorChar;
            if (targetNorm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                offender = pNorm;
                return SelfDropReason.IntoOwnDescendant;
            }
        }
        return SelfDropReason.None;
    }

    public static string FormatReason(SelfDropReason reason, string? offender, string target) {
        string offenderName = offender is null ? "this" : Path.GetFileName(Normalize(offender));
        string targetName = Path.GetFileName(Normalize(target));
        if (string.IsNullOrEmpty(targetName)) {
            targetName = target;
        }

        return reason switch {
            SelfDropReason.Same =>
                $"Cannot move '{offenderName}' onto itself",
            SelfDropReason.AlreadyInTarget =>
                $"'{offenderName}' is already in '{targetName}'",
            SelfDropReason.IntoOwnDescendant =>
                $"Cannot move '{offenderName}' into its own subfolder '{targetName}'",
            _ => "Cannot drop here",
        };
    }


    private static string Normalize(string path) {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
