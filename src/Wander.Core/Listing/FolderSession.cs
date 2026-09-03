using Wander.Core.FileSystem;

namespace Wander.Core.Listing;

/// <summary>What a landed listing should do about the pending intent.</summary>
public enum ArrivalOutcome {
    /// <summary>Nothing to do — no intent, or it is still waiting for its own folder's listing.</summary>
    None,

    /// <summary>Select the listed folder itself (it is never a row in its own listing).</summary>
    SelectFolder,

    /// <summary>The intent was consumed but none of its rows are in the listing.</summary>
    NothingFound,

    /// <summary>Select <see cref="ArrivalDecision.Rows"/>.</summary>
    SelectRows,
}


/// <summary>
/// The answer to "a listing just landed — what should be selected". Computed
/// by <see cref="FolderSession.DecideArrival"/>; the view model only carries
/// it out.
/// </summary>
public sealed record ArrivalDecision(
    ArrivalOutcome Outcome,
    IReadOnlyList<FileSystemEntry> Rows,
    bool TakeFocus = false,
    string? RenameTarget = null,
    string? FolderPath = null) {

    public static readonly ArrivalDecision None =
        new(ArrivalOutcome.None, Array.Empty<FileSystemEntry>());
}


/// <summary>What a watcher tick should do about the changes it has collected.</summary>
public enum WatchOutcome {
    /// <summary>Nothing pending — the timer can stop until the next change.</summary>
    Idle,

    /// <summary>
    /// Something is pending but now is not the moment: a name is being
    /// edited in place, or our own operation is running. The changes stay
    /// noted and are answered on a later tick.
    /// </summary>
    Hold,

    /// <summary>The folder holds a different set of files — list it again.</summary>
    Relist,

    /// <summary>Only file contents changed — re-read these rows in place.</summary>
    RefreshRows,
}


/// <summary>
/// The answer to "the watcher's throttle fired — what now". Computed by
/// <see cref="FolderSession.DecideWatchTick"/>.
/// </summary>
/// <param name="RefreshTrees">
/// True when the composition change is worth pushing at the folder panels
/// too — subfolders are rows there as well as in the list. False for the
/// fallback re-listing taken because a changed file matched no row: nothing
/// says the panels are affected, and the original code never refreshed them
/// on that path.
/// </param>
/// <param name="Stale">
/// Paths whose picture on screen can no longer be trusted — every file the
/// watcher named in this burst. Caches keyed by path alone (thumbnails)
/// have to be told, because neither a re-listing nor a row re-read changes
/// a path: a photograph deleted and replaced under the same name keeps
/// showing the deleted one otherwise.
/// </param>
public sealed record WatchTickDecision(
    WatchOutcome Outcome,
    bool RefreshTrees = false,
    IReadOnlyList<FileSystemEntry>? Rows = null,
    IReadOnlyList<string>? Stale = null) {

    public static readonly WatchTickDecision Idle = new(WatchOutcome.Idle);
    public static readonly WatchTickDecision Hold = new(WatchOutcome.Hold);
}


/// <summary>
/// The state of "the folder being looked at": which listing the rows on
/// screen belong to, what should be selected when the pending listing lands,
/// where the user was in folders they have left, and what the folder watcher
/// has collected since its last tick.
///
/// <para>
/// This is a machine of decisions, not of work: facts go in ("navigating to
/// X", "the listing for epoch N landed", "the watcher noted a change"),
/// decisions come out ("publish", "select these rows", "re-list"). Nothing
/// here touches the disk, a thread or a collection the UI is bound to — the
/// view model executes what this decides. That split is the point: every
/// "who won the race" question in here is answerable by a test, where the
/// same logic inline in the view model was answerable only by hand.
/// </para>
/// </summary>
public sealed class FolderSession {
    private const int SelectionMemoryLimit = 64;

    // Which listing the rows on screen belong to. Bumped whenever a new one
    // starts, and captured by every background pass that computes rows for
    // it — a pass that comes back for an older epoch is answering about a
    // folder nobody is looking at any more. One question, asked in one way,
    // instead of a separate "is this still mine" check invented by each pass.
    private int _epoch;

    private string? _listedPath;

    // What to select once the pending listing lands — one slot, because it
    // is one question. Set by whoever knows better than "whatever was
    // selected before": a rename knows the new name, an undo knows what it
    // put back, a click in the tree knows the folder. See ArrivalIntent for
    // why there is only one.
    private ArrivalIntent? _arrival;

    // Where the user was in each folder they have been in, so coming back
    // lands on the same row. Capped and oldest-first: a long session walks
    // through a lot of folders, and none of this is worth keeping forever.
    private readonly Dictionary<string, string> _selectionMemory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _selectionMemoryOrder = new();

