namespace Wander.Core.Preview;

/// <summary>
/// Finding text in what the preview pane shows (PLAN B6): where the query
/// occurs, and which occurrence Enter and Shift+Enter go to. Case is
/// ignored, as in the content search that may have brought the file here.
/// </summary>
public static class TextFind {
    /// <summary>
    /// Past this the count says "at least": a one-letter query in a
    /// megabyte of log is not a question anybody wants all the answers to.
    /// </summary>
    public const int MaxMatches = 10_000;


    /// <summary>Start offsets of every occurrence, non-overlapping, in order; empty for an empty query.</summary>
    public static IReadOnlyList<int> All(string text, string query) {
        var found = new List<int>();
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) {
            return found;
        }

        int at = 0;
        while (found.Count < MaxMatches) {
            int hit = text.IndexOf(query, at, StringComparison.OrdinalIgnoreCase);
            if (hit < 0) {
                break;
            }
            found.Add(hit);
            at = hit + query.Length;
        }

        return found;
    }


    /// <summary>
    /// The occurrence to show first: the first one at or after the caret,
    /// or the first of all when the caret is past the last. -1 when there
    /// are none.
    /// </summary>
    public static int FirstFrom(IReadOnlyList<int> matches, int caret) {
        if (matches.Count == 0) {
            return -1;
        }

        for (int i = 0; i < matches.Count; i++) {
            if (matches[i] >= caret) {
                return i;
            }
        }

        return 0;
    }


    /// <summary>The next occurrence after <paramref name="current"/>, or the one before it, round the end.</summary>
    public static int Step(int current, int count, bool backwards) {
        if (count <= 0) {
            return -1;
        }
        if (current < 0) {
            return backwards ? count - 1 : 0;
        }

        return backwards ? (current - 1 + count) % count : (current + 1) % count;
    }
}
