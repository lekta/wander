using System.IO;
using System.Windows.Threading;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Search;

namespace Wander.App.Controllers;

/// <summary>
/// The rows a deep search puts on the list, and the bookkeeping that keeps
/// them there: what has been found, what has already been counted, and when
/// to hand the collected batch over.
///
/// <para>
/// Results are collected rather than pushed straight into the list. A bulk
/// refill raises one Reset, and a search over a deep tree produces hundreds
/// of batches — one Reset each would make the list re-lay-out hundreds of
/// times over a collection that keeps growing. They go out on a timer
/// instead, and the view model is the only thing that touches the bound
/// collection.
/// </para>
///
/// <para>
/// Deliberately not fed through the name filter: a folder listing is
/// narrowed by what is typed in the box, and results <em>are</em> what was
/// typed in the box. Filtering them again would answer the same question
/// twice, so they reach the list by their own door.
/// </para>
/// </summary>
public sealed class SearchResultsController {
    /// <summary>
    /// How often a running search may repaint the list. Refilling the bound
    /// collection raises one Reset and one full re-layout, and a search over
    /// a deep tree finds something in hundreds of folders — pushing each
    /// batch as it lands would spend the whole search re-laying-out. A fifth
    /// of a second still looks like results streaming in.
    /// </summary>
    private const int FlushIntervalMs = 200;

    private readonly ContentSearchController _search;
    private readonly IFileSystem _fs;
    private readonly SettingsViewModel _settings;
    private readonly Dispatcher _dispatcher;

    private readonly List<FileSystemEntry> _rows = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _flushTimer;
    private bool _dirty;
    private bool _hereFirst;
    private string? _root;


    public SearchResultsController(
        ContentSearchController search, IFileSystem fs, SettingsViewModel settings, Dispatcher dispatcher) {
        _search = search;
        _fs = fs;
        _settings = settings;
        _dispatcher = dispatcher;
    }


    /// <summary>The rows to show. The one way results reach the list.</summary>
    public event EventHandler<IReadOnlyList<FileSystemEntry>>? RowsChanged;

    /// <summary>Something to tell the user — already localised.</summary>
    public event EventHandler<string>? StatusReported;


    /// <summary>How many rows have been found so far.</summary>
    public int Count => _rows.Count;


    /// <summary>
    /// A pass is starting.
    ///
    /// <para>
    /// The quick filter's continuation keeps what is already on screen as
    /// its seed: those rows are the answer for this folder, already found
    /// and already read by the user, and emptying the list to go looking for
    /// more of the same would blink them away and put them back a moment
    /// later. Any other pass starts from nothing.
    /// </para>
    /// </summary>
    /// <param name="root">The folder the search started from.</param>
    /// <param name="onScreen">The rows currently on the list.</param>
    public void Begin(string? root, IReadOnlyList<FileSystemEntry> onScreen) {
        _rows.Clear();
        _seen.Clear();
        _hereFirst = _search.IsFilterPass;
        _root = root;

        if (_hereFirst) {
            _rows.AddRange(onScreen);
            foreach (var entry in _rows) {
                _seen.Add(entry.FullPath);
            }
        } else {
            RowsChanged?.Invoke(this, Array.Empty<FileSystemEntry>());
        }

        StatusReported?.Invoke(this, string.Format(Strings.StatusSearching, 0, 0));
        StartFlushTimer();
    }


    /// <summary>A folder's worth of results, from the dispatcher.</summary>
    public void Append(IReadOnlyList<FileSystemEntry> batch) {
        if (!_search.IsShowingResults) {
            return;
        }

        foreach (var entry in batch) {
            // The quick filter's pass walks the current folder too, and its
            // matches are already on the list as the seed. Dropping the
            // repeats here rather than narrowing the walk keeps the search
            // service one thing that answers one question.
            if (!_seen.Add(entry.FullPath)) {
                continue;
            }

            _rows.Add(entry);
            _dirty = true;
        }
    }


    /// <summary>
    /// How far along the pass is. The search window has no status strip of
    /// its own, so this is the only place the counts appear while the walk
    /// is running — the spinner in the file area says "still going", and
    /// this says how far.
    /// </summary>
    public void ReportProgress(SearchProgress progress) {
        StatusReported?.Invoke(
            this, string.Format(Strings.StatusSearching, progress.Found, progress.FilesScanned));
    }


