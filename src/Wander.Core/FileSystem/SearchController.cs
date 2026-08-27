using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Wander.Core.Companions;

namespace Wander.Core.FileSystem;

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
public sealed class SearchController : INotifyPropertyChanged {
    private string _query = "";
    private RatingFilter _rating = RatingFilter.None;
    private IReadOnlyList<FileSystemEntry> _source = Array.Empty<FileSystemEntry>();
    private CancellationTokenSource? _cts;


    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fires after a filter pass completes with a new projection. The owner
    /// pushes <c>FilteredEntries</c> into the bound collection from this
    /// handler. Cancelled passes do NOT fire — only the latest survivor does.
    /// </summary>
    public event Action<IReadOnlyList<FileSystemEntry>>? FilteredChanged;


    /// <summary>
    /// Current filter text. Empty string disables the filter and pushes the
    /// raw source through. Setter is fire-and-forget: it schedules an async
    /// pass and returns immediately; the previous pass is cancelled.
    /// </summary>
    public string Query {
        get => _query;
        set {
            value ??= "";
            if (_query == value) {
                return;
            }
            _query = value;
            Raise();
            Raise(nameof(HasQuery));
            _ = ApplyAsync();
        }
    }

    public bool HasQuery => _query.Length > 0;

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
    public void SetSource(IReadOnlyList<FileSystemEntry> source) {
        _source = source ?? Array.Empty<FileSystemEntry>();
        _ = ApplyAsync();
    }

    /// <summary>
    /// Silently clear the query without firing a refilter — used when the
    /// folder changes (the next <see cref="SetSource"/> immediately reapplies
    /// the empty filter, and we'd otherwise race against a stale pass).
    /// </summary>
    public void Reset() {
        _cts?.Cancel();
        if (_query.Length > 0) {
            _query = "";
            Raise(nameof(Query));
            Raise(nameof(HasQuery));
        }
        if (_rating.IsActive) {
            _rating = RatingFilter.None;
            Raise(nameof(RatingFilter));
            Raise(nameof(HasRatingFilter));
        }
    }


    private async Task ApplyAsync() {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Snapshot the inputs so a Refresh / new keystroke mid-flight cannot
        // race us — we either complete with these inputs or get cancelled.
        string query = _query;
        var rating = _rating;
        var source = _source;

        if (string.IsNullOrEmpty(query) && !rating.IsActive) {
            FilteredChanged?.Invoke(source);
            return;
        }

        List<FileSystemEntry> filtered;
        try {
            filtered = await Task.Run(() => {
                var result = new List<FileSystemEntry>();
                foreach (var e in source) {
                    token.ThrowIfCancellationRequested();
                    if (query.Length > 0 && !e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) {
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
                return result;
            }, token);
        } catch (OperationCanceledException) {
            return;
        }

        if (token.IsCancellationRequested) {
            return;
        }
        FilteredChanged?.Invoke(filtered);
    }

    private void Raise([CallerMemberName] string? name = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
