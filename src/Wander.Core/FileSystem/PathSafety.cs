using Wander.Core.Localization;

namespace Wander.Core.FileSystem;

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
///
/// <para>
/// Pure string logic — no I/O, no WPF. Lives in <c>Wander.Core</c> so unit
/// tests can hit it directly and other entry points (CLI, scripting) can
/// share the same rules without dragging a UI dependency.
/// </para>
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

    /// <summary>
    /// Human-readable reason a drop was refused. <paramref name="text"/> is
    /// for tests, which pass their own templates rather than depend on a
    /// process-wide registration: the string table is global state, and a
    /// test that mutates it races every other test class.
    /// </summary>
    public static string FormatReason(
        SelfDropReason reason, string? offender, string target, ITextSource? text = null) {

        string Say(string key) => text is null ? Text.Get(key) : text.Get(key);
        string Fill(string key, params object[] args) {
            try {
                return string.Format(Say(key), args);
            } catch (FormatException) {
                return Say(key);
            }
        }

        string offenderName = offender is null ? Say("DropThis") : Path.GetFileName(Normalize(offender));
        string targetName = Path.GetFileName(Normalize(target));
        if (string.IsNullOrEmpty(targetName)) {
            targetName = target;
        }

        // Text comes from the app's string table through ITextSource —
        // Core has no reference to it by design.
        return reason switch {
            SelfDropReason.Same => Fill("DropOntoItself", offenderName),
            SelfDropReason.AlreadyInTarget => Fill("DropAlreadyThere", offenderName, targetName),
            SelfDropReason.IntoOwnDescendant => Fill("DropIntoOwnSubfolder", offenderName, targetName),
            _ => Say("DropNotAllowed"),
        };
    }


    private static string Normalize(string path) {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
