namespace Wander.Core.Undo;

/// <summary>
/// A folder an undo would move items back into that is no longer there.
/// </summary>
/// <param name="Folder">Where the items came from.</param>
/// <param name="Items">What would go back into it, as it would be named there, in the order the undo reaches them.</param>
public sealed record MissingDestination(string Folder, IReadOnlyList<string> Items);


/// <summary>
/// Where an undo puts things back, asked before it runs (PLAN K, decision
/// of 2026-09-29: the folder an undo returns items to was deleted for good -
/// ask where to put them instead, and say why it is asked). Without the
/// question a move back failed "path not found" and the step was lost.
/// </summary>
public static class UndoDestinations {
    /// <summary>
    /// The folders <paramref name="action"/> would move items back into that
    /// are gone, in the order the undo reaches them. Left out: a folder the
    /// undo itself brings back before it is needed (a merge's emptied source
    /// out of the bin); a folder on a drive that is not there - the drive
    /// comes back with it, that is a "path not found" to report, not a
    /// question; a rename, which stays in its folder.
    /// </summary>
    /// <param name="action">What the next <c>Ctrl+Z</c> takes back.</param>
    /// <param name="folderExists">Whether a folder is on disk; asked once per folder.</param>
    public static IReadOnlyList<MissingDestination> Missing(IUndoableAction action, Func<string, bool> folderExists) {
        var known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var back = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        // Last step first - the order the undo runs in.
        var steps = action.Steps;
        for (int i = steps.Count - 1; i >= 0; i--) {
            foreach (var (from, to) in steps[i].MovesOnUndo) {
                string? folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(to));
                if (string.IsNullOrEmpty(folder) || SameFolder(folder, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(from)))) {
                    continue;
                }
                if (Exists(folder) || BroughtBack(folder) || Path.GetPathRoot(folder) is not { Length: > 0 } root || !Exists(root)) {
                    continue;
                }

                if (!items.TryGetValue(folder, out var list)) {
                    list = new List<string>();
                    items[folder] = list;
                    order.Add(folder);
                }
                list.Add(to);
            }

            foreach (var (_, to) in steps[i].MovesOnUndo) {
                back.Add(Path.TrimEndingDirectorySeparator(to));
            }
            foreach (string path in steps[i].PathsAfterUndo) {
                back.Add(Path.TrimEndingDirectorySeparator(path));
            }
        }

        return order.Select(f => new MissingDestination(f, items[f])).ToList();

        bool Exists(string folder) {
            if (!known.TryGetValue(folder, out bool exists)) {
                exists = folderExists(folder);
                known[folder] = exists;
            }

            return exists;
        }

        // The folder, or one above it, comes back with an earlier step of the undo.
        bool BroughtBack(string folder) {
            for (string? f = folder; !string.IsNullOrEmpty(f); f = Path.GetDirectoryName(f)) {
                if (back.Contains(f)) {
                    return true;
                }
            }

            return false;
        }
    }


    private static bool SameFolder(string a, string? b) {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
