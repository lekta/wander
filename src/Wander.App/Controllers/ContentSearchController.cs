using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Search;
using Wander.Core.Shell;


namespace Wander.App.Controllers;

/// <summary>Where a search is in its life, for the indicator in the search window.</summary>
public enum SearchState {
    /// <summary>Nothing asked for. Both fields empty, or the criteria are the shallow kind.</summary>
    Idle,

    /// <summary>Something typed, nothing running: waiting out the pause, or waiting for a longer query.</summary>
    Pending,

    /// <summary>Walking the disk right now.</summary>
    Running,

    /// <summary>Finished on its own.</summary>
    Done,

    /// <summary>Stopped by the user. What was found stays on screen.</summary>
    Stopped,
}


/// <summary>
/// The half of search that is not a live filter: walking a subtree, looking
/// inside files, asking the system index.
///
/// <para>
/// It sits beside <see cref="Wander.Core.Listing.SearchController"/> rather than inside it
/// because the two are different interactions, not two settings of one.
/// The filter narrows a list that is already on screen and does it on
/// every keystroke; a search leaves the folder behind and takes seconds.
/// Sharing one code path would mean every letter typed launching a disk
/// walk — which is also why this one runs on a timer rather than on
/// change.
/// </para>
///
/// <para>
/// The two do meet, though, and this is where: a mask typed in the
/// toolbar narrows the folder live <em>and</em>, once the typing settles,
/// starts a pass under that folder — see <see cref="IsFilterPass"/>. The
/// answer for the folder you are standing in is instant, the rest arrives
/// underneath it. That is the one case where a keystroke does end in a
/// disk walk, and the pause plus <see cref="MinAutoRunLength"/> are what
/// keep it to one walk per word rather than one per letter.
/// </para>
///
/// <para>
/// Two criteria, combined with <em>and</em>: a mask on the name and text
/// inside the file. Either may be empty. That is what makes "every
/// <c>*.cs</c> that mentions X" expressible, and it is why there is no
/// "search in contents" checkbox — the text field is the switch, and a
/// switch you can see the state of is one that cannot surprise you later.
/// </para>
/// </summary>
public sealed class ContentSearchController : ObservableObject {
    /// <summary>
    /// Quiet time after the last keystroke before a heavy search starts.
    /// Long enough that typing a word is one search rather than six, short
    /// enough to feel like the list is answering rather than waiting.
    /// </summary>
    private const int DebounceMs = 400;

    /// <summary>
    /// Shortest mask the quick filter will walk subfolders for on its own.
    /// One character matches most of a disk, and the walk it starts is
    /// wasted before it begins. Enter and the search button ignore this —
    /// an explicit ask is an ask.
    /// </summary>
    public const int MinAutoRunLength = 2;

    private readonly ContentSearchService? _service;
    private readonly Dispatcher _dispatcher;
    private readonly Func<string?> _root;
    private readonly Func<EntryVisibility> _visibility;
    private readonly ILogger _log;
    private readonly DispatcherTimer _debounce;

    private CancellationTokenSource? _cts;

    // Which pass is the current one. A pass that finds itself superseded
    // leaves the flags alone: the pass that replaced it owns them now. A
    // pass the user merely *stopped* is still the current one and does have
    // to put the running flag down — which is the bug this counter exists
    // to keep out.
    private int _generation;

    // What the box holds, verbatim. Stored rather than derived from the
    // two criteria below: "report:" and "report" mean the same search, so
    // formatting the criteria back would drop the colon the moment it was
    // typed — and the text half could never be started.
    private string _filterText = "";
    private string _nameQuery = "";
    private string _textQuery = "";
    private SearchScope _scope = SearchScope.CurrentFolder;
    private bool _searchBinaries;
    private SearchState _state = SearchState.Idle;
    private bool _isShowingResults;
    private bool _isFilterPass;

    // Where the criteria last came from. Only the toolbar box reaches past
    // the folder on its own; the search window has a checkbox for that, and
    // a search that walked subfolders with the box unticked would be lying
    // about itself.
    private bool _fromFilterBox;


