using Wander.Core.FileSystem;

namespace Wander.Core.Pins;

/// <summary>
/// The pins: long-lived marks on files and folders (PLAN, "Pins"), kept
/// apart from the folders themselves - nothing is written next to the file.
/// Held whole in memory, folder by folder; a folder's pin is a record in its
/// parent. Paths compare without case. Pure bookkeeping - no disk, no
/// clock: the caller hands in the time, and persists <see cref="Snapshot"/>
/// through <c>IPinStore</c> when <see cref="Changed"/> fires.
///
/// <para>Not thread-safe; UI thread only.</para>
/// </summary>
public sealed class PinBook {
    // Folder key (PinKeys) -> name -> record.
    private readonly Dictionary<string, Dictionary<string, PinRecord>> _byFolder = new(StringComparer.OrdinalIgnoreCase);

    // The volumes plugged in, for the keys: empty until the caller has read them.
    private IReadOnlyList<PinVolume> _volumes = Array.Empty<PinVolume>();


    public PinBook(IEnumerable<PinFolder>? folders = null) {
        foreach (var folder in folders ?? Array.Empty<PinFolder>()) {
            // A hand-edited file may hold nulls where records belong.
            if (folder is null || string.IsNullOrWhiteSpace(folder.Key)) {
                continue;
            }
            foreach (var pin in folder.Pins ?? Array.Empty<PinRecord>()) {
                if (pin is null || !IsName(pin.Name)) {
                    continue;
                }
                var pins = PinsOf(Normalize(folder.Key), create: true)!;
                // Two records for one name (a hand-edited file): the later
                // pin is kept.
                if (!pins.TryGetValue(pin.Name, out var existing) || existing.PinnedUtc <= pin.PinnedUtc) {
                    pins[pin.Name] = pin;
                }
            }
        }
    }


    /// <summary>Something in the book changed: the caller saves it.</summary>
    public event EventHandler? Changed;

    /// <summary>Every record the book holds, the hidden ones too.</summary>
    public int Count => _byFolder.Values.Sum(p => p.Count);

    /// <summary>How many folders hold a record.</summary>
    public int FolderCount => _byFolder.Count;

    /// <summary>
    /// Goes up with every change. A listing notes it when it starts and
    /// sweeps only if it is the same when the listing lands: names read
    /// before a pin moved, was set or went to the bin say nothing about it.
    /// </summary>
    public int Version { get; private set; }


    /// <summary>
    /// The volumes plugged in now (<see cref="PinKeys.Snapshot"/>): the book
    /// speaks paths and works out the keys against these. Pins on a volume
    /// not among them are out of sight until it is back.
    /// </summary>
    public void SetVolumes(IReadOnlyList<PinVolume> volumes) {
        _volumes = volumes;
    }

    /// <summary>Whether <paramref name="path"/> is pinned and in sight.</summary>
    public bool IsPinned(string path) {
        return Split(path) is var (key, name)
            && PinsOf(key, create: false) is { } pins
            && pins.TryGetValue(name, out var pin)
            && !pin.InRecycleBin;
    }

    /// <summary>Pins <paramref name="path"/> at <paramref name="nowUtc"/>. True when it was not pinned before.</summary>
    public bool Pin(string path, DateTime nowUtc) {
        if (Split(path) is not var (key, name)) {
            return false;
        }
        var pins = PinsOf(key, create: true)!;
        if (pins.TryGetValue(name, out var pin) && !pin.InRecycleBin) {
            return false;
        }

        pins[name] = new PinRecord(name, nowUtc);
        RaiseChanged();

        return true;
    }

    /// <summary>Takes the pin off <paramref name="path"/>. True when there was one.</summary>
    public bool Unpin(string path) {
        if (Split(path) is not var (key, name) || PinsOf(key, create: false) is not { } pins || !pins.Remove(name)) {
            return false;
        }

        DropIfEmpty(key, pins);
        RaiseChanged();

        return true;
    }

