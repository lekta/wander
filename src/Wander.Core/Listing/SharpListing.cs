using Wander.Core.FileSystem;

namespace Wander.Core.Listing;

/// <summary>
/// Putting the sharpness pass's answers into the rows of a listing - the
/// same question <see cref="RatedListing"/> answers for ratings: which rows
/// change, and the list left alone when none does.
/// </summary>
public static class SharpListing {
    /// <summary>
    /// The listing with <see cref="FileSystemEntry.Sharpness"/> set to what
    /// <paramref name="sharpness"/> says for each row, null included. The
    /// list it was given when no row changes; otherwise a copy, with the
    /// unchanged rows the very same objects.
    /// </summary>
    public static IReadOnlyList<FileSystemEntry> WithScores(
        IReadOnlyList<FileSystemEntry> entries, Func<FileSystemEntry, double?> sharpness) {
        List<FileSystemEntry>? scored = null;

        for (int i = 0; i < entries.Count; i++) {
            double? value = sharpness(entries[i]);
            if (value == entries[i].Sharpness) {
                scored?.Add(entries[i]);
                continue;
            }

            scored ??= new List<FileSystemEntry>(entries.Take(i));
            scored.Add(entries[i] with { Sharpness = value });
        }

        return scored ?? entries;
    }
}
