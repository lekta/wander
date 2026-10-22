using Wander.Core.FileSystem;

namespace Wander.Core.Search;

/// <summary>
/// What each photo said about how it was shot (<see cref="ShotFacts"/>),
/// so a folder is read once for the box's parameters and not on every
/// keystroke. Keyed by path and checked against size and modified time,
/// as the photos' own ratings are (<c>EmbeddedRatingCache</c>); a photo
/// that says nothing is remembered as saying nothing. Safe across threads:
/// the filter reads it on the pool. For the life of the process, with a cap.
/// </summary>
public sealed class ShotFactsCache {
    private const int Cap = 50_000;

    private readonly Dictionary<string, (long? Size, DateTime Modified, ShotFacts? Facts)> _known =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Func<string, ShotFacts?> _read;


    /// <param name="read">Reads a photo's facts off the disk; null when it says nothing or cannot be read.</param>
    public ShotFactsCache(Func<string, ShotFacts?> read) {
        _read = read;
    }


    /// <summary>The facts of <paramref name="entry"/>, read the first time they are asked for.</summary>
    public ShotFacts? Of(FileSystemEntry entry) {
        if (TryKnown(entry, out var known)) {
            return known;
        }

        var facts = _read(entry.FullPath);
        if (facts is { IsEmpty: true }) {
            facts = null;
        }
        lock (_gate) {
            if (_known.Count >= Cap) {
                _known.Clear();
            }
            _known[entry.FullPath] = (entry.Size, entry.ModifiedUtc, facts);
        }

        return facts;
    }

    /// <summary>
    /// The facts of <paramref name="entry"/> without the disk: false for a
    /// photo not read yet, or changed since - what a caller on the UI thread
    /// asks.
    /// </summary>
    public bool TryKnown(FileSystemEntry entry, out ShotFacts? facts) {
        lock (_gate) {
            if (_known.TryGetValue(entry.FullPath, out var known)
                && known.Size == entry.Size && known.Modified == entry.ModifiedUtc) {
                facts = known.Facts;

                return true;
            }
        }
        facts = null;

        return false;
    }
}
