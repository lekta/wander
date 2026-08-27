using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Search;

namespace Wander.App.ViewModels;

/// <summary>
/// The half of search that is not a live filter: walking a subtree, looking
/// inside files, asking the system index.
///
/// <para>
/// It sits beside <see cref="SearchController"/> rather than inside it
/// because the two are different interactions, not two settings of one.
/// The filter narrows a list that is already on screen and does it on
/// every keystroke; a search leaves the folder behind, takes seconds, and
/// only starts when the user says so. Sharing one code path would have
/// meant every letter typed into the box launching a disk walk.
/// </para>
///
/// <para>
/// Results arrive here in batches from a background thread and are
/// marshalled onto the dispatcher once per batch — see
/// <c>ContentSearchService</c> for why a batch rather than a hit.
/// </para>
/// </summary>
public sealed class ContentSearchController : ObservableObject {
    /// <summary>Queries kept for the dropdown. Two dozen is a session's worth.</summary>
    public const int HistoryLimit = 20;

    private readonly ContentSearchService? _service;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _log;

    private CancellationTokenSource? _cts;

    // Which pass is the current one. A pass that finds itself superseded
    // leaves the flags alone: the pass that replaced it owns them now. A
    // pass the user merely *stopped* is still the current one, and does
    // have to put the running flag down — which is the bug this counter
    // exists to keep out.
    private int _generation;

    private bool _searchInContents;
    private SearchScope _scope = SearchScope.CurrentFolder;
    private bool _isRunning;
    private bool _isShowingResults;
    private string _activeQuery = "";
    private string _resultRoot = "";
    private bool _canSearchComputer;
    private bool _indexProbed;


    public ContentSearchController(Dispatcher dispatcher, ContentSearchService? service, ILogger? log = null) {
        _dispatcher = dispatcher;
        _service = service;
        _log = log ?? NullLogger.Instance;
        History.CollectionChanged += (_, _) => Raise(nameof(HasHistory));
    }


    /// <summary>Queries run this session and last, most recent first.</summary>
    public ObservableCollection<string> History { get; } = new();

    /// <summary>
    /// Whether the history section is worth drawing. A heading over an
    /// empty list is chrome that says nothing, and on a first run that is
    /// exactly what it would be.
    /// </summary>
    public bool HasHistory => History.Count > 0;


    /// <summary>Fires on the dispatcher with each batch of results.</summary>
    public event Action<IReadOnlyList<FileSystemEntry>>? BatchArrived;

    /// <summary>Fires on the dispatcher as the walk progresses.</summary>
    public event Action<SearchProgress>? Progressed;

    /// <summary>
    /// Fires on the dispatcher when the pass ends — completed or
    /// cancelled. Null means "cancelled"; a cancelled search still has to
    /// take the running flag down and leave the rows it did find.
    /// </summary>
    public event Action<SearchOutcome?>? Finished;


    /// <summary>False when no search service was registered — a build without the platform layer.</summary>
    public bool IsAvailable => _service is not null;

    /// <summary>
    /// True when the "whole computer" scope has an index behind it.
    ///
    /// <para>
    /// Answered from a cached flag rather than by asking, because asking
    /// means opening a connection to the catalogue and that was measured at
    /// 71 ms on the first call. This property is read during session
    /// restore and again by the panel's bindings — both on the dispatcher —
    /// so the question is put to a background thread the first time and the
    /// answer arrives as a change notification.
    /// </para>
    /// </summary>
    public bool CanSearchComputer {
        get {
            ProbeIndex();

            return _canSearchComputer;
        }
    }


    /// <summary>
    /// Look inside files as well as at their names. The expensive half, so
    /// it is off until asked for.
    /// </summary>
    public bool SearchInContents {
        get => _searchInContents;
        set {
            if (SetField(ref _searchInContents, value)) {
                Raise(nameof(IsDeep));
            }
        }
    }


    /// <summary>How far the next search reaches.</summary>
    public SearchScope Scope {
        get => _scope;
        set {
            if (SetField(ref _scope, value)) {
                Raise(nameof(IsDeep));
                Raise(nameof(IsScopeCurrentFolder));
                Raise(nameof(IsScopeSubfolders));
                Raise(nameof(IsScopeComputer));
            }
        }
    }


    // Three booleans rather than one enum binding: WPF radio buttons bind
    // to bool, and a converter per option costs more than three properties.
    public bool IsScopeCurrentFolder {
        get => _scope == SearchScope.CurrentFolder;
        set {
            if (value) {
                Scope = SearchScope.CurrentFolder;
            }
        }
    }

    public bool IsScopeSubfolders {
        get => _scope == SearchScope.Subfolders;
        set {
            if (value) {
                Scope = SearchScope.Subfolders;
            }
        }
    }

    public bool IsScopeComputer {
        get => _scope == SearchScope.Computer;
        set {
            if (value) {
                Scope = SearchScope.Computer;
            }
        }
    }


    /// <summary>
    /// True when what the box holds is a query to run rather than a filter
    /// to apply live. This is the switch between the two interactions: with
    /// it off, typing narrows the folder on screen letter by letter; with
    /// it on, nothing happens until Enter.
    /// </summary>
    public bool IsDeep => _searchInContents || _scope != SearchScope.CurrentFolder;


