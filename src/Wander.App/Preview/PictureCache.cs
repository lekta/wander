using Wander.Core.FileSystem;
using Wander.Core.Imaging;

namespace Wander.App.Preview;

/// <summary>
/// The last few pictures decoded for the pane: the one on show and its
/// neighbours, so an arrow key lands on a picture already decoded. Keyed by
/// the file as it stands (path, time, size) and the box it was fitted to.
/// Bounded by count and by bytes: the frames of every pane - the split's
/// halves, full screen - count against one share of the picture budget
/// (<see cref="PictureMemory"/>, 2026-09-24). UI thread only.
/// </summary>
internal sealed class PictureCache {
    /// <summary>The picture on show and one neighbour each way.</summary>
    private const int Capacity = 3;

    /// <summary>What the frames of every pane together may hold - set from the settings (<see cref="SetLimit"/>).</summary>
    private static readonly MemoryShare _frames = new(PictureMemory.Frames(
        PictureMemory.Budget(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)));

    private readonly SizedCache<string, DecodedPicture> _items = new(Capacity, _frames);


    public static string KeyOf(FileSystemEntry entry, double boxWidth, double boxHeight) {
        return $"{entry.FullPath}|{entry.ModifiedUtc.Ticks}|{entry.Size}|{boxWidth:F0}x{boxHeight:F0}";
    }

    /// <summary>The frames' share of the budget changed; it bites at each pane's next picture.</summary>
    public static void SetLimit(long bytes) {
        _frames.Limit = bytes;
    }

    /// <summary>What a decoded picture holds: its fitted pixels, and a RAW's embedded JPEG kept for the zoom.</summary>
    public static long BytesOf(DecodedPicture picture) {
        var fit = picture.Fit;

        return PictureMemory.BytesOf(fit.PixelWidth, fit.PixelHeight, fit.Format.BitsPerPixel)
            + (picture.Embedded?.Length ?? 0);
    }


    public bool TryGet(string key, out DecodedPicture picture) {
        return _items.TryGet(key, out picture);
    }

    public bool Contains(string key) {
        return _items.Contains(key);
    }

    /// <summary>The pictures that stay while others go: the one on show, and those decoded around it now.</summary>
    public void Keep(IEnumerable<string> keys) {
        _items.Keep(keys);
    }

    public void Put(string key, DecodedPicture picture) {
        _items.Put(key, picture, BytesOf(picture));
    }

    /// <summary>Whether a picture of <paramref name="bytes"/> may be decoded ahead - see <see cref="SizedCache{TKey,TValue}.Fits"/>.</summary>
    public bool Fits(long bytes) {
        return _items.Fits(bytes);
    }

    public void Clear() {
        _items.Clear();
    }
}
