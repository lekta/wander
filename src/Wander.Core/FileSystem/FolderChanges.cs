namespace Wander.Core.FileSystem;

/// <summary>
/// What a burst of <see cref="DirectoryChange"/> adds up to, and what the
/// listing has to do about it.
///
/// <para>
/// Watcher events arrive on a background thread, in bursts, and several per
/// actual change — one atomic replace is three of them. They have to be
/// collected until the throttle fires and then answered <em>once</em>. This
/// is that accumulator, and it lives in Core rather than in the view model
/// because deciding "re-list everything" versus "re-read these two rows" is
/// the whole substance of not making the folder jump, and it is worth
/// testing.
/// </para>
/// </summary>
public sealed class FolderChanges {
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private bool _structural;


    /// <summary>True when nothing has been noted since the last <see cref="Clear"/>.</summary>
    public bool IsEmpty => !_structural && _paths.Count == 0;

    /// <summary>
    /// True when the folder's composition changed and the only honest answer
    /// is a fresh listing.
    /// </summary>
    public bool NeedsRelisting => _structural;

    /// <summary>
    /// Every path the watcher named since the last <see cref="Clear"/>,
    /// whether the change was structural or not.
    ///
    /// <para>
    /// The structural ones are in here too because of what an appearance
    /// means to a cache: a file deleted and replaced by another one under
    /// the same name is a new picture at an old path, and anything keyed by
    /// path alone (the thumbnail caches) goes on showing the old one. The
    /// re-listing does not fix that - the row is rebuilt, the path is not.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<string> ChangedPaths => _paths;


    public void Note(DirectoryChange change) {
        if (change.Structural || change.Path.Length == 0) {
            _structural = true;
        }

        if (change.Path.Length > 0) {
            _paths.Add(change.Path);
        }
    }


    public void Clear() {
        _structural = false;
        _paths.Clear();
    }


    /// <summary>
    /// The rows a set of changed files touches — either because a row
    /// <em>is</em> one of them, or because one of them is a companion folded
    /// into that row (a <c>.pp3</c> beside a photograph is not a row of its
    /// own, but it is what a row shows).
    ///
    /// <para>
    /// Returns null when some changed file matches no row at all. That means
    /// the listing does not know about it, and the only way to find out what
    /// it is — a file the filters hid, something that arrived without a
    /// creation event, a companion of a file we are not showing — is to list
    /// the folder again. Guessing there is how a list quietly goes out of
    /// sync with the disk.
    /// </para>
    /// </summary>
    public static IReadOnlyList<FileSystemEntry>? RowsFor(
        IReadOnlyList<FileSystemEntry> rows, IReadOnlyCollection<string> changedPaths) {
        if (changedPaths.Count == 0) {
            return Array.Empty<FileSystemEntry>();
        }

        var byPath = new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) {
            byPath[row.FullPath] = row;
            if (row.Companions is not { Count: > 0 } companions) {
                continue;
            }
            foreach (string companion in companions) {
                byPath[companion] = row;
            }
        }

        var touched = new List<FileSystemEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in changedPaths) {
            if (!byPath.TryGetValue(path, out var row)) {
                return null;
            }
            if (seen.Add(row.FullPath)) {
                touched.Add(row);
            }
        }

        return touched;
    }
}
