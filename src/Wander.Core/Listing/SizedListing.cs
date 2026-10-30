using Wander.Core.FileSystem;

namespace Wander.Core.Listing;

/// <summary>
/// Putting what <see cref="FolderSizes"/> found into the rows of a listing -
/// the same question <see cref="SharpListing"/> answers for sharpness. Only
/// for ordering by size: the rows on screen otherwise stay as they were
/// listed, and the table's cell asks for itself.
/// </summary>
public static class SizedListing {
    /// <summary>
    /// The listing with <see cref="FileSystemEntry.ContentSize"/> set on its
    /// folders to what <paramref name="size"/> says. The list it was given
    /// when no row changes; otherwise a copy, with the unchanged rows the
    /// very same objects.
    /// </summary>
    public static IReadOnlyList<FileSystemEntry> WithSizes(
        IReadOnlyList<FileSystemEntry> entries, Func<string, long?> size) {
        List<FileSystemEntry>? sized = null;

        for (int i = 0; i < entries.Count; i++) {
            var entry = entries[i];
            long? value = entry.Kind == EntryKind.Directory && entry.Size is null ? size(entry.FullPath) : null;
            if (value == entry.ContentSize) {
                sized?.Add(entry);
                continue;
            }

            sized ??= new List<FileSystemEntry>(entries.Take(i));
            sized.Add(entry with { ContentSize = value });
        }

        return sized ?? entries;
    }
}
