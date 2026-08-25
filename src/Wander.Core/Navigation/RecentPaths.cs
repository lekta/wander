namespace Wander.Core.Navigation;

/// <summary>
/// MRU list behind the address-bar dropdown. Distinct from
/// <see cref="NavigationService"/>: that one is a linear timeline with a
/// cursor (Back / Forward), this one is "the last N *distinct* places I
/// was", most recent first, no duplicates.
///
/// <para>
/// Paths are compared case-insensitively and ignoring a trailing
/// separator — for this list <c>D:\foo</c> and <c>D:\foo\</c> are the
/// same place.
/// </para>
/// </summary>
public sealed class RecentPaths {
    /// <summary>How many entries are remembered by default.</summary>
    public const int DefaultCapacity = 20;

    private readonly List<string> _items = new();
    private readonly int _capacity;


    public RecentPaths(int capacity = DefaultCapacity) {
        if (capacity < 1) {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive");
        }
        _capacity = capacity;
    }


    /// <summary>Most recent first.</summary>
    public IReadOnlyList<string> Items => _items;


    /// <summary>
    /// Records a visit. An already-known path moves to the front instead
    /// of being duplicated; the oldest entry falls off the tail once the
    /// list outgrows its capacity.
    /// </summary>
    public void Add(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }

        int existing = IndexOf(path);
        if (existing == 0) {
            return;
        }
        if (existing > 0) {
            _items.RemoveAt(existing);
        }

        _items.Insert(0, path);
        if (_items.Count > _capacity) {
            _items.RemoveRange(_capacity, _items.Count - _capacity);
        }
    }

    /// <summary>
    /// Replaces the list wholesale — restoring from <c>state.json</c>.
    /// Input order is kept; duplicates and anything past the capacity are
    /// dropped (the file could have been hand-edited).
    /// </summary>
    public void Load(IEnumerable<string> paths) {
        _items.Clear();
        foreach (string path in paths) {
            if (string.IsNullOrWhiteSpace(path) || IndexOf(path) >= 0) {
                continue;
            }

            _items.Add(path);
            if (_items.Count == _capacity) {
                break;
            }
        }
    }


    private int IndexOf(string path) {
        string needle = Normalize(path);
        for (int i = 0; i < _items.Count; i++) {
            if (string.Equals(Normalize(_items[i]), needle, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }

    private static string Normalize(string path) {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }
}
