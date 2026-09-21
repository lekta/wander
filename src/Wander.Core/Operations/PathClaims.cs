namespace Wander.Core.Operations;

/// <summary>Who holds a path: a reader of ours that steps aside, or an operation the user started.</summary>
public enum ClaimKind {
    /// <summary>A thumbnail, a preview, a text extraction - anything the user did not ask for by name. Yields to a file operation.</summary>
    Background,

    /// <summary>A copy, a move, a delete, an action of the user's own. Never interrupted; named when it is in the way.</summary>
    UserOperation,
}


/// <param name="Path">The claimed path - a source of the operation, not every file under it.</param>
/// <param name="Kind">Whether the holder steps aside or is named.</param>
/// <param name="Owner">
/// A resource key naming the holder: an <see cref="OperationVerbs"/> key for
/// a user operation, a <see cref="ClaimOwners"/> key for a background reader -
/// what a wait for the path calls it ("Wander: thumbnail").
/// </param>
public sealed record PathClaim(string Path, ClaimKind Kind, string Owner);


/// <summary>What a background reader is called when it is in the way - resource keys, like <see cref="OperationVerbs"/>.</summary>
public static class ClaimOwners {
    /// <summary>The shell's own thumbnail of a file.</summary>
    public const string Thumbnail = "HolderThumbnail";

    /// <summary>The system's document filter, reading a file's text for the search.</summary>
    public const string ContentSearch = "HolderContentSearch";
}


/// <summary>
/// The answer to "is Wander itself doing something with this file" (PLAN AF,
/// 2026-09-21). Whoever works on a user's path says so for the time of the
/// work; a file operation about to touch a path asks first, and then
/// background readers are told to let go (<see cref="Yield"/>) while an
/// operation of the user's is named instead of being fought - and the list
/// puts a badge on what is being worked on.
///
/// <para>
/// Cheap by construction: a claim is a <b>source</b> of an operation - the
/// folder being copied, not the hundred thousand files in it - so the table
/// holds tens of rows, thousands for a huge selection. A path is looked up
/// by itself and its ancestors, a dictionary hit per level, plus one scan
/// of the table for what is claimed inside it - the table cannot tell a
/// file from a folder, and the scan is what the numbers below show. Measured
/// 2026-09-21, Release: <see cref="Covering"/> costs 1.5 us with 50 claims,
/// 28 us with 5000, 130 us with 50 000 (the scan is linear in the table);
/// <see cref="IsClaimed"/> - what a row's badge asks - 0.5 us at any size;
/// claiming 50 000 paths takes 7 ms.
/// </para>
///
/// <para>
/// Thread-safe. <see cref="Changed"/> is raised outside the lock, on
/// whatever thread made the change - for a user operation's claim only: a
/// background reader's come and go by the hundred while a folder scrolls
/// past, and nothing on screen shows them.
/// </para>
/// </summary>
public sealed class PathClaims {
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<Entry>> _byPath = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>A user operation's claim appeared or went. Subscribers marshal to their own thread.</summary>
    public event EventHandler? Changed;


    /// <summary>How many paths are claimed - the table a lookup scans, for the log.</summary>
    public int Count {
        get {
            lock (_gate) {
                return _byPath.Count;
            }
        }
    }


