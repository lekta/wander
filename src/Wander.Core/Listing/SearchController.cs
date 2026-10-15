using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Search;

namespace Wander.Core.Listing;

/// <summary>
/// Live text filter over a list of <see cref="FileSystemEntry"/>. The owner
/// (typically <c>MainViewModel</c>) hands in the post-hidden/system snapshot
/// via <see cref="SetSource"/> after every folder refresh; every query
/// change re-projects that snapshot through a case-insensitive
/// <c>Name.Contains</c> filter on a background thread and publishes the
/// result via <see cref="FilteredChanged"/>.
///
/// <para>
/// Lives in Core (no WPF deps) and implements INPC directly so it can be
/// unit-tested without the UI layer. The cancellation-on-keystroke +
/// snapshot-on-flight bookkeeping is the part that benefits most from
/// isolation — concurrent typing races used to be a recurring footgun
/// when this logic lived inline in the view-model.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1001",
    Justification = "Each filter pass's source is cancelled and disposed before the next; the controller lives as long as the main window's list.")]
public sealed class SearchController : INotifyPropertyChanged {
    private string _query = "";
    private NameFilter _name = NameFilter.Empty;
    private TypeFilter _type = TypeFilter.None;
    private ShotFilter _shot = ShotFilter.None;
    private RatingFilter _rating = RatingFilter.None;
    private IReadOnlyList<FileSystemEntry> _source = Array.Empty<FileSystemEntry>();
    private CancellationTokenSource? _cts;

    // The shooting parameters the last projection applied - null when it
    // applied none, the folder's photos saying nothing (ShotFilter).
    private ShotQuery? _shotApplied;

    // The user rearranged the list - a filter changed, the rows came in
    // another order - and no projection has been published since.
    private bool _rearranged;


    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fires after a filter pass completes with a new projection. The owner
    /// pushes <c>FilteredEntries</c> into the bound collection from this
    /// handler. Cancelled passes do NOT fire — only the latest survivor does.
    /// The flag is true for the first projection since the user rearranged
    /// the list - changed a filter, or handed the rows in another order
    /// (<see cref="SetSource"/>): the row the eye was on is to be followed.
    /// </summary>
    public event Action<IReadOnlyList<FileSystemEntry>, bool>? FilteredChanged;

    /// <summary>
    /// Fires when <see cref="Replace"/> changed rows that were on screen
    /// and stay on screen — carrying only those rows, not a new projection.
    ///
    /// <para>
    /// The whole point is what it does <b>not</b> do. Rebuilding the
    /// projection means every row is a new object, which means the list
    /// drops its selection and rebuilds containers under the cursor. For a
    /// change that touched two files out of five hundred — a rating written
    /// into a sidecar — that is a folder that jumps for no reason the user
    /// can see.
    /// </para>
    /// </summary>
    public event Action<IReadOnlyList<FileSystemEntry>>? ItemsChanged;


    /// <summary>
    /// Current filter text. Empty string disables the filter and pushes the
    /// raw source through. Setter is fire-and-forget: it schedules an async
    /// pass and returns immediately; the previous pass is cancelled.
    /// </summary>
    public string Query {
        get => _query;
        set => Filter(value, _type);
    }

    /// <summary>The kind of file the rows have to be - the box's <c>/тип</c>. Set with <see cref="Filter"/>.</summary>
    public TypeFilter Type => _type;

    /// <summary>True when the name, the type or shooting parameters narrow the list.</summary>
    public bool HasQuery => _query.Length > 0 || !_type.IsEmpty || !_shot.Query.IsEmpty;

    /// <summary>
    /// What a photo says about how it was shot, for the box's parameters
    /// (<see cref="ShotFilter"/>): asked on the pool, null for a file that
    /// says nothing - a folder, a document, a screenshot. Null when the
    /// host reads no photos: the parameters are then the name they look like.
    /// </summary>
    public Func<FileSystemEntry, ShotFacts?>? ShotFacts { get; set; }

    /// <summary>
    /// The list is narrowed by shooting parameters - the box had some, and
    /// a photo in the folder says how it was shot. What the camera by the
    /// box is lit by (decision 2026-10-15).
    /// </summary>
    public bool IsShotApplied => _shotApplied is not null;

