using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// Which pictures the pane decodes ahead of time (PLAN AK, step 4): the rows
/// right above and right below the one on show, when they are pictures - an
/// arrow key lands on one of them next. The one in the direction the
/// selection has been moving comes first: a walk down a folder asks for the
/// row below, and the row above is the one it has just left.
/// </summary>
public static class PreviewNeighbors {
    /// <summary>The neighbours to decode, the likelier one first; empty when there are none worth it.</summary>
    /// <param name="listing">The rows on screen, in their order.</param>
    /// <param name="current">The row the pane shows now.</param>
    /// <param name="previousPath">The row it showed before, if any - tells which way the selection moves.</param>
    public static IReadOnlyList<FileSystemEntry> Of(
        IReadOnlyList<FileSystemEntry> listing, FileSystemEntry current, string? previousPath) {
        int at = IndexOf(listing, current.FullPath);
        if (at < 0) {
            return Array.Empty<FileSystemEntry>();
        }

        int before = previousPath is null ? -1 : IndexOf(listing, previousPath);
        bool upwards = before > at;
        var below = at + 1 < listing.Count ? listing[at + 1] : null;
        var above = at > 0 ? listing[at - 1] : null;
        var result = new List<FileSystemEntry>(2);
        foreach (var entry in upwards ? new[] { above, below } : new[] { below, above }) {
            if (entry is { Kind: EntryKind.File } && PreviewRouter.Route(entry.FullPath) == PreviewRoute.Image) {
                result.Add(entry);
            }
        }

        return result;
    }


    private static int IndexOf(IReadOnlyList<FileSystemEntry> listing, string path) {
        for (int i = 0; i < listing.Count; i++) {
            if (string.Equals(listing[i].FullPath, path, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }
}