    /// <summary>A pass is walking the disk right now.</summary>
    public bool IsRunning {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }


    /// <summary>
    /// The list is showing results rather than a folder. Stays true after
    /// the pass finishes — that is when the user reads them.
    /// </summary>
    public bool IsShowingResults {
        get => _isShowingResults;
        private set => SetField(ref _isShowingResults, value);
    }


    /// <summary>What the results on screen were found with.</summary>
    public string ActiveQuery {
        get => _activeQuery;
        private set => SetField(ref _activeQuery, value);
    }


    /// <summary>Folder the results on screen were searched from.</summary>
    public string ResultRoot {
        get => _resultRoot;
        private set => SetField(ref _resultRoot, value);
    }


    /// <summary>
    /// Starts a pass, replacing any that is still running. Does nothing
    /// without a service, an empty query, or — for the two walking scopes —
    /// a folder to start from.
    /// </summary>
    public void Start(string query, string? root, EntryVisibility visibility) {
        if (_service is null || string.IsNullOrWhiteSpace(query)) {
            return;
        }
        if (_scope != SearchScope.Computer && string.IsNullOrEmpty(root)) {
            return;
        }

        Cancel();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        int generation = ++_generation;

        ActiveQuery = query;
        ResultRoot = root ?? "";
        IsShowingResults = true;
        IsRunning = true;
        Remember(query);

        var request = new SearchRequest(query, root ?? "", _scope, _searchInContents, visibility);
        var progress = new DispatchedProgress(_dispatcher, p => Progressed?.Invoke(p), token);

        _ = RunAsync(request, progress, token, generation);
    }


    /// <summary>
    /// Stops the pass. The rows already found stay on screen: a search
    /// stopped halfway has still answered part of the question, and
    /// throwing that away is the behaviour people cancel *out of*.
    /// </summary>
    public void Cancel() {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }


    /// <summary>
    /// Leaves results mode, so the list goes back to showing a folder.
    /// The caller re-lists; this only puts the flags back.
    /// </summary>
    public void ExitResults() {
        Cancel();
        IsRunning = false;
        IsShowingResults = false;
        ActiveQuery = "";
        ResultRoot = "";
    }


    /// <summary>Restores the query history from <c>state.json</c>.</summary>
    public void LoadHistory(IEnumerable<string> queries) {
        History.Clear();
        foreach (string query in queries) {
            if (string.IsNullOrWhiteSpace(query) || History.Contains(query)) {
                continue;
            }
            History.Add(query);
            if (History.Count == HistoryLimit) {
                break;
            }
        }
    }


    /// <summary>
    /// Asks the index whether it exists, once, off the dispatcher. A saved
    /// scope of "the whole computer" falls back when the answer is no —
    /// the search service would return nothing, and a box that quietly
    /// finds nothing is worse than one that says where it is looking.
    /// </summary>
    private void ProbeIndex() {
        if (_indexProbed || _service is null) {
            return;
        }
        _indexProbed = true;

        _ = Task.Run(() => {
            bool available = _service.CanSearchComputer;
            _dispatcher.BeginInvoke(() => {
                _canSearchComputer = available;
                Raise(nameof(CanSearchComputer));
                if (!available && _scope == SearchScope.Computer) {
                    Scope = SearchScope.CurrentFolder;
                }
            });
        });
    }


    private async Task RunAsync(
        SearchRequest request,
        IProgress<SearchProgress> progress,
        CancellationToken token,
        int generation) {
        SearchOutcome? outcome = null;
        bool cancelled = false;
        try {
            outcome = await _service!.RunAsync(
                request,
                batch => Dispatch(token, () => BatchArrived?.Invoke(Project(batch))),
                progress,
                token);
        } catch (OperationCanceledException) {
            // Either the user stopped this pass or a newer one replaced it.
            cancelled = true;
        } catch (Exception ex) {
            _log.Error($"Search '{request.Query}' failed", ex);
        }

        bool stopped = cancelled || token.IsCancellationRequested;
        var result = outcome;

        _ = _dispatcher.BeginInvoke(() => {
            // A pass that has been superseded reports nothing and touches
            // nothing: the flags, the status line and the rows on screen
            // all belong to the pass that replaced it.
            if (generation != _generation) {
                return;
            }

            IsRunning = false;
            Finished?.Invoke(stopped ? null : result);
        });
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

        _dispatcher.BeginInvoke(() => {
            if (!token.IsCancellationRequested) {
                action();
            }
        });
    }


    /// <summary>
    /// Puts a query at the front of the history, moving rather than
    /// duplicating one that is already there.
    /// </summary>
    private void Remember(string query) {
        int existing = History.IndexOf(query);
        if (existing == 0) {
            return;
        }
        if (existing > 0) {
            History.RemoveAt(existing);
        }

        History.Insert(0, query);
        while (History.Count > HistoryLimit) {
            History.RemoveAt(History.Count - 1);
        }
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

            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => {
                if (!_token.IsCancellationRequested) {
                    _report(value);
                }
            });
        }
    }
}
