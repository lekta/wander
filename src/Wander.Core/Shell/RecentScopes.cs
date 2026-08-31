namespace Wander.Core.Shell;

/// <summary>
/// The last few file types the user opened a context menu on.
///
/// <para>
/// It exists for one screen: the "Добавить" picker in the context-menu
/// settings. There are around eight hundred registered extensions on a
/// normal machine and no useful way to rank them — except that somebody who
/// has just spent an hour right-clicking <c>.psd</c> files and has now come
/// to this page is almost certainly here about <c>.psd</c>. So the picker
/// leads with what was actually browsed instead of with the alphabet.
/// </para>
///
/// <para>
/// Most-recent-first, de-duplicated, capped. Cheap to keep and worthless to
/// protect: losing it costs one scroll.
/// </para>
/// </summary>
public static class RecentScopes {
    /// <summary>
    /// How many to remember. Small on purpose — the point is "what I was
    /// just doing", and a list of twenty is a list, not a shortcut.
    /// </summary>
    public const int Max = 5;


    public static IReadOnlyList<string> Add(IReadOnlyList<string> current, string? scope) {
        if (string.IsNullOrEmpty(scope)) {
            return current;
        }

        var result = new List<string>(current.Count + 1) { scope };
        foreach (string existing in current) {
            if (result.Count >= Max) {
                break;
            }
            if (!string.Equals(existing, scope, StringComparison.OrdinalIgnoreCase)) {
                result.Add(existing);
            }
        }

        // Unchanged when it was already at the front: the caller persists on
        // a difference, and re-clicking the same file type should not write
        // state.json on every right-click.
        return result.SequenceEqual(current, StringComparer.OrdinalIgnoreCase) ? current : result;
    }
}
