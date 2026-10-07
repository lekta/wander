using Wander.Core.FileSystem;

namespace Wander.Core.Companions;

/// <summary>
/// What <see cref="EmbeddedRating"/> found in each photo, so a folder of RAW
/// files is read once and not on every re-listing (decision 2026-10-01:
/// [can cache, invalidation correct]). Only the photo's own rating is kept -
/// sidecars are read live. Keyed by path and checked against size and
/// modified time; the watcher forgets a file it saw change, F5 a whole
/// folder, because exiftool <c>-P</c> rewrites a file and keeps its time.
/// For the life of the process, with a cap against a day of browsing.
/// </summary>
internal sealed class EmbeddedRatingCache {
    private const int Cap = 50_000;

    private readonly Dictionary<string, (long? Size, DateTime Modified, SidecarRating? Rating)> _known =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();


    public bool TryGet(FileSystemEntry photo, out SidecarRating? rating) {
        lock (_gate) {
            if (_known.TryGetValue(photo.FullPath, out var known)
                && known.Size == photo.Size && known.Modified == photo.ModifiedUtc) {
                rating = known.Rating;

                return true;
            }
        }
        rating = null;

        return false;
    }


    public void Put(FileSystemEntry photo, SidecarRating? rating) {
        lock (_gate) {
            if (_known.Count >= Cap) {
                _known.Clear();
            }
            _known[photo.FullPath] = (photo.Size, photo.ModifiedUtc, rating);
        }
    }


    public void Forget(string path) {
        lock (_gate) {
            _known.Remove(path);
        }
    }


    /// <summary>Every photo directly in <paramref name="folder"/>.</summary>
    public void ForgetFolder(string folder) {
        string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        lock (_gate) {
            foreach (string path in _known.Keys.ToArray()) {
                string? parent = Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(parent, trimmed, StringComparison.OrdinalIgnoreCase)) {
                    _known.Remove(path);
                }
            }
        }
    }
}