    // What the watcher has noted since the last tick answered. Lives here
    // because "re-list everything" versus "re-read these two rows" is a
    // decision about the listing, and the accumulator is part of it.
    private readonly FolderChanges _pendingChanges = new();


    /// <summary>The folder whose rows are on screen, or null between listings.</summary>
    public string? ListedPath => _listedPath;

    /// <summary>The pending intent, or null. Read-only outside; set through <see cref="SetArrival"/>.</summary>
    public ArrivalIntent? Arrival => _arrival;


    // --- Epochs -----------------------------------------------------------

    /// <summary>
    /// A new listing is starting. Returns its epoch — every pass computing
    /// rows for it must carry that number to <see cref="IsCurrent"/>.
    /// <paramref name="arriving"/> is true when this is a walk into a
    /// different folder rather than a re-listing of the one on screen: the
    /// view mode is chosen for an arrival, not for every F5.
    /// </summary>
    public int BeginListing(string path, out bool arriving) {
        arriving = !IsSamePath(path, _listedPath);
        if (arriving) {
            _listedPath = null;
        }

        return ++_epoch;
    }


    /// <summary>
    /// Everything in flight is stale — search results are taking the list
    /// over, and a folder read or a rating pass that lands later must not
    /// overwrite them. No path change: the folder underneath is unchanged.
    /// </summary>
    public void InvalidateListings() {
        _epoch++;
    }


    /// <summary>
    /// Whether an answer computed for <paramref name="epoch"/> is still
    /// about the listing on screen. Checked in the one place rows are
    /// published, and by every background pass before it bothers finishing.
    /// </summary>
    public bool IsCurrent(int epoch) {
        return epoch == _epoch;
    }


    /// <summary>The listing landed: the rows on screen are now this folder's.</summary>
    public void NoteListed(string path) {
        _listedPath = path;
    }


    /// <summary>The folder is gone, unreadable, or there is no folder — no rows belong to anything.</summary>
    public void NoteListingGone() {
        _listedPath = null;
    }


    // --- Arrival intent ---------------------------------------------------

    /// <summary>
    /// Replaces the pending intent. Replaces, never accumulates: two
    /// callers both leaving one would otherwise race, and the winner would
    /// fall out of the order of the lines — see <see cref="ArrivalIntent"/>.
    /// </summary>
    public void SetArrival(ArrivalIntent intent) {
        _arrival = intent;
    }


    /// <summary>
    /// Navigation is happening. Notes where the user was in the folder
    /// being left (so walking back in lands on the same row), drops an
    /// intent that belongs to an overtaken navigation, and — when no caller
    /// knew better — plans the default: going up highlights the folder we
    /// came out of, anything else falls back to what was selected there
    /// last time.
    /// </summary>
    /// <param name="navigatingTo">Where navigation is going; null when leaving to nowhere.</param>
    /// <param name="selectedPath">The primary selected row of the folder being left, if any.</param>
    public void OnNavigating(string? navigatingTo, string? selectedPath) {
        RememberSelection(selectedPath);

        // An intent for another folder belongs to a navigation this one
        // overtook — including the plain "keep what was selected", which
        // always names the folder being left.
        if (_arrival is { } pending && !IsSamePath(pending.ForFolder, navigatingTo)) {
            _arrival = null;
        }

        // A caller that already said what it wants said it about this
        // navigation, one line before starting it. Nothing to guess.
        if (_arrival is not null || navigatingTo is null) {
            return;
        }

        if (_listedPath is { } left && IsSamePath(navigatingTo, ParentOf(left))) {
            _arrival = ArrivalIntent.Rows(navigatingTo, new[] { left });

            return;
        }

        if (_selectionMemory.TryGetValue(navigatingTo, out string? remembered)) {
            _arrival = ArrivalIntent.Rows(navigatingTo, new[] { remembered });
        }
    }


    /// <summary>
    /// Notes where the user is in the folder currently on screen. Bounded:
    /// the oldest folder's memory goes when the cap is reached.
    /// </summary>
    public void RememberSelection(string? selectedPath) {
        if (_listedPath is not { } path || selectedPath is null) {
            return;
        }

        if (!_selectionMemory.ContainsKey(path)) {
            _selectionMemoryOrder.Enqueue(path);
            if (_selectionMemoryOrder.Count > SelectionMemoryLimit) {
                _selectionMemory.Remove(_selectionMemoryOrder.Dequeue());
            }
        }

        _selectionMemory[path] = selectedPath;
    }