    /// <summary>
    /// Stars and colour label the rows have to carry. Lives here rather
    /// than beside the name filter because there is only one projection of
    /// the folder onto the screen, and two independent filters racing to
    /// produce it is exactly the bug this class was extracted to avoid.
    /// Same fire-and-forget setter as <see cref="Query"/>.
    /// </summary>
    public RatingFilter RatingFilter {
        get => _rating;
        set {
            value ??= RatingFilter.None;
            if (_rating == value) {
                return;
            }
            _rating = value;
            _rearranged = true;
            Raise();
            Raise(nameof(HasRatingFilter));
            _ = ApplyAsync();
        }
    }

    public bool HasRatingFilter => _rating.IsActive;

    /// <summary>Most recent unfiltered source. Tests assert on it; UI doesn't bind here.</summary>
    public IReadOnlyList<FileSystemEntry> Source => _source;


    /// <summary>
    /// Replace the source list and re-run the current filter against it.
    /// Called by the host after every folder Refresh — the result is pushed
    /// through <see cref="FilteredChanged"/> on the calling thread once the
    /// async filter completes.
    /// </summary>
    /// <param name="source">The folder's rows, before the filters.</param>
    /// <param name="rearranged">The rows come in an order the user has just chosen.</param>
    public void SetSource(IReadOnlyList<FileSystemEntry> source, bool rearranged = false) {
        _source = source ?? Array.Empty<FileSystemEntry>();
        _rearranged |= rearranged;
        _ = ApplyAsync();
    }

    /// <summary>
    /// The name and the type together, in one pass. Set one after the
    /// other, the first would publish a projection the second replaces at
    /// once: going from "x" to "/images", the whole folder flashed up
    /// between the two. Same fire-and-forget as <see cref="Query"/>.
    /// </summary>
    /// <param name="query">The mask, the shooting parameters taken out of it.</param>
    /// <param name="type">The type, the same.</param>
    /// <param name="shot">The shooting parameters, and what the box says without them; none by default.</param>
    public void Filter(string? query, TypeFilter? type, ShotFilter? shot = null) {
        query ??= "";
        type ??= TypeFilter.None;
        shot ??= ShotFilter.None;
        bool queryChanged = _query != query;
        bool typeChanged = _type != type;
        bool shotChanged = !_shot.SameAs(shot);
        if (!queryChanged && !typeChanged && !shotChanged) {
            return;
        }

        _shot = shot;

        if (queryChanged) {
            _query = query;
            // Parsed here rather than in the loop: the mask is re-read on
            // every keystroke and applied to every row of the folder.
            _name = NameFilter.Parse(query);
            Raise(nameof(Query));
        }
        if (typeChanged) {
            _type = type;
            Raise(nameof(Type));
        }
        _rearranged = true;
        Raise(nameof(HasQuery));
        _ = ApplyAsync();
    }

    /// <summary>
    /// Silently clear the query without firing a refilter — used when the
    /// folder changes (the next <see cref="SetSource"/> immediately reapplies
    /// the empty filter, and we'd otherwise race against a stale pass).
    /// </summary>
    /// <param name="rating">The rating filter the next folder starts with - the one it was left with, when that is remembered; none otherwise.</param>
    public void Reset(RatingFilter? rating = null) {
        _cts?.Cancel();
        _rearranged = false;
        if (HasQuery) {
            _query = "";
            _name = NameFilter.Empty;
            _type = TypeFilter.None;
            _shot = ShotFilter.None;
            Raise(nameof(Query));
            Raise(nameof(Type));
            Raise(nameof(HasQuery));
        }
        SetShotApplied(null);
        rating ??= RatingFilter.None;
        if (_rating != rating) {
            _rating = rating;
            Raise(nameof(RatingFilter));
            Raise(nameof(HasRatingFilter));
        }
    }


    /// <summary>
    /// Swaps updated versions of rows into the source, matching by path.
    ///
    /// <para>
    /// Takes the cheap route when it can: if none of the replacements
    /// changes whether the row passes the current filter, only
    /// <see cref="ItemsChanged"/> fires and the list is left alone apart
    /// from those rows. If a row's verdict flips — a photo just rated two
    /// stars while the filter asks for four — the projection genuinely has
    /// to be rebuilt, and the full pass runs.
    /// </para>
    /// </summary>
    public void Replace(IReadOnlyList<FileSystemEntry> updated) {
        if (updated.Count == 0) {
            return;
        }

        var source = new List<FileSystemEntry>(_source);
        var landed = new List<FileSystemEntry>(updated.Count);
        bool membershipChanged = false;

        foreach (var entry in updated) {
            int at = source.FindIndex(
                e => string.Equals(e.FullPath, entry.FullPath, StringComparison.OrdinalIgnoreCase));
            if (at < 0) {
                continue;
            }

            // A replacement that says exactly what the row already says is
            // not a change. Dropping it here is what stops our own write
            // coming back round through the folder watcher and rebuilding
            // the row a second time for nothing.
            if (source[at].SaysTheSameAs(entry)) {
                continue;
            }

            membershipChanged |= Passes(source[at]) != Passes(entry);
            source[at] = entry;
            landed.Add(entry);
        }

        if (landed.Count == 0) {
            return;
        }

        _source = source;
        if (membershipChanged) {
            _ = ApplyAsync();

            return;
        }

        ItemsChanged?.Invoke(landed);
    }


