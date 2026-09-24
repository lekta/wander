namespace Wander.Core.Imaging;

/// <summary>
/// Bytes that several caches hold against one limit (<see cref="PictureMemory"/>):
/// the frames of every preview count together, whichever pane decoded them.
/// </summary>
public sealed class MemoryShare {
    public MemoryShare(long limit) {
        Limit = limit;
    }


    /// <summary>What the caches may hold together. Lowered, it bites at each cache's next put.</summary>
    public long Limit { get; set; }

    /// <summary>What they hold now.</summary>
    public long Held { get; private set; }


    internal void Add(long bytes) {
        Held += bytes;
    }

    internal void Remove(long bytes) {
        Held = Math.Max(0, Held - bytes);
    }
}


/// <summary>
/// A few decoded pictures, the latest used last, bounded twice: by count,
/// and by the bytes all the caches of one <see cref="MemoryShare"/> hold
/// together. The entries <see cref="Keep"/> names stay whatever else goes -
/// the picture on show and the ones being decoded around it: dropped to
/// make room for each other, they would be decoded twice. Not thread-safe:
/// a pane's cache lives on its UI thread.
/// </summary>
public sealed class SizedCache<TKey, TValue> where TKey : notnull {
    private readonly int _capacity;
    private readonly MemoryShare _share;
    private readonly IEqualityComparer<TKey> _comparer;

    // Most recently used last: the one to drop is the first not kept.
    private readonly List<Entry> _items;
    private HashSet<TKey> _kept;


    public SizedCache(int capacity, MemoryShare share, IEqualityComparer<TKey>? comparer = null) {
        _capacity = Math.Max(1, capacity);
        _share = share;
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _items = new List<Entry>(_capacity);
        _kept = new HashSet<TKey>(_comparer);
    }


    public int Count => _items.Count;

    /// <summary>What this cache holds, of what its share holds.</summary>
    public long Held { get; private set; }


    public bool TryGet(TKey key, out TValue value) {
        int at = IndexOf(key);
        if (at < 0) {
            value = default!;

            return false;
        }

        var item = _items[at];
        _items.RemoveAt(at);
        _items.Add(item);
        value = item.Value;

        return true;
    }

    public bool Contains(TKey key) {
        return IndexOf(key) >= 0;
    }

    /// <summary>
    /// The entries that stay while others go - whether they are in yet or
    /// not. Replaces the ones named before: they are ordinary entries again.
    /// </summary>
    public void Keep(IEnumerable<TKey> keys) {
        _kept = new HashSet<TKey>(keys, _comparer);
    }

    /// <summary>
    /// Adds <paramref name="value"/>, <paramref name="bytes"/> big, as the
    /// latest, and drops the oldest entries that are not kept while there
    /// are too many or the share is over its limit. Neither this one nor a
    /// kept one goes: the picture on show is shown even when it alone is
    /// over the limit.
    /// </summary>
    public void Put(TKey key, TValue value, long bytes) {
        Remove(key);
        _items.Add(new Entry(key, value, bytes));
        Held += bytes;
        _share.Add(bytes);

        for (int i = 0; i < _items.Count && (_items.Count > _capacity || _share.Held > _share.Limit);) {
            var item = _items[i];
            if (_comparer.Equals(item.Key, key) || _kept.Contains(item.Key)) {
                i++;
                continue;
            }

            Drop(i);
        }
    }

    /// <summary>
    /// Whether a picture of <paramref name="bytes"/> fits in what the share
    /// has left, counting what this cache would drop for it - everything it
    /// holds that is not kept.
    /// </summary>
    public bool Fits(long bytes) {
        long droppable = 0;
        foreach (var item in _items) {
            if (!_kept.Contains(item.Key)) {
                droppable += item.Bytes;
            }
        }

        return bytes <= _share.Limit - (_share.Held - droppable);
    }

    /// <summary>Drops everything, and gives its bytes back to the share.</summary>
    public void Clear() {
        _share.Remove(Held);
        Held = 0;
        _items.Clear();
        _kept.Clear();
    }


    private int IndexOf(TKey key) {
        for (int i = 0; i < _items.Count; i++) {
            if (_comparer.Equals(_items[i].Key, key)) {
                return i;
            }
        }

        return -1;
    }

    private void Remove(TKey key) {
        int at = IndexOf(key);
        if (at >= 0) {
            Drop(at);
        }
    }

    private void Drop(int at) {
        long bytes = _items[at].Bytes;
        _items.RemoveAt(at);
        Held -= bytes;
        _share.Remove(bytes);
    }


    private sealed record Entry(TKey Key, TValue Value, long Bytes);
}
