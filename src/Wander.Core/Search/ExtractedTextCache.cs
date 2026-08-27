namespace Wander.Core.Search;

/// <summary>
/// Text pulled out of expensive formats, kept for the next search.
///
/// <para>
/// Deliberately in memory and deliberately small. Measured on a
/// repository-sized tree, scanning 1884 text files (8.3 MB) takes about
/// 180 ms on a single thread — a disk index would spend gigabytes and a
/// background crawler to save a fifth of a second. What is worth keeping
/// is the handful of documents that cost tens of milliseconds each to
/// unzip or to hand across a COM boundary, and only for as long as the
/// window is open.
/// </para>
///
/// <para>
/// The key carries size and modification time, so an edited file misses
/// rather than answers with what it used to say. Eviction is
/// least-recently-used against a character budget: characters rather than
/// entries because the whole point is bounding memory, and one book is
/// worth a thousand memos.
/// </para>
/// </summary>
public sealed class ExtractedTextCache {
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);

    /// <summary>Most recently used at the front, eviction victim at the back.</summary>
    private readonly LinkedList<Entry> _order = new();

    private readonly long _budgetChars;
    private long _chars;


    /// <param name="budgetBytes">
    /// Roughly how much memory the cache may hold. Halved into characters:
    /// .NET strings are UTF-16, so a megabyte of budget is half a million
    /// characters of document.
    /// </param>
    public ExtractedTextCache(long budgetBytes = 32L * 1024 * 1024) {
        _budgetChars = Math.Max(1, budgetBytes / 2);
    }


    /// <summary>Characters currently held. Tests assert on it; the settings page may one day show it.</summary>
    public long CharCount {
        get {
            lock (_gate) {
                return _chars;
            }
        }
    }


    /// <summary>How many entries are held.</summary>
    public int Count {
        get {
            lock (_gate) {
                return _index.Count;
            }
        }
    }


    /// <summary>
    /// The remembered text for a file, or null when it was never cached,
    /// was evicted, or has changed on disk since.
    /// </summary>
    public string? Get(string path, long size, DateTime modifiedUtc) {
        string key = Key(path, size, modifiedUtc);

        lock (_gate) {
            if (!_index.TryGetValue(key, out var node)) {
                return null;
            }
            _order.Remove(node);
            _order.AddFirst(node);

            return node.Value.Text;
        }
    }


    /// <summary>
    /// Remember a file's text, evicting the least recently used entries
    /// until the budget is met again. Text larger than the whole budget is
    /// not stored at all — keeping it would evict everything else to hold
    /// one item that the next insert throws away.
    /// </summary>
    public void Put(string path, long size, DateTime modifiedUtc, string text) {
        if (text.Length > _budgetChars) {
            return;
        }

        string key = Key(path, size, modifiedUtc);

        lock (_gate) {
            if (_index.TryGetValue(key, out var existing)) {
                _chars -= existing.Value.Text.Length;
                _order.Remove(existing);
                _index.Remove(key);
            }

            var node = _order.AddFirst(new Entry(key, text));
            _index[key] = node;
            _chars += text.Length;

            while (_chars > _budgetChars && _order.Last is { } victim) {
                _order.RemoveLast();
                _index.Remove(victim.Value.Key);
                _chars -= victim.Value.Text.Length;
            }
        }
    }


    /// <summary>Drop everything. Used when the user turns content search off.</summary>
    public void Clear() {
        lock (_gate) {
            _index.Clear();
            _order.Clear();
            _chars = 0;
        }
    }


    private static string Key(string path, long size, DateTime modifiedUtc) {
        return $"{path.ToLowerInvariant()}|{size}|{modifiedUtc.Ticks}";
    }


    private readonly record struct Entry(string Key, string Text);
}