    /// <summary>
    /// Claims <paramref name="paths"/> until the returned token is disposed.
    /// </summary>
    /// <param name="paths">The sources of the work, as the user named them.</param>
    /// <param name="kind">Whether the holder steps aside or is named.</param>
    /// <param name="owner">See <see cref="PathClaim.Owner"/>.</param>
    /// <param name="yield">
    /// For a background reader: cancelled by <see cref="Yield"/> when a file
    /// operation needs the path. The reader still disposes its claim itself,
    /// once its handle is closed - that is what the operation waits for.
    /// </param>
    public IDisposable Claim(IEnumerable<string> paths, ClaimKind kind, string owner, CancellationTokenSource? yield = null) {
        var token = new Token(this);
        var entries = paths.Select(p => new Entry(new PathClaim(Normalize(p), kind, owner), yield, token)).ToList();
        token.Entries = entries;
        lock (_gate) {
            foreach (var entry in entries) {
                if (!_byPath.TryGetValue(entry.Claim.Path, out var list)) {
                    _byPath[entry.Claim.Path] = list = new List<Entry>(1);
                }
                list.Add(entry);
            }
        }
        if (entries.Count > 0 && kind == ClaimKind.UserOperation) {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return token;
    }

    /// <summary>
    /// Every claim an operation on <paramref name="path"/> would run into:
    /// on the path itself, on a folder above it, on anything inside it.
    /// </summary>
    /// <param name="path">What the operation is about to touch.</param>
    /// <param name="except">The asking operation's own claim - it is never in its own way.</param>
    public IReadOnlyList<PathClaim> Covering(string path, IDisposable? except = null) {
        lock (_gate) {
            return Find(Normalize(path)).Where(e => !ReferenceEquals(e.Token, except)).Select(e => e.Claim).ToList();
        }
    }

    /// <summary>True when the path itself or a folder above it is claimed - what a badge on a row asks.</summary>
    /// <param name="path">The row's path.</param>
    /// <param name="kind">Only claims of this kind count; null counts every claim.</param>
    public bool IsClaimed(string path, ClaimKind? kind = null) {
        lock (_gate) {
            for (string? at = Normalize(path); !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at)) {
                if (_byPath.TryGetValue(at, out var list) && (kind is null || list.Any(e => e.Claim.Kind == kind))) {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Tells every background reader in the way of <paramref name="path"/>
    /// to stop, and returns who they were, for the log. User operations are
    /// left alone - they are in <see cref="Covering"/> for the caller to name.
    /// </summary>
    public IReadOnlyList<PathClaim> Yield(string path) {
        List<Entry> asked;
        lock (_gate) {
            asked = Find(Normalize(path)).Where(e => e.Claim.Kind == ClaimKind.Background).ToList();
        }
        foreach (var entry in asked) {
            try {
                entry.YieldSource?.Cancel();
            } catch (ObjectDisposedException) {
                // The reader finished between the lookup and the call.
            }
        }

        return asked.Select(e => e.Claim).ToList();
    }


    private List<Entry> Find(string path) {
        var found = new List<Entry>();
        for (string? at = path; !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at)) {
            if (_byPath.TryGetValue(at, out var list)) {
                found.AddRange(list);
            }
        }

        string inside = path + Path.DirectorySeparatorChar;
        foreach (var (claimed, list) in _byPath) {
            if (claimed.StartsWith(inside, StringComparison.OrdinalIgnoreCase)) {
                found.AddRange(list);
            }
        }

        return found;
    }

    private void Release(IReadOnlyList<Entry> entries) {
        lock (_gate) {
            foreach (var entry in entries) {
                if (_byPath.TryGetValue(entry.Claim.Path, out var list) && list.Remove(entry) && list.Count == 0) {
                    _byPath.Remove(entry.Claim.Path);
                }
            }
        }
        if (entries.Any(e => e.Claim.Kind == ClaimKind.UserOperation)) {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string Normalize(string path) {
        return Path.TrimEndingDirectorySeparator(path);
    }


    private sealed class Entry {
        public Entry(PathClaim claim, CancellationTokenSource? yieldSource, Token token) {
            Claim = claim;
            YieldSource = yieldSource;
            Token = token;
        }


        public PathClaim Claim { get; }

        public CancellationTokenSource? YieldSource { get; }

        /// <summary>The claim this entry came with - what <see cref="Covering"/> leaves out for its owner.</summary>
        public Token Token { get; }
    }


    private sealed class Token : IDisposable {
        private readonly PathClaims _owner;
        private int _released;


        public Token(PathClaims owner) {
            _owner = owner;
        }


        public IReadOnlyList<Entry> Entries { get; set; } = Array.Empty<Entry>();


        public void Dispose() {
            if (Interlocked.Exchange(ref _released, 1) == 0) {
                _owner.Release(Entries);
            }
        }
    }
}