    public ContentSearchController(
        Dispatcher dispatcher,
        ContentSearchService? service,
        Func<string?> root,
        Func<EntryVisibility> visibility,
        ILogger? log = null) {
        _dispatcher = dispatcher;
        _service = service;
        _root = root;
        _visibility = visibility;
        _log = log ?? NullLogger.Instance;

        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher) {
            Interval = TimeSpan.FromMilliseconds(DebounceMs),
        };
        _debounce.Tick += (_, _) => {
            _debounce.Stop();
            RunNow();
        };

    }


    /// <summary>Fires on the dispatcher when a pass begins, so the list can be emptied.</summary>
    public event Action? Started;

    /// <summary>Fires on the dispatcher with each batch of results.</summary>
    public event Action<IReadOnlyList<FileSystemEntry>>? BatchArrived;

    /// <summary>Fires on the dispatcher as the walk progresses, for the status bar.</summary>
    public event Action<SearchProgress>? Progressed;

    /// <summary>Fires on the dispatcher when the pass ends — completed, stopped or failed.</summary>
    public event Action<SearchOutcome?>? Finished;

    /// <summary>
    /// Fires when the shallow criteria changed and the live name filter has
    /// to be re-pointed. The owner decides what to do with it, because the
    /// live filter belongs to the folder listing, not to this.
    /// </summary>
    public event Action? ShallowChanged;


    /// <summary>False when no search service was registered — a build without the platform layer.</summary>
    public bool IsAvailable => _service is not null;


    /// <summary>
    /// Both criteria as the single line the toolbar box shows and accepts
    /// — see <see cref="SearchExpression"/>. This is the one place the two
    /// halves are visible outside the search window, and it is why a
    /// narrowed list can no longer sit there unexplained.
    /// </summary>
    public string FilterText {
        get => _filterText;
        set {
            value ??= "";
            if (_filterText == value) {
                return;
            }

            _filterText = value;
            Raise();

            var (name, text) = SearchExpression.Parse(value);
            if (_nameQuery == name && _textQuery == text) {
                // A trailing colon and no colon are the same criteria. The
                // box keeps the character so the text half can be typed
                // after it; nothing else needs to hear about it.
                return;
            }

            _nameQuery = name;
            _textQuery = text;
            Raise(nameof(NameQuery));
            Raise(nameof(TextQuery));
            Raise(nameof(IsDeep));
            Raise(nameof(BinariesApplicable));
            OnCriteriaChanged(immediate: false, fromFilterBox: true);
        }
    }


    /// <summary>Mask on the file name. Substring, or wildcards — see <see cref="NameFilter"/>.</summary>
    public string NameQuery {
        get => _nameQuery;
        set {
            value ??= "";
            if (SetField(ref _nameQuery, value)) {
                SyncFilterText();
                OnCriteriaChanged(immediate: false);
            }
        }
    }


    /// <summary>
    /// Text to find inside files. Non-empty is what turns the box from a
    /// filter into a search — there is no separate checkbox for it.
    /// </summary>
    public string TextQuery {
        get => _textQuery;
        set {
            value ??= "";
            if (SetField(ref _textQuery, value)) {
                SyncFilterText();
                Raise(nameof(IsDeep));
                Raise(nameof(BinariesApplicable));
                OnCriteriaChanged(immediate: false);
            }
        }
    }


    /// <summary>How far the search reaches.</summary>
    public SearchScope Scope {
        get => _scope;
        set {
            if (SetField(ref _scope, value)) {
                Raise(nameof(IsDeep));
                Raise(nameof(SearchSubfolders));
                Raise(nameof(HasNonDefaultOptions));
                // A switch is a decision, not a keystroke: it runs at once.
                OnCriteriaChanged(immediate: true);
            }
        }
    }


    /// <summary>
    /// Also scan files that are not text, byte for byte. Only means
    /// anything alongside <see cref="TextQuery"/>.
    /// </summary>
    public bool SearchBinaries {
        get => _searchBinaries;
        set {
            if (SetField(ref _searchBinaries, value)) {
                Raise(nameof(HasNonDefaultOptions));
                OnCriteriaChanged(immediate: true);
            }
        }
    }


    /// <summary>
    /// Whether the binaries switch does anything right now. False without
    /// text to look for, and false for a query with characters outside
    /// ASCII — see <see cref="BinaryTextSearch"/> for why that is a hard
    /// limit rather than a guess.
    /// </summary>
    public bool BinariesApplicable => BinaryTextSearch.Supports(_textQuery);


    /// <summary>
    /// Walk into subfolders. A checkbox rather than one of three radio
    /// buttons because that is what it is — a modifier on "here", the same
    /// shape every other search dialog gives it.
    /// </summary>
    public bool SearchSubfolders {
        get => _scope == SearchScope.Subfolders;
        set => Scope = value ? SearchScope.Subfolders : SearchScope.CurrentFolder;
    }


    /// <summary>
    /// True when something other than the two text fields is in force.
    /// The toolbar's search button marks itself when it is, so a list
    /// narrowed by a switch set in a window that is now closed still says
    /// as much.
    /// </summary>
    public bool HasNonDefaultOptions => _scope != SearchScope.CurrentFolder || _searchBinaries;


    /// <summary>The folder the next search would start from, for the window to show.</summary>
    public string? Root => _root();


    /// <summary>
    /// True when answering needs a disk walk rather than a re-filter of
    /// what is already on screen. This is the line between the two
    /// interactions: below it the owner's live filter handles everything,
    /// above it this class does.
    /// </summary>
    public bool IsDeep => _textQuery.Length > 0 || _scope != SearchScope.CurrentFolder;


    /// <summary>Where the search is in its life. Drives the indicator.</summary>
    public SearchState State {
        get => _state;
        private set {
            if (SetField(ref _state, value)) {
                Raise(nameof(IsRunning));
            }
        }
    }

    public bool IsRunning => _state == SearchState.Running;


    /// <summary>
    /// The list is showing results rather than a folder. Stays true after
    /// the pass finishes — that is when the user reads them.
    /// </summary>
    public bool IsShowingResults {
        get => _isShowingResults;
        private set => SetField(ref _isShowingResults, value);
    }


    /// <summary>
    /// The pass that owns the list right now is the quick filter reaching
    /// past the folder on screen, rather than a search set up in the search
    /// window.
    ///
    /// <para>
    /// The owner shows the two differently. A search replaces the list; this
    /// one continues it — the rows the live filter already found stay where
    /// they are and the deeper ones land under them, because blanking the
    /// answer the user is reading in order to go looking for more of it is
    /// not an improvement.
    /// </para>
    /// </summary>
    public bool IsFilterPass {
        get => _isFilterPass;
        private set => SetField(ref _isFilterPass, value);
    }


    /// <summary>
    /// Tells the window that the current folder moved, so the "Folder"
    /// row stops describing where the user used to be.
    /// </summary>
    public void NoteRootChanged() {
        Raise(nameof(Root));
    }


    /// <summary>Runs the search now, ignoring both the pause and the length floor. This is Enter.</summary>
    public void RunNow() {
        _debounce.Stop();

        if (_service is null) {
            return;
        }

        var name = NameFilter.Parse(_nameQuery);
        if (name.IsEmpty && _textQuery.Length == 0) {
            return;
        }

        string? root = _root();
        if (string.IsNullOrEmpty(root)) {
            return;
        }

        // Inside an archive there is nothing to walk: the search service
        // reads the filesystem, and these paths are not on it. The live
        // name filter still works — it filters the rows already listed,
        // which is the whole archive level anyway.
        if (Archives.Contains(root)) {
            return;
        }

        // Shallow criteria mean the live filter has already answered for
        // the folder on screen. What the toolbar box starts here is the
        // continuation of that answer: the same mask, under the same
        // folder. The window's own shallow search has nothing to add — the
        // filter already showed it — so it does not run at all.
        bool filterPass = !IsDeep && _fromFilterBox;
        if (!IsDeep && !filterPass) {
            return;
        }

        var scope = filterPass ? SearchScope.Subfolders : _scope;

        Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int generation = ++_generation;

        // Before Started: the owner reads it to decide whether to empty the
        // list or to keep what is in it.
        IsFilterPass = filterPass;
        IsShowingResults = true;
        State = SearchState.Running;

        var request = new SearchRequest(
            name, _textQuery, root, scope, _searchBinaries, _visibility());

        Started?.Invoke();
        _ = RunAsync(request, token, generation);
    }


    /// <summary>
    /// Abandons whatever is running or pending, without saying anything
    /// about it. Bumping the generation is the point: the in-flight pass
    /// finds itself superseded and reports nothing, so whoever cancelled it
    /// owns the state from here.
    ///
    /// <para>
    /// Without that, a pass cancelled because the criteria moved on still
    /// announced "stopped" a moment later and overwrote the state the new
    /// criteria had just set — the window said "Остановлено" about a search
    /// nobody had stopped.
    /// </para>
    /// </summary>
    public void Cancel() {
        _debounce.Stop();
        _generation++;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }


    /// <summary>
    /// The Stop button: the same abandonment, said out loud. The rows
    /// already found stay on screen — a search stopped halfway has still
    /// answered part of the question, and throwing that away is the
    /// behaviour people stop *out of*.
    /// </summary>
    public void Stop() {
        if (_state != SearchState.Running) {
            return;
        }

        Cancel();
        State = SearchState.Stopped;
        Finished?.Invoke(null);
    }


    /// <summary>
    /// Leaves results mode, so the list goes back to showing a folder. The
    /// caller re-lists; this only puts the flags back.
    /// </summary>
    public void ExitResults() {
        Cancel();
        IsShowingResults = false;
        IsFilterPass = false;
        State = SearchState.Idle;
    }


    /// <summary>
    /// Everything a search is, back to nothing — text, area, the binaries
    /// switch. Called when the user walks into another folder.
    ///
    /// <para>
    /// The flags go too, and that is the point. A ticked "search
    /// subfolders" used to survive both navigation and the window being
    /// closed, and while it was on, every keystroke in the box was a disk
    /// walk rather than a filter — so the box quietly stopped narrowing the
    /// folder and nothing on screen explained why. A search belongs to the
    /// folder it was set up in.
    /// </para>
    /// </summary>
    public void Reset() {
        _searchBinaries = false;
        Raise(nameof(SearchBinaries));

        // Through the property, so the scope change raises what the window
        // binds to; its own OnCriteriaChanged is harmless with both fields
        // already empty.
        Clear();
        Scope = SearchScope.CurrentFolder;
        Raise(nameof(HasNonDefaultOptions));
    }


    /// <summary>Empties both fields and leaves results. What the clear button and Esc do.</summary>
    public void Clear() {
        _nameQuery = "";
        _textQuery = "";
        _filterText = "";
        Raise(nameof(NameQuery));
        Raise(nameof(TextQuery));
        Raise(nameof(FilterText));
        Raise(nameof(IsDeep));
        Raise(nameof(BinariesApplicable));
        ExitResults();
        ShallowChanged?.Invoke();
    }


    /// <summary>Re-runs whatever is set up now. F5 on a result list.</summary>
    public void Rerun() {
        if (IsShowingResults || IsDeep) {
            RunNow();
        }
    }


    /// <summary>
    /// Rewrites the box after a field in the window changed. Only from
    /// that direction: typing in the box is the box's own business, and
    /// re-formatting it there is what ate the colon.
    /// </summary>
    private void SyncFilterText() {
        _filterText = SearchExpression.Format(_nameQuery, _textQuery);
        Raise(nameof(FilterText));
    }


    /// <summary>
    /// A criterion moved. Decides between "start the clock", "start now"
    /// and "this is not our business at all".
    /// </summary>
    private void OnCriteriaChanged(bool immediate, bool fromFilterBox = false) {
        _fromFilterBox = fromFilterBox;

        // A walk whose answer nobody wants any more is wasted disk, and a
        // pass left running would land on top of the new criteria's state.
        Cancel();

        // Shallow criteria have a cheap answer to fall back on: the live
        // filter repaints the folder at once, and this class goes looking
        // under it after the pause. Deep ones do not, so their results stay
        // on screen until the new pass replaces them — otherwise the search
        // window would blink back to the folder on every keystroke.
        if (!IsDeep) {
            if (IsShowingResults) {
                ExitResults();
            } else {
                State = SearchState.Idle;
            }
        }

        ShallowChanged?.Invoke();

        if (NameFilter.Parse(_nameQuery).IsEmpty && _textQuery.Length == 0) {
            if (IsShowingResults) {
                ExitResults();
            }

            return;
        }

        if (immediate) {
            RunNow();

            return;
        }

        if (!IsDeep) {
            // Shallow and not from the box: the live filter is the whole
            // answer, and there is nothing left to walk for.
            if (!fromFilterBox) {
                return;
            }

            // The floor guards the automatic walk only. One character
            // matches most of a disk, and the walk it starts is wasted
            // before it begins.
            if (_nameQuery.Length < MinAutoRunLength) {
                return;
            }
        }

        State = SearchState.Pending;
        _debounce.Start();
    }


    private async Task RunAsync(SearchRequest request, CancellationToken token, int generation) {
        SearchOutcome? outcome = null;
        bool cancelled = false;
        var progress = new DispatchedProgress(_dispatcher, OnProgress, token);

        try {
            outcome = await _service!.RunAsync(
                request,
                batch => Dispatch(token, () => BatchArrived?.Invoke(Project(batch))),
                progress,
                token);
        } catch (OperationCanceledException) {
            cancelled = true;
        } catch (Exception ex) {
            _log.Error($"Search '{request.Name.Text}' / '{request.Text}' failed", ex);
        }

        bool stopped = cancelled || token.IsCancellationRequested;
        var result = outcome;

        _ = _dispatcher.BeginInvoke(() => {
            // A superseded pass reports nothing and touches nothing: the
            // flags, the status line and the rows on screen all belong to
            // the pass that replaced it.
            if (generation != _generation) {
                return;
            }

            State = stopped ? SearchState.Stopped : SearchState.Done;
            Finished?.Invoke(stopped ? null : result);
        });
    }


    private void OnProgress(SearchProgress progress) {
        if (_state == SearchState.Running) {
            Progressed?.Invoke(progress);
        }
    }


    private static IReadOnlyList<FileSystemEntry> Project(IReadOnlyList<SearchHit> batch) {
        var entries = new List<FileSystemEntry>(batch.Count);
        foreach (var hit in batch) {
            entries.Add(hit.Entry);
        }

        return entries;
    }


    private void Dispatch(CancellationToken token, Action action) {
        if (_dispatcher.CheckAccess()) {
            if (!token.IsCancellationRequested) {
                action();
            }

            return;
        }

        _ = _dispatcher.BeginInvoke(() => {
            if (!token.IsCancellationRequested) {
                action();
            }
        });
    }


    /// <summary>
    /// <see cref="Progress{T}"/> captures whatever synchronization context
    /// happened to be current when it was built, which on a thread-pool
    /// thread is none. This one always lands on the window's dispatcher.
    /// </summary>
    private sealed class DispatchedProgress : IProgress<SearchProgress> {
        private readonly Dispatcher _dispatcher;
        private readonly Action<SearchProgress> _report;
        private readonly CancellationToken _token;


        public DispatchedProgress(Dispatcher dispatcher, Action<SearchProgress> report, CancellationToken token) {
            _dispatcher = dispatcher;
            _report = report;
            _token = token;
        }


        public void Report(SearchProgress value) {
            if (_token.IsCancellationRequested) {
                return;
            }

            _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, () => {
                if (!_token.IsCancellationRequested) {
                    _report(value);
                }
            });
        }
    }
}