    /// <summary>The names pinned in <paramref name="folder"/>, in sight, in name order.</summary>
    public IReadOnlyList<string> NamesIn(string folder) {
        if (PinsOf(KeyOf(Normalize(folder)), create: false) is not { } pins) {
            return Array.Empty<string>();
        }

        return pins.Values
            .Where(p => !p.InRecycleBin)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The folders holding a pin in sight, as paths, in path order.</summary>
    public IReadOnlyList<string> Folders() {
        var found = new List<string>();
        foreach (var (key, pins) in _byFolder) {
            if (PathOf(key) is { } path && pins.Values.Any(p => !p.InRecycleBin)) {
                found.Add(path);
            }
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);

        return found;
    }

    /// <summary>Takes every pin off, the hidden ones too. Returns how many went.</summary>
    public int UnpinAll() {
        int count = Count;
        if (count == 0) {
            return 0;
        }

        _byFolder.Clear();
        RaiseChanged();

        return count;
    }


    // --- Following --------------------------------------------------------

    /// <summary>
    /// Wander moved or renamed <paramref name="from"/> to <paramref name="to"/>:
    /// its pin, and every pin under it when it is a folder, go along. Each
    /// key is worked out again from the new path - across volumes the kind
    /// of key changes. Nothing under <paramref name="from"/>: nothing
    /// happens, so a repeat is harmless. A change of case only is no move.
    /// Returns how many pins followed.
    /// </summary>
    public int Follow(string from, string to) {
        return Follow(new[] { (from, to) });
    }

    /// <summary>
    /// Several moves at once, all read against the book as it stands
    /// before any of them - a batch rename may swap two names.
    /// </summary>
    public int Follow(IReadOnlyList<(string From, string To)> moves) {
        var plan = new List<(string Key, PinRecord Pin, string To)>();
        var taken = new HashSet<(string, string)>();
        foreach (var (from, to) in moves) {
            if (string.Equals(Normalize(from), Normalize(to), StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            foreach (var (key, pin, full) in AtOrUnder(from)) {
                if (taken.Add((key.ToUpperInvariant(), pin.Name.ToUpperInvariant()))
                    && PathRewrite.Under(full, from, to) is { } moved) {
                    plan.Add((key, pin, moved));
                }
            }
        }
        if (plan.Count == 0) {
            return 0;
        }

        foreach (var (key, pin, _) in plan) {
            _byFolder[key].Remove(pin.Name);
        }
        foreach (var (_, pin, moved) in plan) {
            if (Split(moved) is var (key, name)) {
                PinsOf(key, create: true)![name] = pin with { Name = name };
            }
        }
        foreach (var key in plan.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (_byFolder.TryGetValue(key, out var pins)) {
                DropIfEmpty(key, pins);
            }
        }
        RaiseChanged();

        return plan.Count;
    }

    /// <summary>
    /// <paramref name="path"/> went to the Recycle Bin: its pin, and every
    /// pin under it, is kept out of sight and out of the sweep until the
    /// listing finds the item back (<see cref="Sweep"/>). Returns how many.
    /// </summary>
    public int MarkRecycled(string path) {
        int count = 0;
        foreach (var (key, pin, _) in AtOrUnder(path)) {
            if (!pin.InRecycleBin) {
                _byFolder[key][pin.Name] = pin with { InRecycleBin = true };
                count++;
            }
        }
        if (count > 0) {
            RaiseChanged();
        }

        return count;
    }

    /// <summary>
    /// <paramref name="path"/> was deleted for good: its pin, and every pin
    /// under it, goes. Returns how many.
    /// </summary>
    public int Forget(string path) {
        var gone = AtOrUnder(path);
        foreach (var (key, pin, _) in gone) {
            var pins = _byFolder[key];
            pins.Remove(pin.Name);
            DropIfEmpty(key, pins);
        }
        if (gone.Count > 0) {
            RaiseChanged();
        }

        return gone.Count;
    }

    /// <summary>
    /// <paramref name="folder"/> was listed to the end, and
    /// <paramref name="names"/> is everything in it, hidden files too. A pin
    /// in the bin whose name is back is in sight again; with
    /// <paramref name="removeGone"/> a pin whose name is missing is taken
    /// off - one in the bin stays. Only a complete listing of a folder on a
    /// disk is for this: a partial one would take off what it did not reach.
    /// <paramref name="listedAt"/> is the <see cref="Version"/> the listing
    /// started at: a book changed since then is not swept at all.
    /// Returns the names taken off.
    /// </summary>
    public IReadOnlyList<string> Sweep(string folder, IReadOnlyCollection<string> names, bool removeGone, int listedAt) {
        string key = KeyOf(Normalize(folder));
        if (listedAt != Version || PinsOf(key, create: false) is not { } pins) {
            return Array.Empty<string>();
        }

        var present = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        bool changed = false;
        foreach (var pin in pins.Values.ToList()) {
            if (present.Contains(pin.Name)) {
                if (pin.InRecycleBin) {
                    pins[pin.Name] = pin with { InRecycleBin = false };
                    changed = true;
                }
            } else if (removeGone && !pin.InRecycleBin) {
                pins.Remove(pin.Name);
                removed.Add(pin.Name);
                changed = true;
            }
        }
        DropIfEmpty(key, pins);
        if (changed) {
            RaiseChanged();
        }

        return removed;
    }

    /// <summary>Everything the book holds, for <c>IPinStore.Save</c>: folders by key, pins by name.</summary>
    public IReadOnlyList<PinFolder> Snapshot() {
        return _byFolder
            .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .Select(f => new PinFolder(
                f.Key,
                f.Value.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }


    // --- Keys -------------------------------------------------------------

    /// <summary>The storage key of a folder path.</summary>
    private string KeyOf(string folder) {
        return PinKeys.ToKey(folder, _volumes);
    }

    /// <summary>The path a storage key stands for, or null when its volume is not plugged in.</summary>
    private string? PathOf(string key) {
        return PinKeys.ToPath(key, _volumes);
    }

    /// <summary>The parent's key and the name; null for a root or a path with no name.</summary>
    private (string Key, string Name)? Split(string path) {
        string full = Normalize(path);
        string? parent = Path.GetDirectoryName(full);
        string name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(parent) || !IsName(name)) {
            return null;
        }

        return (KeyOf(parent), name);
    }


    /// <summary>
    /// The pin on <paramref name="path"/> and every pin under it, with their
    /// full paths now - pins on a volume not plugged in are not among them.
    /// </summary>
    private List<(string Key, PinRecord Pin, string Full)> AtOrUnder(string path) {
        var found = new List<(string, PinRecord, string)>();
        string full = Normalize(path);
        var own = Split(full);
        foreach (var (key, pins) in _byFolder) {
            if (own is var (parentKey, name) && string.Equals(key, parentKey, StringComparison.OrdinalIgnoreCase)) {
                if (pins.TryGetValue(name, out var pin)) {
                    found.Add((key, pin, full));
                }
                continue;
            }
            if (PathOf(key) is { } folder && PathRewrite.Under(folder, full, full) is not null) {
                found.AddRange(pins.Values.Select(p => (key, p, Path.Combine(folder, p.Name))));
            }
        }

        return found;
    }


    // --- Helpers ----------------------------------------------------------

    private Dictionary<string, PinRecord>? PinsOf(string key, bool create) {
        if (_byFolder.TryGetValue(key, out var pins)) {
            return pins;
        }
        if (!create) {
            return null;
        }

        pins = new Dictionary<string, PinRecord>(StringComparer.OrdinalIgnoreCase);
        _byFolder[key] = pins;

        return pins;
    }

    private void DropIfEmpty(string key, Dictionary<string, PinRecord> pins) {
        if (pins.Count == 0) {
            _byFolder.Remove(key);
        }
    }

    private void RaiseChanged() {
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsName(string? name) {
        return !string.IsNullOrEmpty(name) && name.IndexOfAny(new[] { '\\', '/' }) < 0;
    }

    private static string Normalize(string path) {
        return Path.TrimEndingDirectorySeparator(path);
    }
}
