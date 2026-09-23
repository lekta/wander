using Wander.Core.FileSystem;

namespace Wander.Core.Folders;

/// <summary>
/// The records Wander keeps about folders (<see cref="FolderRecord"/>),
/// keyed by path: find, pin a view, follow a rename, age out. Pure
/// bookkeeping - no disk, no clock; the caller hands in today's date and
/// the creation time it read on the pool, and persists
/// <see cref="Records"/> through <c>IFolderSettingsStore</c> when a call
/// says something changed.
///
/// <para>
/// Only folders with something to say are kept: a record whose last field
/// was cleared is dropped, so the book holds pins rather than a diary of
/// every folder ever opened. The cap is on pins, and the oldest visit goes
/// first - a folder pinned once and never opened again is the one whose
/// pin is worth least.
/// </para>
///
/// <para>
/// Not thread-safe; UI thread only. <see cref="Records"/> is a snapshot the
/// pool may read (<see cref="AdoptCandidates"/>).
/// </para>
/// </summary>
public sealed class FolderSettingsBook {
    /// <summary>
    /// How many folders may carry a record. Thousands rather than the 128 of
    /// the state-file era: the file is loaded once and a record is a line.
    /// </summary>
    public const int DefaultCapacity = 3000;

    private readonly Dictionary<string, FolderRecord> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _capacity;


    public FolderSettingsBook(IEnumerable<FolderRecord>? records = null, int capacity = DefaultCapacity) {
        if (capacity < 1) {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive");
        }
        _capacity = capacity;

        foreach (var record in records ?? Array.Empty<FolderRecord>()) {
            if (string.IsNullOrWhiteSpace(record.Path) || record.IsEmpty) {
                continue;
            }
            string key = Normalize(record.Path);
            // Two records for one folder (a hand-edited file): the later
            // visit is the one that knows more.
            if (!_byKey.TryGetValue(key, out var existing) || existing.LastVisit <= record.LastVisit) {
                _byKey[key] = record with { Path = key };
            }
        }
        Trim();
    }


    public int Count => _byKey.Count;

    /// <summary>
    /// Everything the book holds, latest visit first - the order the file
    /// is written in, so a diff of <c>folders.json</c> reads as a history.
    /// </summary>
    public IReadOnlyList<FolderRecord> Records =>
        _byKey.Values
            .OrderByDescending(r => r.LastVisit)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();


    /// <summary>The record for <paramref name="path"/>, or null when the folder has none.</summary>
    public FolderRecord? Find(string path) {
        return _byKey.TryGetValue(Normalize(path), out var record) ? record : null;
    }

    /// <summary>
    /// The folder was opened today. Only a folder that already has a record
    /// is touched - it moves to the front of the ageing order and learns its
    /// creation time if it did not know it. Returns true when the record
    /// changed and the book wants saving.
    /// </summary>
    public bool Touch(string path, DateTime? createdUtc, DateOnly today) {
        string key = Normalize(path);
        if (!_byKey.TryGetValue(key, out var record)) {
            return false;
        }

        var touched = record with {
            LastVisit = today > record.LastVisit ? today : record.LastVisit,
            CreatedUtc = record.CreatedUtc ?? createdUtc,
        };
        if (touched == record) {
            return false;
        }

        _byKey[key] = touched;

        return true;
    }

    /// <summary>
    /// Pins <paramref name="view"/> to the folder, or with null lets the
    /// folder be chosen for again. A record left with nothing in it is
    /// dropped; a new pin past the cap ages the oldest pin out. Returns true
    /// when anything changed.
    /// </summary>
    public bool SetView(string path, ViewMode? view, DateTime? createdUtc, DateOnly today) {
        string key = Normalize(path);
        _byKey.TryGetValue(key, out var record);

        if (view is null) {
            if (record is null || record.View is null) {
                return false;
            }
            var cleared = record with { View = null };
            if (cleared.IsEmpty) {
                _byKey.Remove(key);
            } else {
                _byKey[key] = cleared;
            }

            return true;
        }

        var pinned = record is null
            ? new FolderRecord(key, createdUtc, today, view)
            : record with { View = view, LastVisit = today, CreatedUtc = record.CreatedUtc ?? createdUtc };
        if (pinned == record) {
            return false;
        }

        _byKey[key] = pinned;
        Trim();

        return true;
    }

    /// <summary>
    /// A folder was renamed or moved by Wander: every record at or under
    /// <paramref name="oldRoot"/> now answers to the new path
    /// (<see cref="PathRewrite.Under"/>). A record already at the new path
    /// gives way to the one that moved there. Returns how many followed.
    /// </summary>
    public int Follow(string oldRoot, string newRoot) {
        var moves = new List<(string From, string To)>();
        foreach (var key in _byKey.Keys) {
            if (PathRewrite.Under(key, oldRoot, newRoot) is { } moved) {
                moves.Add((key, Normalize(moved)));
            }
        }

        foreach (var (from, to) in moves) {
            var record = _byKey[from];
            _byKey.Remove(from);
            _byKey[to] = record with { Path = to };
        }

        return moves.Count;
    }

    /// <summary>
    /// Records that may belong to <paramref name="path"/> under a former
    /// name: the same creation time, on the same volume, at another path.
    /// A static pass over a snapshot so the caller can run it - and the
    /// <c>stat</c> of each candidate that follows - on the pool.
    /// </summary>
    public static IReadOnlyList<FolderRecord> AdoptCandidates(
        IReadOnlyList<FolderRecord> records, string path, DateTime createdUtc) {
        string key = Normalize(path);
        string? root = Path.GetPathRoot(key);
        var found = new List<FolderRecord>();
        foreach (var record in records) {
            if (record.CreatedUtc != createdUtc
                || string.Equals(record.Path, key, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetPathRoot(record.Path), root, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            found.Add(record);
        }

        return found;
    }

    /// <summary>
    /// A folder with no record arrived, and the caller checked which of its
    /// <see cref="AdoptCandidates"/> no longer exist on disk
    /// (<paramref name="gone"/>). Exactly one gone candidate is the folder
    /// under its old name: its record is re-keyed to <paramref name="path"/>
    /// and the old path returned. Two candidates (a copy that kept its
    /// dates - robocopy, an unpacker) or none: nothing is adopted, and null
    /// comes back.
    /// </summary>
    public string? Adopt(string path, DateTime createdUtc, IReadOnlyCollection<string> gone) {
        string key = Normalize(path);
        if (_byKey.ContainsKey(key)) {
            return null;
        }

        var goneKeys = new HashSet<string>(gone.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var vacated = AdoptCandidates(Records, key, createdUtc)
            .Where(c => goneKeys.Contains(c.Path))
            .ToList();
        if (vacated.Count != 1) {
            return null;
        }

        string from = vacated[0].Path;
        _byKey.Remove(from);
        _byKey[key] = vacated[0] with { Path = key };

        return from;
    }

    /// <summary>
    /// Drops the records least recently visited until the book fits its
    /// cap. Returns how many went.
    /// </summary>
    public int Trim() {
        int excess = _byKey.Count - _capacity;
        if (excess <= 0) {
            return 0;
        }

        var oldest = _byKey.Values
            .OrderBy(r => r.LastVisit)
            .ThenByDescending(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Take(excess)
            .Select(r => r.Path)
            .ToList();
        foreach (string key in oldest) {
            _byKey.Remove(key);
        }

        return oldest.Count;
    }


    private static string Normalize(string path) {
        return Path.TrimEndingDirectorySeparator(path.Trim());
    }
}