    /// <summary>
    /// A listing has landed — what should be selected? The one place an
    /// intent is consumed.
    ///
    /// <para>
    /// An intent for some other folder keeps waiting for its own listing —
    /// a navigation that overtook it is dropped in
    /// <see cref="OnNavigating"/>, not here. An empty list is not an answer
    /// either: it is the gap between leaving one folder and the next one
    /// listing, and consuming the intent there is what stopped "up one
    /// level" from highlighting the folder it came out of.
    /// </para>
    /// </summary>
    public ArrivalDecision DecideArrival(string? currentFolder, IReadOnlyList<FileSystemEntry> rows) {
        if (_arrival is not { } intent) {
            return ArrivalDecision.None;
        }

        if (!IsSamePath(intent.ForFolder, currentFolder)) {
            return ArrivalDecision.None;
        }

        if (intent.Action == ArrivalAction.SelectFolderItself) {
            // A folder is not a row in its own listing, so an empty listing
            // is no obstacle: there is nothing to look for in it.
            _arrival = null;

            return new ArrivalDecision(
                ArrivalOutcome.SelectFolder, Array.Empty<FileSystemEntry>(), FolderPath: intent.Paths[0]);
        }

        if (rows.Count == 0) {
            return ArrivalDecision.None;
        }

        _arrival = null;
        var wanted = new HashSet<string>(intent.Paths, StringComparer.OrdinalIgnoreCase);
        var found = rows.Where(e => wanted.Contains(e.FullPath)).ToList();
        if (found.Count == 0) {
            return new ArrivalDecision(ArrivalOutcome.NothingFound, Array.Empty<FileSystemEntry>());
        }

        // Only for the row it was asked for: a listing that landed for some
        // other reason must not open an editor under the user's hands.
        string? rename = intent.RenameTarget is { } pending && IsSamePath(found[0].FullPath, pending)
            ? pending
            : null;

        return new ArrivalDecision(ArrivalOutcome.SelectRows, found, intent.TakeFocus, rename);
    }


    // --- Folder watcher ---------------------------------------------------

    /// <summary>The watcher saw something. Collected until the throttle asks <see cref="DecideWatchTick"/>.</summary>
    public void NoteChange(DirectoryChange change) {
        _pendingChanges.Note(change);
    }


    /// <summary>The folder is not being watched any more — pending changes answer nothing.</summary>
    public void ForgetPendingChanges() {
        _pendingChanges.Clear();
    }


    /// <summary>
    /// The throttle fired — what now? Idempotent: a tick with nothing
    /// pending decides <see cref="WatchOutcome.Idle"/> and changes no
    /// state, however many times it fires.
    /// </summary>
    /// <param name="busy">
    /// True while a name is being edited in place or our own file operation
    /// is running — two things a re-listing must not interrupt. The changes
    /// stay pending, not dropped, and are answered on a later tick.
    /// </param>
    /// <param name="rows">The full listing behind the screen, for matching changed files to rows.</param>
    public WatchTickDecision DecideWatchTick(bool busy, IReadOnlyList<FileSystemEntry> rows) {
        if (_pendingChanges.IsEmpty) {
            return WatchTickDecision.Idle;
        }

        if (busy) {
            return WatchTickDecision.Hold;
        }

        // Every path this burst touched, kept before the accumulator is
        // cleared: whatever the outcome, the caller has to drop what it
        // cached about these files.
        var stale = _pendingChanges.ChangedPaths.ToArray();

        // A file appeared, vanished or was renamed: the folder holds a
        // different set of files than the one on screen, and only a fresh
        // listing can say what it is now.
        if (_pendingChanges.NeedsRelisting) {
            _pendingChanges.Clear();

            return new WatchTickDecision(WatchOutcome.Relist, RefreshTrees: true, Stale: stale);
        }

        // Nothing appeared or vanished — some files were written to. If
        // every one of them belongs to a row we are showing, those rows are
        // re-read and swapped in place: no new listing, no rebuilt
        // containers, no selection to put back. This is the path a rating
        // written into a sidecar takes, whoever wrote it — us or RawTherapee
        // in the next window.
        var touched = FolderChanges.RowsFor(rows, _pendingChanges.ChangedPaths);
        _pendingChanges.Clear();

        if (touched is null) {
            return new WatchTickDecision(WatchOutcome.Relist, Stale: stale);
        }

        return touched.Count > 0
            ? new WatchTickDecision(WatchOutcome.RefreshRows, Rows: touched, Stale: stale)
            : WatchTickDecision.Idle;
    }


    // --- Helpers ----------------------------------------------------------

    private static bool IsSamePath(string? a, string? b) {
        return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>The folder one level up, or null at a drive root.</summary>
    private static string? ParentOf(string path) {
        return Path.GetDirectoryName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
