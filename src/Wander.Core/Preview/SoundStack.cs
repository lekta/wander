using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// When the preview pane shows a stack of shapes instead of one track
/// (decision 2026-10-27): two or more files selected and every one of them
/// a sound - a folder of sounds is gone through by comparing them. Two
/// sounds stack rather than split (<see cref="PreviewPair"/>): two cards of
/// a track side by side say less than two shapes one over the other.
/// </summary>
public static class SoundStack {
    /// <summary>The sounds in listing order, or null when the selection is not such a stack.</summary>
    /// <param name="selection">What is selected, in whatever order the list reports it.</param>
    /// <param name="listing">The rows on screen; decides the order.</param>
    public static IReadOnlyList<FileSystemEntry>? Of(
        IReadOnlyList<FileSystemEntry> selection, IReadOnlyList<FileSystemEntry> listing) {
        if (!Applies(selection)) {
            return null;
        }

        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < listing.Count; i++) {
            order.TryAdd(listing[i].FullPath, i);
        }

        return selection
            .OrderBy(e => order.TryGetValue(e.FullPath, out int i) ? i : int.MaxValue)
            .ToList();
    }

    /// <summary>
    /// How much of the width each shape takes: its length against the
    /// longest of the stack (decision 2026-10-27), so a stack compares
    /// lengths at a glance. A length not known takes the whole width, as
    /// does every shape while none is known.
    /// </summary>
    public static double[] Shares(IReadOnlyList<TimeSpan?> lengths) {
        double longest = lengths.Count == 0 ? 0 : lengths.Max(l => l?.TotalSeconds ?? 0);

        return lengths
            .Select(l => l is { } known && longest > 0 ? Math.Clamp(known.TotalSeconds / longest, 0, 1) : 1)
            .ToArray();
    }

    /// <summary>Whether <paramref name="selection"/> is two or more sound files and nothing else.</summary>
    public static bool Applies(IReadOnlyList<FileSystemEntry> selection) {
        return selection.Count >= 2
            && selection.All(e => e.Kind == EntryKind.File && AudioTags.IsAudio(e.FullPath));
    }
}
