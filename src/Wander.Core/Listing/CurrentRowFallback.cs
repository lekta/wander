using Wander.Core.FileSystem;

namespace Wander.Core.Listing;

/// <summary>
/// Which row becomes the current one when the current one has left the
/// folder - deleted here, deleted in the program it was opened in, moved
/// away.
///
/// <para>
/// The next surviving row in the order the folder stood in, so whatever
/// took the departed file's place on screen is what the keyboard is on; the
/// nearest surviving row before it when it was the last. One rule for every
/// way a file can leave (decided 2026-09-21): looking through photographs
/// and throwing one out, the next thing wanted is the next photograph -
/// selected, in the preview, under the arrow keys - and not a folder with
/// nothing selected, which is what Explorer leaves. What keeps a second
/// <c>Del</c> from being an accident is the confirmation, not an empty
/// selection.
/// </para>
/// </summary>
public static class CurrentRowFallback {
    /// <param name="before">The rows as they stood, the departed ones among them.</param>
    /// <param name="departed">The paths that left; the last of them in <paramref name="before"/> is where the search starts.</param>
    /// <param name="after">The rows now.</param>
    /// <returns>The row of <paramref name="after"/> to make current; null when none is left or nothing of <paramref name="departed"/> stood in <paramref name="before"/>.</returns>
    public static FileSystemEntry? After(
        IReadOnlyList<FileSystemEntry> before, IReadOnlyCollection<string> departed, IReadOnlyList<FileSystemEntry> after) {
        var gone = new HashSet<string>(departed, StringComparer.OrdinalIgnoreCase);

        int at = -1;
        for (int i = before.Count - 1; i >= 0; i--) {
            if (gone.Contains(before[i].FullPath)) {
                at = i;
                break;
            }
        }
        if (at < 0 || after.Count == 0) {
            return null;
        }

        var standing = new Dictionary<string, FileSystemEntry>(after.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in after) {
            standing[row.FullPath] = row;
        }

        for (int i = at + 1; i < before.Count; i++) {
            if (standing.TryGetValue(before[i].FullPath, out var next)) {
                return next;
            }
        }
        for (int i = at - 1; i >= 0; i--) {
            if (standing.TryGetValue(before[i].FullPath, out var previous)) {
                return previous;
            }
        }

        return null;
    }
}
