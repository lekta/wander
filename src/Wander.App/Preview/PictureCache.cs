using Wander.Core.FileSystem;

namespace Wander.App.Preview;

/// <summary>
/// The last few pictures decoded for the pane: the one on show and its
/// neighbours, so an arrow key lands on a picture already decoded. Keyed by
/// the file as it stands (path, time, size) and the box it was fitted to.
/// UI thread only.
/// </summary>
internal sealed class PictureCache {
    /// <summary>The picture on show and one neighbour each way.</summary>
    private const int Capacity = 3;

    private readonly List<(string Key, DecodedPicture Picture)> _items = new(Capacity);


    public static string KeyOf(FileSystemEntry entry, double boxWidth, double boxHeight) {
        return $"{entry.FullPath}|{entry.ModifiedUtc.Ticks}|{entry.Size}|{boxWidth:F0}x{boxHeight:F0}";
    }


    public bool TryGet(string key, out DecodedPicture picture) {
        int at = _items.FindIndex(i => i.Key == key);
        if (at < 0) {
            picture = null!;

            return false;
        }

        // Most recent last: the one to drop is the one at the front.
        var item = _items[at];
        _items.RemoveAt(at);
        _items.Add(item);
        picture = item.Picture;

        return true;
    }

    public bool Contains(string key) {
        return _items.Exists(i => i.Key == key);
    }

    public void Put(string key, DecodedPicture picture) {
        _items.RemoveAll(i => i.Key == key);
        if (_items.Count == Capacity) {
            _items.RemoveAt(0);
        }
        _items.Add((key, picture));
    }

    public void Clear() {
        _items.Clear();
    }
}
