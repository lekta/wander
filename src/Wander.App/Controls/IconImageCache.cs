using System.Windows.Media.Imaging;
using Wander.App.Converters;
using Wander.Core.Icons;
using Wander.Core.Imaging;

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
/// Bounded oldest-first, and only the thumbnail sizes (Medium and Large -
/// one image per file) are counted: Small and Normal are keyed by
/// extension, so there are as many of them as there are file types on the
/// machine and they cost a few kilobytes each. Bounded twice: by count, and
/// by the bytes they hold against the thumbnails' share of the picture
/// budget (<see cref="PictureMemory"/>, 2026-09-24) - a large one is a
/// quarter of a megabyte at 256 px, and four times that at 512 px, the
/// side a 200 % display gets.
/// </para>
/// </summary>
internal static class IconImageCache {
    /// <summary>
    /// How many per-file thumbnails to keep decoded. Two screenfuls of
    /// tiles at the largest sensible size, so scrolling back over what was
    /// just seen is free, at a few tens of megabytes.
    /// </summary>
    private const int ThumbnailBudget = 256;

    /// <summary>
    /// The sizes one path can be cached at, held rather than asked for:
    /// <see cref="Forget"/> runs once per file in a watcher burst, and
    /// <c>Enum.GetValues</c> allocates a fresh array on every call.
    /// </summary>
    private static readonly IconSize[] _sizes = Enum.GetValues<IconSize>();

    // Keyed by a tuple rather than a formatted string: this is asked once
    // per tile appearing, and a string built per lookup is garbage produced
    // by the very code that exists to stop the hot path costing anything.
    //
    // The bytes an image was decoded from travel with it, and a hit counts
    // only while the provider still answers with that same array. The key
    // is a path, and a path outlives what stands on it: a folder renamed
    // away leaves its path to be asked about once more as a file that is
    // not there, and the next "New folder" at that path would otherwise be
    // drawn with the picture decoded for the absence (2026-09-21).
    private static readonly Dictionary<(IconSize Size, string Path), (byte[] From, BitmapImage Image)> _images = new();
    private static readonly Queue<(IconSize Size, string Path)> _thumbOrder = new();
    private static readonly Lock _lock = new();

    // What the thumbnails hold decoded, and may - SetLimit.
    private static long _thumbBytes;
    private static long _thumbLimit = PictureMemory.Thumbnails(
        PictureMemory.Budget(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes));


    /// <summary>
    /// The already-decoded image, if these bytes have been drawn for this
    /// path and size before. The distinction matters to <see cref="AsyncIcon"/>: a hit
    /// here can go on screen synchronously (scrolling back over seen tiles
    /// must not blink), while a miss means a real decode — work that has no
    /// business on the UI thread, where a folder revisit used to run
    /// hundreds of them in one second.
    /// </summary>
    public static bool TryGetDecoded(string path, IconSize size, byte[] bytes, out BitmapImage image) {
        lock (_lock) {
            if (_images.TryGetValue((size, path), out var hit) && ReferenceEquals(hit.From, bytes)) {
                image = hit.Image;

                return true;
            }
        }

        image = null!;

        return false;
    }


    /// <summary>
    /// The decoded form of <paramref name="bytes"/> for this path and size,
    /// decoding it only if this is the first time it is asked for.
    /// </summary>
    public static BitmapImage Get(string path, IconSize size, byte[] bytes) {
        if (TryGetDecoded(path, size, bytes, out var known)) {
            return known;
        }

        // Decoded outside the lock: two tiles racing on the same file at
        // worst decode it twice, which is cheaper than making every other
        // tile wait behind one decode.
        var image = IconConverter.ToImage(bytes);
        var key = (size, path);

        lock (_lock) {
            bool isNew = !_images.TryGetValue(key, out var old);
            _images[key] = (bytes, image);
            if (!IsThumbnail(size)) {
                return image;
            }

            _thumbBytes += BytesOf(image) - (isNew ? 0 : BytesOf(old.Image));
            if (isNew) {
                _thumbOrder.Enqueue(key);
            }
            Trim();
        }

        return image;
    }


    /// <summary>
    /// The thumbnails' share of the picture budget changed - the settings'
    /// ceiling, or the machine's sixteenth. A lower one bites now.
    /// </summary>
    public static void SetLimit(long bytes) {
        lock (_lock) {
            _thumbLimit = bytes;
            Trim();
        }
    }


    /// <summary>
    /// Drops the decoded images of one file. The tier below
    /// (<see cref="Wander.Core.Icons.IIconProvider.Forget"/>) has just been
    /// told the same thing; leaving the decoded copy here would keep the
    /// old picture on screen anyway, since a hit here never asks the
    /// provider at all.
    /// </summary>
    public static void Forget(string path) {
        lock (_lock) {
            foreach (var size in _sizes) {
                if (_images.Remove((size, path), out var gone) && IsThumbnail(size)) {
                    _thumbBytes -= BytesOf(gone.Image);
                }
            }
        }
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
            _thumbBytes = 0;
        }
    }


    /// <summary>Caller holds the lock. Drops the oldest thumbnails while there are too many or they hold too much.</summary>
    private static void Trim() {
        while (_thumbOrder.Count > 0 && (_thumbOrder.Count > ThumbnailBudget || _thumbBytes > _thumbLimit)) {
            // A key forgotten meanwhile is in the queue with nothing behind it.
            if (_images.Remove(_thumbOrder.Dequeue(), out var gone)) {
                _thumbBytes -= BytesOf(gone.Image);
            }
        }
    }

    private static bool IsThumbnail(IconSize size) {
        return size is IconSize.Large or IconSize.Medium;
    }

    private static long BytesOf(BitmapSource image) {
        return PictureMemory.BytesOf(image.PixelWidth, image.PixelHeight, image.Format.BitsPerPixel);
    }
}
