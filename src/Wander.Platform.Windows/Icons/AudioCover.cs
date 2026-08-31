using Wander.Core.Preview;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// The picture that belongs to a music file, for drawing on its tile.
///
/// <para>
/// Explorer does this and we did not, which is the whole reason it exists.
/// The bytes come from <see cref="AudioTags"/> — the tag inside the file
/// first, the picture lying beside it second — so a folder of tracks looks
/// like the record it is rather than a column of identical note glyphs.
/// </para>
///
/// <para>
/// The second source is what needs the care here. Finding a cover beside a
/// track means listing the folder, and doing that once per file turns a
/// hundred-track album into a hundred listings. So the answer is
/// remembered per folder, keyed by the folder's own last-write time: adding
/// or removing a file changes that, which drops the entry and asks again.
/// A memo that never expired would be faster and would leave a freshly
/// added <c>Cover.jpg</c> invisible until the next run.
/// </para>
/// </summary>
internal static class AudioCover {
    /// <summary>Folders to remember. A few screenfuls of navigation, not a session's worth.</summary>
    private const int MaxRememberedFolders = 64;

    private static readonly Dictionary<string, (DateTime Stamp, string? Cover)> _beside =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Queue<string> _order = new();
    private static readonly object _lock = new();


    public static bool Supports(string path) {
        return AudioTags.IsAudio(path);
    }


    /// <summary>
    /// Cover art for <paramref name="path"/> as image bytes, or null when
    /// the track has none anywhere. The caller frames it like a book cover.
    /// </summary>
    public static byte[]? Read(string path) {
        if (!Supports(path)) {
            return null;
        }

        try {
            if (AudioTags.Read(path)?.Cover is { Length: > 0 } embedded) {
                return embedded;
            }

            string? beside = BesideCached(path);

            return beside is null ? null : File.ReadAllBytes(beside);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException) {
            return null;
        }
    }


    private static string? BesideCached(string path) {
        string? folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder)) {
            return null;
        }

        DateTime stamp;
        try {
            stamp = Directory.GetLastWriteTimeUtc(folder);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }

        lock (_lock) {
            if (_beside.TryGetValue(folder, out var hit) && hit.Stamp == stamp) {
                return hit.Cover;
            }
        }

        // Outside the lock: a listing on a sleeping disk must not hold up
        // every other tile in the folder.
        string? cover = AudioTags.CoverBeside(path);

        lock (_lock) {
            if (!_beside.ContainsKey(folder)) {
                _order.Enqueue(folder);
                while (_order.Count > MaxRememberedFolders) {
                    _beside.Remove(_order.Dequeue());
                }
            }
            _beside[folder] = (stamp, cover);
        }

        return cover;
    }


    /// <summary>Paired with the settings dialog's "clear thumbnail cache".</summary>
    public static void Forget() {
        lock (_lock) {
            _beside.Clear();
            _order.Clear();
        }
    }
}
