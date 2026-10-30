namespace Wander.Core.Navigation;

/// <summary>
/// The order of bookmarks in their section of the panel: what moving one
/// does, and what a saved order means once the list it was saved for has
/// changed. Works on the section's identifiers, whatever they are - paths of
/// the user's own, keys of the built-in ones.
/// </summary>
public static class BookmarkOrder {
    /// <summary>
    /// The saved order over what exists now: the saved ones that still do,
    /// then the rest in their usual order - a built-in row a newer build
    /// added has a place without anybody having saved one.
    /// </summary>
    public static List<string> Merge(IReadOnlyList<string> known, IEnumerable<string>? saved) {
        var merged = new List<string>();
        foreach (string item in saved ?? Array.Empty<string>()) {
            if (known.Contains(item, StringComparer.OrdinalIgnoreCase) && !merged.Contains(item, StringComparer.OrdinalIgnoreCase)) {
                merged.Add(item);
            }
        }
        merged.AddRange(known.Where(item => !merged.Contains(item, StringComparer.OrdinalIgnoreCase)));

        return merged;
    }


    /// <summary>
    /// Moves <paramref name="item"/> by <paramref name="delta"/> rows, counting
    /// only the rows <paramref name="counts"/> lets through: in the panel a
    /// press moves past the next row on screen, not past a hidden one nobody
    /// sees go by.
    /// </summary>
    /// <returns>False when nothing moved: no such item, or no row to pass.</returns>
    public static bool Move(List<string> list, string item, int delta, Func<string, bool> counts) {
        int from = list.FindIndex(other => string.Equals(other, item, StringComparison.OrdinalIgnoreCase));
        if (from < 0 || delta == 0) {
            return false;
        }

        int to = from;
        for (int left = Math.Abs(delta); left > 0;) {
            to += Math.Sign(delta);
            if (to < 0 || to >= list.Count) {
                return false;
            }
            if (counts(list[to])) {
                left--;
            }
        }

        string moved = list[from];
        list.RemoveAt(from);
        list.Insert(to, moved);

        return true;
    }
}
