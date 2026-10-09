using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Persistence;

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
/// it out. <see cref="Top"/> is the row to put first on screen, when the
/// intent named one.
/// </summary>
public sealed record ArrivalDecision(
    ArrivalOutcome Outcome,
    IReadOnlyList<FileSystemEntry> Rows,
    bool TakeFocus = false,
    string? RenameTarget = null,
    string? FolderPath = null,
    string? Top = null) {

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
/// <param name="Renames">
/// The renames in this burst, old path to new, for the re-listing: a
/// selected file renamed by another program stays selected under its new
/// name (decision B7).
/// </param>
public sealed record WatchTickDecision(
    WatchOutcome Outcome,
    bool RefreshTrees = false,
    IReadOnlyList<FileSystemEntry>? Rows = null,
    IReadOnlyList<string>? Stale = null,
    IReadOnlyList<(string From, string To)>? Renames = null) {

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
    private const int PlaceMemoryLimit = 64;

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

    // Where the user was in each folder they have been in - the place in
    // its list and the rating filter over it - so coming back lands there.
    // Capped, the folder left longest ago going first: a long session walks
    // through a lot of folders, and none of this is worth keeping forever.
    private readonly Dictionary<string, FolderPlace> _places = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _placeOrder = new();

    // What the watcher has noted since the last tick answered. Lives here
    // because "re-list everything" versus "re-read these two rows" is a
    // decision about the listing, and the accumulator is part of it.
    private readonly FolderChanges _pendingChanges = new();


    /// <summary>The folder whose rows are on screen, or null between listings.</summary>
    public string? ListedPath => _listedPath;

    /// <summary>The pending intent, or null. Read-only outside; set through <see cref="SetArrival"/>.</summary>
    public ArrivalIntent? Arrival => _arrival;

    /// <summary>
    /// Coming back to a folder puts the list where it was left - the same
    /// file, the same row on top - however it is reached, a panel row too
    /// (<c>AppSettings.RememberFolderPlace</c>). Off: the file left selected
    /// there, brought into view, and only on a way in that names nothing
    /// else - the history, the address, a row of the list.
    /// </summary>
    public bool RestoresPlace { get; set; }

    /// <summary>Coming back to a folder puts back the rating filter it was left with (<see cref="FilterFor"/>).</summary>
    public bool RestoresFilter { get; set; }

    /// <summary>The folders remembered, the most recently left first - what the next session starts with (<see cref="LoadPlaces"/>).</summary>
    public IReadOnlyList<FolderPlace> Places => Enumerable.Reverse(_placeOrder).Select(folder => _places[folder]).ToArray();


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
    /// The intent of an operation, kept only when it is about the folder on
    /// screen (N8). Pasted into a subfolder, the rows are not in this
    /// listing, and an intent left waiting for that folder took the
    /// selection and the keyboard the next time anyone walked in, whatever
    /// for. A navigation's own intent is <see cref="SetArrival"/>'s.
    /// </summary>
    /// <returns>True when it was kept.</returns>
    public bool SetArrivalHere(ArrivalIntent intent) {
        if (!IsSamePath(intent.ForFolder, _listedPath)) {
            return false;
        }

        _arrival = intent;

        return true;
    }

    /// <summary>
    /// A folder Wander moved or renamed: what is remembered about it and
    /// under it follows (REDESIGN 4.12) - the folder on screen, where the
    /// user was in each folder, and the pending intent - so walking back in
    /// lands where they left.
    /// </summary>
    public void RewriteMemory(string from, string to) {
        _listedPath = Moved(_listedPath, from, to);
        if (_arrival is { } pending) {
            _arrival = pending with {
                ForFolder = Moved(pending.ForFolder, from, to),
                Paths = pending.Paths.Select(p => Moved(p, from, to)!).ToArray(),
                RenameTarget = Moved(pending.RenameTarget, from, to),
            };
        }

        // A folder under the moved one, or a row of one: the remembered
        // place follows, in its slot of the order.
        for (int i = 0; i < _placeOrder.Count; i++) {
            var place = _places[_placeOrder[i]];
            var moved = place with { Folder = Moved(place.Folder, from, to)!, Place = place.Place?.Moved(from, to) };
            _places.Remove(place.Folder);
            _places[moved.Folder] = moved;
            _placeOrder[i] = moved.Folder;
        }
    }


    /// <summary>What the last session remembered, the most recently left first (<see cref="Places"/>). Replaces what is held.</summary>
    public void LoadPlaces(IReadOnlyList<FolderPlace> places) {
        _places.Clear();
        _placeOrder.Clear();
        foreach (var place in places.Reverse().Where(p => p.Folder.Length > 0)) {
            Note(place);
        }
    }

    /// <summary>
    /// The rating filter <paramref name="folder"/> starts with as it is
    /// walked into: the one it was left with, while
    /// <see cref="RestoresFilter"/>; none otherwise.
    /// </summary>
    public RatingFilter FilterFor(string? folder) {
        return RestoresFilter && folder is not null && _places.TryGetValue(folder, out var left)
            ? new RatingFilter(left.FilterRanks, left.FilterColors)
            : RatingFilter.None;
    }


    /// <summary>
    /// Navigation is happening. Notes where the user was in the folder
    /// being left (so walking back in lands there), drops an intent that
    /// belongs to an overtaken navigation, and — when no caller knew better
    /// — plans the default: going up highlights the folder we came out of,
    /// anything else falls back to where the user was there last time.
    /// With <see cref="RestoresPlace"/> that is the whole place, the row on
    /// top too, and a panel row's "the folder itself" gives way to it.
    /// </summary>
    /// <param name="navigatingTo">Where navigation is going; null when leaving to nowhere.</param>
    /// <param name="selectedPath">The primary selected row of the folder being left, if any.</param>
    /// <param name="place">Where the list stood in the folder being left; null when its rows are not the folder's - search results.</param>
    /// <param name="filter">The rating filter over the folder being left; null leaves the one remembered.</param>
    public void OnNavigating(string? navigatingTo, string? selectedPath, ListPlace? place = null, RatingFilter? filter = null) {
        RememberSelection(selectedPath, place, filter);

        // An intent for another folder belongs to a navigation this one
        // overtook — including the plain "keep what was selected", which
        // always names the folder being left.
        if (_arrival is { } pending && !IsSamePath(pending.ForFolder, navigatingTo)) {
            _arrival = null;
        }
        if (navigatingTo is null) {
            return;
        }

        _places.TryGetValue(navigatingTo, out var remembered);
        var kept = RestoresPlace ? remembered?.Place : null;

        // A panel row asks for the folder itself, nothing in the list; the
        // place left there is the list the user comes back to all the same.
        if (_arrival is { Action: ArrivalAction.SelectFolderItself } && kept is not null) {
            _arrival = Back(navigatingTo, kept);

            return;
        }

        // A caller that already said what it wants said it about this
        // navigation, one line before starting it. Nothing to guess.
        if (_arrival is not null) {
            return;
        }

        if (_listedPath is { } left && IsSamePath(navigatingTo, ParentOf(left))) {
            _arrival = ArrivalIntent.Rows(navigatingTo, new[] { left }) with { Top = kept?.Top };

            return;
        }

        if (kept is not null) {
            _arrival = Back(navigatingTo, kept);
        } else if (remembered?.Place?.Row is { } row) {
            _arrival = ArrivalIntent.Rows(navigatingTo, new[] { row });
        }
    }


    /// <summary>
    /// Notes where the user is in the folder currently on screen: its place
    /// - or only the selected row, when the rows are not the folder's - and
    /// its rating filter; what is not given stays as remembered. Bounded:
    /// the folder left longest ago goes when the cap is reached.
    /// </summary>
    public void RememberSelection(string? selectedPath, ListPlace? place = null, RatingFilter? filter = null) {
        if (_listedPath is not { } folder || (selectedPath is null && place is null && filter is null)) {
            return;
        }

        var now = _places.TryGetValue(folder, out var was) ? was : new FolderPlace { Folder = folder };
        if (place is not null) {
            now = now with { Place = place };
        } else if (selectedPath is not null) {
            now = now with { Place = new ListPlace { Row = selectedPath } };
        }
        if (filter is not null) {
            now = now with { FilterRanks = filter.Ranks, FilterColors = filter.Colors };
        }
        Note(now);
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
    ///
    /// <para>
    /// None of the wanted rows listed: an intent that knows the rows they
    /// stood among (the last session's place, 2026-09-25) selects the one
    /// that took their place, by the list's own rule; with none of those
    /// either, nothing is selected and nothing is scrolled - the folder from
    /// its top. Its row for the top of the screen goes with a selection, or
    /// alone when no row was asked for.
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
        // Nothing found still takes the keyboard when the intent was to: a
        // window come up on a folder with no row to stand on leaves it on
        // itself otherwise, where the arrows move nothing.
        if (intent.Paths.Count == 0) {
            return new ArrivalDecision(ArrivalOutcome.NothingFound, Array.Empty<FileSystemEntry>(), intent.TakeFocus, Top: intent.Top);
        }

        var wanted = new HashSet<string>(intent.Paths, StringComparer.OrdinalIgnoreCase);
        var found = rows.Where(e => wanted.Contains(e.FullPath)).ToList();
        if (found.Count == 0 && intent.StoodAmong is { } stood
            && CurrentRowFallback.After(stood, intent.Paths, rows.Select(r => r.FullPath).ToList()) is { } next) {
            found = rows.Where(e => IsSamePath(e.FullPath, next)).Take(1).ToList();
        }
        if (found.Count == 0) {
            return new ArrivalDecision(ArrivalOutcome.NothingFound, Array.Empty<FileSystemEntry>(), intent.TakeFocus);
        }

        // Only for the row it was asked for: a listing that landed for some
        // other reason must not open an editor under the user's hands.
        string? rename = intent.RenameTarget is { } pending && IsSamePath(found[0].FullPath, pending)
            ? pending
            : null;

        return new ArrivalDecision(ArrivalOutcome.SelectRows, found, intent.TakeFocus, rename, Top: intent.Top);
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
            var renames = _pendingChanges.Renames.ToArray();
            _pendingChanges.Clear();

            return new WatchTickDecision(WatchOutcome.Relist, RefreshTrees: true, Stale: stale, Renames: renames);
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

    private static string? Moved(string? path, string from, string to) {
        return path is null ? null : PathRewrite.Under(path, from, to) ?? path;
    }

    /// <summary>Coming back to where the list stood: a walk, not a start - the keyboard stays where it is.</summary>
    private static ArrivalIntent Back(string folder, ListPlace place) {
        return ArrivalIntent.Place(folder, place.Row, place.StoodAmong, place.Top) with { TakeFocus = false };
    }

    /// <summary>Keeps <paramref name="place"/> as the folder left last; the one left longest ago goes past the cap.</summary>
    private void Note(FolderPlace place) {
        _placeOrder.RemoveAll(folder => IsSamePath(folder, place.Folder));
        _placeOrder.Add(place.Folder);
        _places.Remove(place.Folder);
        _places[place.Folder] = place;
        if (_placeOrder.Count > PlaceMemoryLimit) {
            _places.Remove(_placeOrder[0]);
            _placeOrder.RemoveAt(0);
        }
    }


    /// <summary>The folder one level up, or null at a drive root.</summary>
    private static string? ParentOf(string path) {
        return Path.GetDirectoryName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