    /// <summary>
    /// End of the pass. A null outcome means the user stopped it — the rows
    /// already found stay, because a search stopped halfway has still
    /// answered part of the question.
    /// </summary>
    public void Finish(SearchOutcome? outcome) {
        StopFlushTimer();
        Flush();

        if (outcome is not { } result) {
            StatusReported?.Invoke(this, string.Format(Strings.StatusSearchStopped, _rows.Count));

            return;
        }

        // The walk's own count leaves out the seed — the folder's matches
        // were found by the live filter, not by it — so what is on the list
        // is the honest answer to "how many".
        int found = _hereFirst ? _rows.Count : result.Found;

        string what = Describe();
        string text = found == 0
            ? string.Format(Strings.StatusSearchNothing, what)
            : string.Format(Strings.StatusSearchFound, found, what, result.FilesScanned);

        if (result.Truncated) {
            text += string.Format(Strings.StatusSearchTruncated, result.Found);
        }
        // Worth saying out loud: a folder of PDFs on a machine with no PDF
        // filter installed otherwise reads as "nothing in these documents"
        // when the truth is that none of them could be opened.
        if (result.UnreadableFiles > 0) {
            text += string.Format(Strings.StatusSearchUnreadable, result.UnreadableFiles);
        }

        StatusReported?.Invoke(this, text);
    }


    /// <summary>
    /// Re-orders what is already found. The sort changed, and results cannot
    /// simply be asked for again: their order comes from the pass that found
    /// them, not from an enumerator that can be re-run.
    /// </summary>
    public void Resort() {
        _dirty = true;
        Flush();
    }


    /// <summary>Forgets everything found. The list is put back by the caller.</summary>
    public void Clear() {
        StopFlushTimer();
        _rows.Clear();
        _seen.Clear();
        _hereFirst = false;
        _root = null;
    }


    /// <summary>
    /// Drops result rows whose files no longer exist. One stat per row, and
    /// only on the paths that asked for a refresh in the first place — a
    /// completed file operation, the folder watcher, a visibility setting.
    /// Rows that are still there keep their place: the pass that found them
    /// is not re-run, because "delete one file" is not a reason to walk the
    /// disk again.
    /// </summary>
    public void PruneMissing() {
        int removed = _rows.RemoveAll(entry => {
            if (_fs.FileExists(entry.FullPath) || _fs.DirectoryExists(entry.FullPath)) {
                return false;
            }
            _seen.Remove(entry.FullPath);

            return true;
        });
        if (removed == 0) {
            return;
        }

        _dirty = true;
        Flush();
    }


    /// <summary>
    /// Hands what has been found to the list, keeping the current sort.
    /// Sorted rather than left in discovery order because the user's chosen
    /// sort is a setting, not a property of one folder — and because arrival
    /// order changes between two identical searches.
    /// </summary>
    private void Flush() {
        if (!_dirty) {
            return;
        }
        _dirty = false;

        var sort = new SortOptions(_settings.SortKey, _settings.SortAscending, _settings.GroupFoldersFirst);
        var sorted = EntryComparers.Sort(_rows, sort);

        RowsChanged?.Invoke(this, _hereFirst ? HereFirst(sorted) : sorted);
    }


    /// <summary>
    /// Splits a sorted result list into "in the folder on screen" and
    /// "somewhere under it", in that order and keeping the sort inside each
    /// half.
    ///
    /// <para>
    /// Only for the quick filter's pass, and it is the whole shape of that
    /// interaction: the user asked about the folder they are standing in
    /// and got an answer, and the subtree is the extra. Letting the sort
    /// interleave the two would scatter that answer through rows from
    /// folders the user never opened.
    /// </para>
    /// </summary>
    private IReadOnlyList<FileSystemEntry> HereFirst(IReadOnlyList<FileSystemEntry> sorted) {
        var here = new List<FileSystemEntry>(sorted.Count);
        var below = new List<FileSystemEntry>();

        foreach (var entry in sorted) {
            string? folder = Path.GetDirectoryName(
                entry.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (folder is not null && _root is not null
                && string.Equals(folder, _root, StringComparison.OrdinalIgnoreCase)) {
                here.Add(entry);
            } else {
                below.Add(entry);
            }
        }

        here.AddRange(below);

        return here;
    }


    /// <summary>
    /// The search as one phrase for the status bar. Both halves when both
    /// were given, because "найдено 3 по запросу «отчёт»" is a different
    /// claim from "3 файла *.docx со словом «отчёт»".
    /// </summary>
    private string Describe() {
        string name = _search.NameQuery;
        string text = _search.TextQuery;

        if (name.Length > 0 && text.Length > 0) {
            return string.Format(Strings.SearchDescriptionBoth, name, text);
        }

        return text.Length > 0 ? string.Format(Strings.SearchDescriptionText, text) : name;
    }


    private void StartFlushTimer() {
        if (_flushTimer is null) {
            _flushTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) {
                Interval = TimeSpan.FromMilliseconds(FlushIntervalMs),
            };
            _flushTimer.Tick += (_, _) => Flush();
        }

        _flushTimer.Start();
    }


    private void StopFlushTimer() {
        _flushTimer?.Stop();
    }
}
