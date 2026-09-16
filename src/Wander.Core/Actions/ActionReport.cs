using Wander.Core.FileSystem;
using Wander.Core.Localization;

namespace Wander.Core.Actions;

/// <summary>
/// What the user is told after a run in which something did not work: a
/// line per item that failed or was cancelled, the file's name first, then
/// the exit code and the last line the program wrote to stderr - usually
/// the one that says why. Capped, with the rest counted: the full tails
/// are in the log.
/// </summary>
public static class ActionReport {
    public const int DefaultLimit = 20;


    /// <summary>Whether the run needs a report at all - anything but a clean success.</summary>
    public static bool IsNeeded(IReadOnlyList<ActionItemResult> results) {
        return results.Any(r => r.Status != BatchItemStatus.Ok);
    }


    /// <param name="text">For tests, which pass their own templates; the app's string table otherwise.</param>
    /// <param name="oneCommand">
    /// The run was one process over the whole selection, so every item
    /// carries the one outcome: it is said once, with the count, not once
    /// per file.
    /// </param>
    public static IReadOnlyList<string> Lines(
        IReadOnlyList<ActionItemResult> results, int limit = DefaultLimit, ITextSource? text = null,
        bool oneCommand = false) {

        string Say(string key) => text is null ? Text.Get(key) : text.Get(key);
        string Fill(string key, params object[] args) {
            try {
                return string.Format(Say(key), args);
            } catch (FormatException) {
                return Say(key);
            }
        }

        string Line(ActionItemResult result) {
            string name = NameOf(result.Path);
            if (result.Status == BatchItemStatus.Cancelled) {
                return Fill("ActionReportCancelled", name);
            }
            if (result.Error is not null) {
                return Fill("ActionReportError", name, result.Error.Message);
            }

            string last = LastLine(result.ErrorTail);

            return last.Length > 0
                ? Fill("ActionReportExitCode", name, result.ExitCode, last)
                : Fill("ActionReportExitCodeOnly", name, result.ExitCode);
        }

        var bad = results.Where(r => r.Status != BatchItemStatus.Ok).ToList();
        if (oneCommand && bad.Count > 1) {
            return new[] { Fill("ActionReportOnce", bad.Count, Line(bad[0])) };
        }

        var lines = bad.Take(limit).Select(Line).ToList();
        if (bad.Count > limit) {
            lines.Add(Fill("ActionReportMore", bad.Count - limit));
        }

        return lines;
    }


    /// <summary>The last non-empty line of a program's stderr tail.</summary>
    public static string LastLine(string tail) {
        int end = tail.TrimEnd().LastIndexOf('\n');

        return (end < 0 ? tail : tail[(end + 1)..]).Trim();
    }


    private static string NameOf(string path) {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

        return name.Length > 0 ? name : path;
    }
}
