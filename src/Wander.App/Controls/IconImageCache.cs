using System.Windows.Media.Imaging;
using Wander.App.Converters;
using Wander.Core.Icons;

namespace Wander.App.Controls;

/// <summary>
/// Keeps decoded thumbnails, so a tile that has been on screen before costs
/// nothing to show again.
///
/// <para>
/// The icon provider caches <c>byte[]</c> — the file as the shell handed it
/// over — and turning those bytes into something WPF can draw is a JPEG
/// decode on the UI thread. Cheap once (a third of a millisecond), except
/// that scrolling a folder of photos does it three hundred times a second:
/// in the trace that prompted this, 338 decodes and 141 ms inside one
/// second of scrolling, all of it on the thread that should have been
/// drawing. Decoded images are immutable and frozen, so they can simply be
/// kept.
/// </para>
///
/// <para>
/// Bounded by count, oldest-first, and only the thumbnail sizes (Medium and
/// Large — one image per file) are counted: Small and Normal are keyed by
/// extension, so there are as many of them as there are file types on the
/// machine and they cost a few kilobytes each. A large one is around a
/// quarter of a megabyte decoded, which is what
/// <see cref="ThumbnailBudget"/> is sized against; a medium one is a
/// sixteenth of that, so counting the two together is deliberately generous
/// rather than exact.
/// </para>
/// </summary>
internal static class IconImageCache {
    /// <summary>
    /// How many per-file thumbnails to keep decoded. Two screenfuls of
    /// tiles at the largest sensible size, so scrolling back over what was
    /// just seen is free, at a few tens of megabytes.
    /// </summary>
    private const int ThumbnailBudget = 256;

    // Keyed by a tuple rather than a formatted string: this is asked once
    // per tile appearing, and a string built per lookup is garbage produced
    // by the very code that exists to stop the hot path costing anything.
    private static readonly Dictionary<(IconSize Size, string Path), BitmapImage> _images = new();
    private static readonly Queue<(IconSize Size, string Path)> _thumbOrder = new();
    private static readonly object _lock = new();


    /// <summary>
    /// The already-decoded image, if this path and size have been drawn
    /// before. The distinction matters to <see cref="AsyncIcon"/>: a hit
    /// here can go on screen synchronously (scrolling back over seen tiles
    /// must not blink), while a miss means a real decode — work that has no
    /// business on the UI thread, where a folder revisit used to run
    /// hundreds of them in one second.
    /// </summary>
    public static bool TryGetDecoded(string path, IconSize size, out BitmapImage image) {
        lock (_lock) {
            return _images.TryGetValue((size, path), out image!);
        }
    }


    /// <summary>
    /// The decoded form of <paramref name="bytes"/> for this path and size,
    /// decoding it only if this is the first time it is asked for.
    /// </summary>
    public static BitmapImage Get(string path, IconSize size, byte[] bytes) {
        var key = (size, path);

        lock (_lock) {
            if (_images.TryGetValue(key, out var hit)) {
                return hit;
            }
        }

        // Decoded outside the lock: two tiles racing on the same file at
        // worst decode it twice, which is cheaper than making every other
        // tile wait behind one decode.
        var image = IconConverter.ToImage(bytes);

        lock (_lock) {
            if (!_images.TryAdd(key, image)) {
                return _images[key];
            }
            if (size is IconSize.Large or IconSize.Medium) {
                _thumbOrder.Enqueue(key);
                while (_thumbOrder.Count > ThumbnailBudget) {
                    _images.Remove(_thumbOrder.Dequeue());
                }
            }
        }

        return image;
    }


    /// <summary>
    /// Drops everything. Paired with the settings dialog's "clear thumbnail
    /// cache" — leaving decoded copies of what the user just cleared would
    /// make the button look broken.
    /// </summary>
    public static void Clear() {
        lock (_lock) {
            _images.Clear();
            _thumbOrder.Clear();
        }
    }
}