    /// <summary>Whether one row survives the current filters. Cheap enough to run inline: the facts are read by the pass that applied them.</summary>
    private bool Passes(FileSystemEntry entry) {
        // Parameters the folder's photos do not answer to are the name they look like.
        bool plain = _shotApplied is null && !_shot.Query.IsEmpty;
        var name = plain ? NameFilter.Parse(_shot.PlainName) : _name;
        var type = plain ? _shot.PlainType : _type;
        if (!name.Matches(entry.Name) || !type.Matches(entry)) {
            return false;
        }
        if (_shotApplied is { } shot && (entry.IsFolderLike || !shot.Matches(ShotFacts?.Invoke(entry)))) {
            return false;
        }

        return entry.IsFolderLike || _rating.Matches(entry);
    }


    private async Task ApplyAsync() {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Snapshot the inputs so a Refresh / new keystroke mid-flight cannot
        // race us — we either complete with these inputs or get cancelled.
        var name = _name;
        var type = _type;
        var shot = _shot;
        var facts = ShotFacts;
        var rating = _rating;
        var source = _source;

        if (name.IsEmpty && type.IsEmpty && shot.Query.IsEmpty && !rating.IsActive) {
            SetShotApplied(null);
            Publish(source);
            return;
        }

        List<FileSystemEntry> filtered;
        bool applied;
        try {
            (filtered, applied) = await Task.Run(() => {
                // The parameters apply where a photo says how it was shot -
                // the first one found settles it; in a folder where none
                // does, they are the name they look like (decision
                // 2026-10-15). Facts are read here, on the pool, once a
                // file (ShotFactsCache).
                bool applies = !shot.Query.IsEmpty && facts is not null && source.Any(e => {
                    token.ThrowIfCancellationRequested();

                    return !e.IsFolderLike && facts(e) is not null;
                });
                var useName = applies || shot.Query.IsEmpty ? name : NameFilter.Parse(shot.PlainName);
                var useType = applies || shot.Query.IsEmpty ? type : shot.PlainType;

                var result = new List<FileSystemEntry>();
                foreach (var e in source) {
                    token.ThrowIfCancellationRequested();
                    if (!useName.Matches(e.Name) || !useType.Matches(e)) {
                        continue;
                    }
                    // A file that does not say how it was shot is not one
                    // shot so; a folder says nothing.
                    if (applies && (e.IsFolderLike || !shot.Query.Matches(facts!(e)))) {
                        continue;
                    }
                    // Folders are never filtered out by a rating: a folder
                    // has no stars, and hiding the way back out of a folder
                    // of three-star photos is not what "show me three stars"
                    // asked for.
                    if (!e.IsFolderLike && !rating.Matches(e)) {
                        continue;
                    }
                    result.Add(e);
                }
                return (result, applies);
            }, token);
        } catch (OperationCanceledException) {
            return;
        }

        if (token.IsCancellationRequested) {
            return;
        }
        SetShotApplied(applied ? shot.Query : null);
        Publish(filtered);
    }

    private void SetShotApplied(ShotQuery? applied) {
        bool was = _shotApplied is not null;
        _shotApplied = applied;
        if (was != applied is not null) {
            Raise(nameof(IsShotApplied));
        }
    }

    /// <summary>
    /// A projection out, with whether it is the first since the user
    /// rearranged the list. Only the latest pass publishes, and it was
    /// started after the change, so it is the one that shows it.
    /// </summary>
    private void Publish(IReadOnlyList<FileSystemEntry> rows) {
        bool rearranged = _rearranged;
        _rearranged = false;
        FilteredChanged?.Invoke(rows, rearranged);
    }

    private void Raise([CallerMemberName] string? name = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
