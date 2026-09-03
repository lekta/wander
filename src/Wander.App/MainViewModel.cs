using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wander.App.Controllers;
using Wander.App.Controls;
using Wander.App.Dialogs;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Companions;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Logging;
using Wander.Core.Menu;
using Wander.Core.Navigation;
using Wander.Core.Operations;
using Wander.Core.Persistence;
using Wander.Core.Search;
using Wander.Core.Shell;
using Wander.Core.Undo;


namespace Wander.App;

public sealed class MainViewModel : ObservableObject {
    /// <summary>How long a folder may take to list before the spinner shows.</summary>
    private const int SpinnerDelayMs = 150;

    /// <summary>
    /// How slow an arrival has to be before it is worth a line in the log.
    /// A third of a second is where "it opened" turns into "it took a
    /// moment" — below that there is nothing to investigate and a line per
    /// folder would bury what matters.
    /// </summary>
    private const int SlowFolderMs = 300;

    /// <summary>
    /// How often an outside change to the current folder may cost a
    /// re-listing. Half a second: fast enough that a file saved by another
    /// application appears while the user is still looking at the folder,
    /// slow enough that unpacking an archive into it does not re-list a
    /// thousand times.
    /// </summary>
    private const int WatchIntervalMs = 500;

    /// <summary>
    /// How long navigation has to stay quiet before state.json is written.
    /// See the constructor's note at <see cref="_stateSaveTimer"/>.
    /// </summary>
    private const int StateSaveDelayMs = 500;

    /// <summary>
    /// How many folders may keep a hand-picked view. See
    /// <see cref="_manualViewModes"/> for why the list is capped.
    /// </summary>
    private const int ManualViewModeLimit = 128;

    /// <summary>Smallest the preview pane may be, and what the file list keeps of the window beside it.</summary>
    private const double PreviewMinWidth = 120;
    private const double ListMinWidth = 240;

    /// <summary>The same pair for the bookmarks panel and the drives tree under it.</summary>
    private const double BookmarksMinHeight = 44;
    private const double TreeMinHeight = 200;

    private readonly IFileSystem _fs;
    private readonly IShellLauncher _shell;
    private readonly IAppStateStore _stateStore;
    private readonly IDialogs _dialogs;
    private readonly IFileLockInspector? _lockInspector;
    private readonly NavigationController _nav;
    private readonly FileOperationService _ops;
    private readonly UndoService _undo;
    private readonly OperationTracker _tracker;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _log;
    private readonly CompanionResolver _companions;

    private string _status = "";
    private string? _caretPath;
    private FileSystemEntry? _selectedEntry;
    private string? _renamingPath;
    private IReadOnlyList<FileSystemEntry> _selectedEntries = Array.Empty<FileSystemEntry>();
    // Two view modes, not one. _viewMode is what is on screen; _userViewMode
    // is what the user last asked for, and it is the one that persists. The
    // gallery switching itself on in a folder of photographs must not
    // rewrite the choice the user made — otherwise one visit to a photo
    // folder turns every folder into a gallery for good.
    private ViewMode _viewMode = ViewMode.Details;
    private ViewMode _userViewMode = ViewMode.Details;

    // Folders where the user picked a view by hand, and which one. Kept in
    // insertion order so ManualViewModeLimit drops the oldest, and
    // persisted in the session bucket of state.json: "the gallery stops
    // guessing here" has to survive a restart, or the promise lasts until
    // teatime.
    //
    // Capped rather than unbounded: this grows one entry per folder the
    // user ever set a view in, and state.json is loaded on every launch.
    private readonly Dictionary<string, ViewMode> _manualViewModes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _manualViewModeOrder = new();

    private bool _isPreviewVisible;
    private double _previewWidth = 280;
    private double _bookmarksHeight = 200;

    // Pane sizes as they were persisted, plus the window they were a share
    // of, held from RestoreState until the window is loaded and can say how
    // big it is now - see RestorePaneSizes.
    private double _savedPreviewWidth;
    private double _savedBookmarksHeight;
    private double _savedWindowWidth;
    private double _savedWindowHeight;
    private double _windowWidth;
    private double _windowHeight;

    private readonly ClipboardController _clipboard;

    private bool _isBookmarksExpanded = true;
    private IReadOnlyList<NavigationStop> _persistedExpandedPaths = Array.Empty<NavigationStop>();
    private string? _missingFolderPath;

    private CancellationTokenSource? _listLoadCts;
    private bool _isListLoading;

    // --- Ratings --------------------------------------------------------
    // Read after the listing has landed, never as part of it: the folder
    // has to appear at once, and the stars a moment later. Cancelled the
    // same way the listing is, because a pass that finishes after the user
    // has walked into another folder would push that folder's rows over
    // this one's.
    private readonly CompanionMetadataService? _companionMetadata;
    private bool _hasRatings;

    // True while rows are being swapped for updated copies. The list drops
    // a replaced object out of its selection and says so, and that report
    // would clear the preview and the status line for the two frames until
    // the selection is put back. See ReplaceRows.
    private bool _rowsReplacing;

    // --- Auto-refresh ---------------------------------------------------
    // The watcher says "something changed" from a background thread, often
    // many times in a row; the timer turns that into at most one re-listing
    // per interval. See OnWatchTick for why it is a repeating timer rather
    // than a one-shot restarted on every event.
    private readonly IDirectoryWatcher? _watcher;
    private readonly DispatcherTimer? _watchTimer;

    // Debounce for state.json — see the constructor for why.
    private readonly DispatcherTimer _stateSaveTimer;

    // The state of "the folder being looked at": listing epochs, the
    // arrival intent, selection memory and the watcher's accumulated
    // changes. Lives in Core so its races are answerable by tests; this
    // view model executes what it decides. See FolderSession.
    private readonly FolderSession _session = new();

    // Search filter is owned by SearchController; we only keep the hidden-
    // count separately for the "X items (N hidden)" status-bar message.
    private readonly SearchController _search = new();
    private int _hiddenCount;


    // Rows the running (or last) search found, in arrival order. Kept apart
    // from Entries so a re-sort has something to sort: Entries is the
    // projection on screen, this is the result set behind it.

    // The quick filter's own pass seeds the result list with the folder's
    // matches and then walks underneath it, so the walk re-finds what is
    // already there — hence the path set above — and the two halves have
    // to stay apart on screen: here first, below after.
    private bool _isSearchWindowOpen;



    public MainViewModel() {
        _fs = ServiceLocator.Get<IFileSystem>();
        _shell = ServiceLocator.Get<IShellLauncher>();
        _stateStore = ServiceLocator.Get<IAppStateStore>();
        _dialogs = ServiceLocator.Get<IDialogs>();
        _lockInspector = ServiceLocator.TryGet<IFileLockInspector>();
        _ops = ServiceLocator.Get<FileOperationService>();
        _undo = ServiceLocator.Get<UndoService>();
        _tracker = ServiceLocator.Get<OperationTracker>();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _log = ServiceLocator.Get<ILogger>();
        _companions = ServiceLocator.Get<CompanionResolver>();
        // Without a system clipboard registered the controller keeps its
        // paths to itself, exactly as it did before the mirroring existed.
        _clipboard = new ClipboardController(
            ServiceLocator.TryGet<ISystemClipboard>());

        if (ServiceLocator.TryGet<IDirectoryWatcher>() is { } watcher) {
            _watcher = watcher;
            _watcher.Changed += (_, change) => _dispatcher.BeginInvoke(() => NoteFolderChanged(change));
            _watchTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) {
                Interval = TimeSpan.FromMilliseconds(WatchIntervalMs),
            };
            _watchTimer.Tick += OnWatchTick;
        }

        // One write per quiet moment instead of one per keystroke: holding
        // an arrow key in the tree navigates several times a second, and
        // each navigation used to read and rewrite state.json on the UI
        // thread. The state is a convenience ("open where I left off"), so
        // the last half-second of it is not worth a disk write per step —
        // MainWindow flushes on close for the write still pending there.
        _stateSaveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) {
            Interval = TimeSpan.FromMilliseconds(StateSaveDelayMs),
        };
        _stateSaveTimer.Tick += (_, _) => {
            _stateSaveTimer.Stop();
            WriteStateNow();
        };

        _companionMetadata = ServiceLocator.TryGet<CompanionMetadataService>();

        // Settings VM is owned by MainVM and shared with the dialog when it
        // opens. Built before every controller that takes it: RatingsController
        // used to be handed the property four lines above its assignment and
        // silently held null - the kind of bug the two-phase construction
        // below exists to make impossible. The side-effect subscription
        // (OnSettingsChanged) is at the very end of the constructor, after
        // RestoreState - see the note there.
        Settings = new SettingsViewModel();

        Ratings = new RatingsController(
            _fs, _companions, _companionMetadata, _search, Settings, _log,
            isCurrent: _session.IsCurrent,
            publish: PublishRows,
            ask: question => _dialogs.Ask(new DialogRequest(
                DialogKind.CreateSidecar, Strings.ConfirmCreateSidecarTitle, question,
                DialogButtons.YesNo, DialogIcon.Question)));
        Ratings.HasRatingsChanged += (_, value) => HasRatings = value;
        Ratings.StatusReported += (_, text) => Status = text;

        Preview = new PreviewController(
            ServiceLocator.TryGet<IImageMetadataReader>(),
            _companionMetadata);
        Preview.RatingRequested += (_, request) =>
            request.Rating = Ratings.ApplyToPrimary(request.Entry, request.Field, request.Value);
        Preview.RevealRequested += (_, path) => RevealPath(path);
        Ratings.CompanionsChanged += (_, _) => Preview.ReloadCompanions();

        _nav = new NavigationController(
            new NavigationService(), _fs, TryGetShellNamespace(), _log);
        _nav.StatusReported += (_, text) => Status = text;

        Entries = new BulkObservableCollection<FileSystemEntry>();
        Operations = new ObservableCollection<OperationViewModel>();

        Shell = new ShellCommandsController(_shell, _log);
        Shell.StatusReported += (_, text) => Status = text;

        // The trees are created first and handed a way to look at the
        // bookmark rows, because everything that spans both panels lives
        // with them. The lambda is not called during construction, so the
        // bookmarks panel being one line away is fine (hence the !).
        Trees = new FolderTreesController(_fs, Settings, () => Bookmarks!.Items);

        // Wiring a fresh node is the trees' bookkeeping, not the panel's, so
        // the controller is handed that one operation and owns everything
        // else about bookmarks itself.
        Bookmarks = new BookmarksController(_fs, Settings, _log, Trees.Wire);
        Bookmarks.StatusReported += (_, text) => Status = text;
        Bookmarks.Changed += (_, _) => {
            Bookmarks.Build(_persistedExpandedPaths);
            SaveState();
            RaiseMissingFolder();
        };

        _tracker.Changed += OnTrackerChanged;

        OpenCommand = new RelayCommand(p => OpenEntry(p as FileSystemEntry ?? _selectedEntry), _ => _selectedEntry is not null);
        // Destructive ops are blocked inside shell namespaces (Recycle Bin):
        // the entries' FullPaths point at $Recycle.Bin backing files, and
        // copying / deleting / renaming those would bypass the shell's
        // restore-tracking and corrupt the bin's state. Read-only browsing
        // only in this iteration.
        DeleteCommand = new RelayCommand(_ => _ = DeleteSelectedAsync(permanent: false), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        RenameCommand = new RelayCommand(p => Rename(_selectedEntry, p as string), _ => _selectedEntry is not null && !IsCurrentShellNamespace);
        // Copy is the one clipboard verb an archive still answers: it puts
        // the paths inside the archive on the clipboard, and Paste in a real
        // folder turns them into an extraction. The bin stays excluded -
        // pasting a $Recycle.Bin backing path copies a mangled file.
        CopyCommand = new RelayCommand(
            _ => Copy(),
            _ => _selectedEntries.Count > 0 && (!IsCurrentShellNamespace || CurrentArchive is not null));
        ExtractCommand = new RelayCommand(_ => _ = ExtractSelectionAsync(), _ => CanExtractSelection());
        CutCommand = new RelayCommand(_ => Cut(), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        PasteCommand = new RelayCommand(_ => _ = PasteAsync(), _ => _clipboard.HasContent && _nav.Current is not null && !IsCurrentShellNamespace);
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => _nav.Current is not null && !IsCurrentShellNamespace);
        RestoreFromRecycleBinCommand = new RelayCommand(
            _ => RestoreFromRecycleBin(),
            _ => IsCurrentRecycleBin && _selectedEntries.Count > 0);
        RefreshCommand = new RelayCommand(_ => RefreshOrRerunSearch());
        SetViewModeCommand = new RelayCommand(p => SetViewMode(p as string));
        SetGalleryBackgroundCommand = new RelayCommand(p => SetGalleryBackground(p as string));
        FilterColorChoices = ColorLabelViewModel.CreateChoices();
        SetFilterRankCommand = new RelayCommand(p => SetFilterRank(p as string));
        SetRankForSelectionCommand = new RelayCommand(p => SetRankForSelection(p as string));
        SetFilterColorCommand = new RelayCommand(p => SetFilterColor(p as string));
        ClearRatingFilterCommand = new RelayCommand(_ => ClearRatingFilter(), _ => HasRatingFilter);
        SetSortKeyCommand = new RelayCommand(p => SetSortKey(p as string));
        ToggleSortAscendingCommand = new RelayCommand(_ => Settings.SortAscending = !Settings.SortAscending);
        ToggleGroupFoldersFirstCommand = new RelayCommand(_ => Settings.GroupFoldersFirst = !Settings.GroupFoldersFirst);
        ExitCommand = new RelayCommand(_ => Application.Current?.Shutdown());
        OptionsCommand = new RelayCommand(_ => OpenSettingsDialog());
        // GitHub's template chooser lets the user pick "Bug report" or
        // "Feature request"; nothing is pre-filled, so no session data is
        // involved — unlike the crash path, which bundles diagnostics.
        ReportIssueCommand = new RelayCommand(
            _ => Shell.OpenUrl(Diagnostics.CrashReporter.IssueChooserUrl));
        HelpCommand = new RelayCommand(_ => Shell.OpenUrl(Diagnostics.CrashReporter.GuideUrl));
        // Properties falls back to the folder being listed, so a background
        // right-click (and Alt+Enter with nothing selected) opens the
        // folder's own sheet — Explorer parity.
        PropertiesCommand = new RelayCommand(_ => ShowProperties(), _ => PropertiesTarget() is not null);
        OpenWithCommand = new RelayCommand(_ => OpenWith(), _ => _selectedEntry is not null && !IsCurrentShellNamespace);
        OpenInTerminalCommand = new RelayCommand(_ => OpenInTerminal(), _ => TerminalFolder() is not null);
        CopyPathCommand = new RelayCommand(
            _ => Shell.CopyPaths(SelectedPathsOrCurrent()), _ => PropertiesTarget() is not null);
        CopyNameCommand = new RelayCommand(
            _ => Shell.CopyNames(_selectedEntries.Select(e => e.Name).ToArray()),
            _ => _selectedEntries.Count > 0);
        CreateShortcutCommand = new RelayCommand(
            _ => CreateShortcutsForSelection(),
            _ => _selectedEntries.Count > 0 && _nav.Current is not null && !IsCurrentShellNamespace);
        OpenJournalCommand = new RelayCommand(_ => OpenJournal(), _ => Journal.Count > 0);
        TogglePreviewCommand = new RelayCommand(_ => IsPreviewVisible = !IsPreviewVisible);
        UndoCommand = new RelayCommand(_ => UndoLast(), _ => _undo.CanUndo);
        PermanentDeleteCommand = new RelayCommand(_ => _ = DeleteSelectedAsync(permanent: true), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        OpenLogFileCommand = new RelayCommand(_ => Shell.OpenLogFile(), _ => ServiceLocator.IsRegistered<ILogFile>());
        ToggleBookmarksCommand = new RelayCommand(_ => IsBookmarksExpanded = !IsBookmarksExpanded);
        AddBookmarkCommand = new RelayCommand(p => Bookmarks.Add(p as string));
        RemoveBookmarkCommand = new RelayCommand(p => Bookmarks.Remove((p as TreeNodeViewModel)?.FullPath));
        RemoveMissingBookmarkCommand = new RelayCommand(_ => Bookmarks.Remove(_missingFolderPath), _ => IsMissingBookmark);
        RelocateMissingBookmarkCommand = new RelayCommand(_ => RelocateBookmark(_missingFolderPath), _ => IsMissingBookmark);

        // Batch executors push undo steps from thread-pool workers, so this
        // event can arrive off the UI thread; CommandManager requery only
        // works on the dispatcher thread.
        // Arrives from the thread pool when a background operation records
        // an action, so it goes through Post rather than straight through.
        _undo.Changed += (_, _) => _dispatcher.Post(() => {
            UndoCommand.RaiseCanExecuteChanged();
            Raise(nameof(UndoTooltip));
        });

        _clipboard.Changed += (_, _) => {
            PasteCommand.RaiseCanExecuteChanged();
            Raise(nameof(CutPaths));
        };

        // The controller decides for itself when to run, so it needs the
        // two things only the view model knows — where we are and what may
        // be shown. Handed as callbacks rather than copied, because both
        // change under it while a search is being set up.
        ContentSearch = new ContentSearchController(
            _dispatcher,
            ServiceLocator.TryGet<ContentSearchService>(),
            () => _nav.Current,
            () => Settings.Visibility,
            _log);

        SearchResults = new SearchResultsController(ContentSearch, _fs, Settings, _dispatcher);
        SearchResults.RowsChanged += (_, rows) => Entries.ReplaceAll(rows);
        SearchResults.StatusReported += (_, text) => Status = text;

        ContentSearch.Started += BeginSearchResults;
        ContentSearch.BatchArrived += SearchResults.Append;
        ContentSearch.Progressed += SearchResults.ReportProgress;
        ContentSearch.Finished += SearchResults.Finish;
        ContentSearch.ShallowChanged += SyncLiveFilter;
        ContentSearch.PropertyChanged += OnContentSearchChanged;

        SearchCommand = new RelayCommand(_ => ContentSearch.RunNow());
        StopSearchCommand = new RelayCommand(_ => ContentSearch.Stop(), _ => ContentSearch.IsRunning);
        ClearSearchCommand = new RelayCommand(_ => ClearSearch(), _ => HasSearchQuery || ContentSearch.IsShowingResults);

        _search.PropertyChanged += (_, e) => {
            // SearchController owns the underlying _query; surface the
            // changes under the names XAML/binding consumers already expect.
            if (e.PropertyName == nameof(SearchController.RatingFilter)) {
                Raise(nameof(RatingFilter));
                Raise(nameof(FilterIncludesUnrated));
                SyncFilterChoices();
            } else if (e.PropertyName == nameof(SearchController.HasRatingFilter)) {
                Raise(nameof(HasRatingFilter));
                ClearRatingFilterCommand.RaiseCanExecuteChanged();
            }
        };
        _search.FilteredChanged += filtered => {
            // A folder listing that lands while the list is showing search
            // results would replace them. The watcher, a finishing rating
            // pass and a stale refresh can all get here after the search
            // took the list over.
            if (ContentSearch.IsShowingResults) {
                return;
            }

            using (PerfLog.Measure("ui.rows")) {
                ReconcileEntries(() => SyncEntries(filtered));
            }
            UpdateFilterStatus(filtered.Count, _search.Source.Count);
            using (PerfLog.Measure("ui.restore")) {
                ApplyArrival();
            }
        };
        _search.ItemsChanged += changed => {
            if (ContentSearch.IsShowingResults) {
                return;
            }

            ReplaceRows(changed);
        };

        _nav.CurrentChanged += (_, _) => OnNavigationChanged();
        _nav.PropertyChanged += (_, e) => {
            // Surface AddressText / WindowTitle / CurrentPath changes under
            // the names XAML already binds to. NavigationController owns the
            // truth; MainVM is just the shop window.
            if (e.PropertyName == nameof(NavigationController.AddressText)) {
                Raise(nameof(AddressText));
            } else if (e.PropertyName == nameof(NavigationController.WindowTitle)) {
                Raise(nameof(WindowTitle));
            } else if (e.PropertyName == nameof(NavigationController.Current)) {
                Raise(nameof(CurrentPath));
            }
        };

        Trees.LoadRoots();
        RestoreState();
        // Restored settings decide how big the thumbnail caches may be; the
        // provider starts idle until it is told.
        ApplyThumbnailCacheSettings();

        // --- Turn on. Everything above built the object; from here it
        // reacts to changes. Subscribed after RestoreState on purpose:
        // ApplyFrom raises one PropertyChanged per restored setting, and
        // running the side effects during construction - a Refresh here, a
        // tree reload there, depending on which restored value happened to
        // differ from its default - was the hidden-initialization-order
        // problem this ordering replaces (O6.4). Same for the expansions
        // RestoreState re-opens: they are the saved state coming back, not
        // a change worth saving.
        Settings.PropertyChanged += OnSettingsChanged;
        Trees.ExpansionChanged += (_, _) => SaveState();
    }


    /// <summary>
    /// Raised after a refresh has reconciled <see cref="Entries"/>, carrying
    /// the rows that should be selected again. The view owns multi-selection
    /// (SelectedItems lives on the controls, not here), so it does the actual
    /// selecting.
    /// </summary>
    public event Action<IReadOnlyList<FileSystemEntry>>? SelectionRestoreRequested;

    /// <summary>
    /// Raised after rows were swapped for updated copies in place, carrying
    /// the rows that were selected before. Distinct from
    /// <see cref="SelectionRestoreRequested"/> in exactly one way, and it is
    /// the whole reason it exists: this one must not scroll and must not
    /// move the keyboard. Nothing happened that the user should be taken
    /// anywhere for — a number changed inside a row they are looking at.
    /// </summary>
    public event Action<IReadOnlyList<FileSystemEntry>>? SelectionRefreshRequested;

    /// <summary>
    /// Raised when the row a restore just landed on is one the user is
    /// expected to name — a folder that has only just been created. The
    /// in-place editor lives in the row template, so opening it is the
    /// view's job; the view model only says which row and when.
    /// </summary>
    public event Action<FileSystemEntry>? InlineRenameRequested;

    /// <summary>
    /// The rows of a folder the user walked into have landed. Carries the
    /// clock that has been running since the navigation, so the view can
    /// time the first screen from there (<c>FirstScreenWatch</c>).
    /// </summary>
    public event Action<string, System.Diagnostics.Stopwatch>? FolderArrived;


    public BulkObservableCollection<FileSystemEntry> Entries { get; }
    public ObservableCollection<OperationViewModel> Operations { get; }

    /// <summary>The left panel's bookmarks — the rows and the list behind them.</summary>
    public BookmarksController Bookmarks { get; }

    /// <summary>The drives tree, and what both folder panels share.</summary>
    public FolderTreesController Trees { get; }

    /// <summary>Stars and colour labels — reading them, writing them, keeping rows in step.</summary>
    public RatingsController Ratings { get; }

    /// <summary>The rows a deep search puts on the list, and their bookkeeping.</summary>
    public SearchResultsController SearchResults { get; }

    /// <summary>
    /// The files waiting to be moved, so the list can fade them. Empty for a
    /// copy: a copy leaves the originals exactly where they are, and fading
    /// them would promise a move that is not going to happen.
    /// </summary>
    public IReadOnlyList<string> CutPaths =>
        _clipboard.IsCut ? _clipboard.Paths : Array.Empty<string>();

    /// <summary>Verbs that hand a path to the system and are done with it.</summary>
    public ShellCommandsController Shell { get; }

    /// <summary>
    /// Owns the preview pane content (kind, image / text / code / web,
    /// footer summary). MainVM only feeds it selection / folder / visibility
    /// — XAML binds to <c>Preview.X</c> directly.
    /// </summary>
    public PreviewController Preview { get; }

    /// <summary>
    /// User preferences. XAML binds to this (e.g. tile sizes) and the
    /// settings dialog edits it directly. Side effects (re-listing the
    /// folder when ShowHidden flips, persisting to disk) run through
    /// <see cref="OnSettingsChanged"/>.
    /// </summary>
    public SettingsViewModel Settings { get; }

    private double _aggregateProgress;
    public double AggregateProgress {
        get => _aggregateProgress;
        private set => SetField(ref _aggregateProgress, value);
    }

    public bool HasActiveOperations => Operations.Count > 0;

    /// <summary>
    /// Set during async list enumeration (Recycle Bin and other shell
    /// namespaces — the Shell.Application COM hop can take a noticeable
    /// fraction of a second). Bound to a spinner overlay on the right
    /// pane so the user sees that something is happening.
    /// </summary>
    public bool IsListLoading {
        get => _isListLoading;
        private set => SetField(ref _isListLoading, value);
    }

    public string? CurrentPath => _nav.Current;

    /// <summary>
    /// Navigation state the address bar binds to directly
    /// (<c>Nav.Breadcrumbs</c>, <c>Nav.RecentPaths</c>,
    /// <c>Nav.IsEditingAddress</c>) — same arrangement as
    /// <see cref="Preview"/>. The Back/Forward/Up/Navigate commands stay
    /// mirrored below so existing bindings keep working.
    /// </summary>
    public NavigationController Nav => _nav;

    /// <summary>Address-bar text. Backed by <see cref="NavigationController.AddressText"/>.</summary>
    public string AddressText {
        get => _nav.AddressText;
        set => _nav.AddressText = value;
    }

    public string Status {
        get => _status;
        set {
            // Noted before the property changes, so the journal holds every
            // line the user could have seen - including the ones a second
            // message replaced before the eye got to them. That is the
            // whole reason it exists.
            Journal.Note(value, DateTime.Now);
            SetField(ref _status, value);
        }
    }

    /// <summary>
    /// What happened this session, in the words the status bar used. Read
    /// by the journal button next to it — see
    /// <see cref="OpenJournalCommand"/>.
    ///
    /// <para>
    /// Not a copy of the status line: the line also carries what the list
    /// <em>is</em> ("элементов: 27", rewritten on every keystroke of a
    /// filter), and a journal of those answers nothing. Those go through
    /// <see cref="SetStatusQuietly"/>; what the journal keeps is the
    /// folders that were opened and the operations that ran in them.
    /// </para>
    /// </summary>
    public ActionJournal Journal { get; } = new();

    /// <summary>
    /// The row the keyboard would move from — Explorer's focus rectangle,
    /// and the only thing on screen after a click on empty space that says
    /// where the next arrow key starts.
    ///
    /// <para>
    /// A path rather than a row: rows are replaced on every re-listing and
    /// on every rating written, and a caret held as an object would either
    /// go stale or force the list to be rebuilt to move it. The list sets
    /// it; the row templates read it through
    /// <see cref="Converters.CaretRowConverter"/>.
    /// </para>
    /// </summary>
    public string? CaretPath {
        get => _caretPath;
        set => SetField(ref _caretPath, value);
    }

    public FileSystemEntry? SelectedEntry {
        get => _selectedEntry;
        set {
            // Nothing the list says about the selection while its rows are
            // being swapped is the truth. It drops each replaced object as
            // it goes and moves SelectedItem onto whichever selected row is
            // still standing, so a plain three-row update walked the preview
            // through three other photographs before landing back on the
            // right one. ReconcileEntries assigns the real value when the
            // swap is over.
            if (_rowsReplacing) {
                return;
            }
            if (SetField(ref _selectedEntry, value)) {
                Preview.SetPrimary(value);
            }
        }
    }

    /// <summary>
    /// Set by an operation that ran behind a modal dialog: by the time the
    /// dialog closes, the row that had the keyboard has been rebuilt out of
    /// existence and focus is left sitting on the window. The list takes it
    /// back together with the restored selection — losing the keyboard after
    /// every operation is one of the Explorer habits this project exists to
    /// avoid. Consumed once, by <c>FileListView.RestoreListSelection</c>.
    /// </summary>
    public bool FocusListAfterRestore { get; set; }


    public IReadOnlyList<FileSystemEntry> SelectedEntries {
        get => _selectedEntries;
        set {
            // Same reason as SelectedEntry above: while rows are being
            // swapped the list reports a selection it is about to get back,
            // and the status bar must not count that out loud.
            if (_rowsReplacing) {
                return;
            }
            if (SetField(ref _selectedEntries, value)) {
                NoteSelectionKind();
                Preview.SetSelection(value);
                Raise(nameof(SelectionSummary));
            }
        }
    }

    /// <summary>
    /// True when everything selected is an archive Wander can open — the
    /// precondition for "Извлечь…" outside an archive. A field rather than
    /// a computed property for the same reason as
    /// <see cref="IsCurrentShellNamespace"/>: the answer ends in a
    /// <c>File.Exists</c> per row, and <c>CanExecute</c> asks constantly.
    /// </summary>
    public bool SelectionIsArchive { get; private set; }

    /// <summary>
    /// "Выбрано: 3 · 1.2 MB" — what the selection amounts to, for the status
    /// bar. Empty when nothing is selected, so the field disappears rather
    /// than showing a zero.
    ///
    /// <para>
    /// Folders count as objects but not as bytes: their real size needs a
    /// recursive walk, and doing one on every click is exactly the kind of
    /// thing that makes a file manager feel slow. The count says how many of
    /// them were left out of the total, so the number on screen is never
    /// quietly wrong.
    /// </para>
    /// </summary>
    public string SelectionSummary {
        get {
            if (_selectedEntries.Count == 0) {
                return "";
            }

            long bytes = 0;
            int folders = 0;
            foreach (var entry in _selectedEntries) {
                if (entry.IsFolderLike) {
                    folders++;
                } else {
                    bytes += entry.Size ?? 0;
                }
            }

            string text = string.Format(
                Strings.StatusSelection, _selectedEntries.Count, SizeFormatter.Format(bytes));

            return folders == 0
                ? text
                : text + string.Format(Strings.StatusSelectionFolders, folders);
        }
    }

    /// <summary>
    /// What is in the search box. One box, two behaviours: while the search
    /// is shallow (this folder, names only) every keystroke is forwarded to
    /// <see cref="SearchController"/> and the list narrows live, exactly as
    /// it always has. Once contents or subfolders are switched on, the text
    /// is a query waiting for Enter and the folder on screen is left alone
    /// — a disk walk per keystroke is not a filter.
    /// </summary>
    public string SearchQuery {
        get => ContentSearch.FilterText;
        set => ContentSearch.FilterText = value;
    }

    public bool HasSearchQuery => ContentSearch.NameQuery.Length > 0 || ContentSearch.TextQuery.Length > 0;

    /// <summary>
    /// The deep half of search — subfolders, file contents, the system
    /// index. Bound directly by the panel behind the search box.
    /// </summary>
    public ContentSearchController ContentSearch { get; }

    /// <summary>True when the list is showing search results rather than a folder.</summary>
    public bool IsSearchResults => ContentSearch.IsShowingResults;

    /// <summary>
    /// Whether the search window is up. The toolbar box hides while it is:
    /// the same criteria in two places is how one of them ends up stale,
    /// and only the window can show all of them.
    /// </summary>
    public bool IsSearchWindowOpen {
        get => _isSearchWindowOpen;
        set => SetField(ref _isSearchWindowOpen, value);
    }

    /// <summary>
    /// The view on screen. Written both by the user (through
    /// <see cref="SetViewModeCommand"/>, which also remembers the choice)
    /// and by <see cref="AutoSelectViewMode"/>; persistence hangs off the
    /// former only, which is why this setter saves nothing.
    /// </summary>
    public ViewMode ViewMode {
        get => _viewMode;
        set {
            if (SetField(ref _viewMode, value)) {
                Raise(nameof(ContentPalette));
            }
        }
    }

    /// <summary>
    /// The colours of the area the files are shown in — which is the
    /// gallery's palette <em>only while the gallery is on screen</em>.
    ///
    /// <para>
    /// The distinction matters because the surround is a gallery setting,
    /// not an application theme: the table, the tiles and the icons are
    /// drawn on the window's own background whatever it is set to. The
    /// preview pane follows the area it sits next to, so it has to ask this
    /// rather than ask the setting — asking the setting put a black pane
    /// beside a white file list.
    /// </para>
    ///
    /// <para>
    /// The gallery itself still binds the setting directly: it is only ever
    /// visible in the one mode where the two agree.
    /// </para>
    /// </summary>
    public GalleryPalette ContentPalette =>
        ViewMode == ViewMode.Gallery ? Settings.GalleryPalette : GalleryPalette.Plain;

    public bool IsPreviewVisible {
        get => _isPreviewVisible;
        set {
            if (SetField(ref _isPreviewVisible, value)) {
                Preview.SetVisible(value);
                SaveState();
            }
        }
    }

    public double PreviewWidth {
        get => _previewWidth;
        set {
            double clamped = Math.Max(PreviewMinWidth, Math.Min(PaneSizes.LegacyMax, value));
            if (SetField(ref _previewWidth, clamped)) {
                SaveState();
            }
        }
    }

    /// <summary>
    /// Height of the bookmarks region, in pixels — where the user left the
    /// divider in the left pane. Same arrangement as
    /// <see cref="PreviewWidth"/>: the window applies it to the grid, the
    /// view model persists it.
    /// </summary>
    public double BookmarksHeight {
        get => _bookmarksHeight;
        set {
            double clamped = Math.Max(BookmarksMinHeight, Math.Min(PaneSizes.LegacyMax, value));
            if (SetField(ref _bookmarksHeight, clamped)) {
                SaveState();
            }
        }
    }

    // Navigation commands live on NavigationController; surface them here
    // so existing XAML bindings (BackCommand / ForwardCommand / ...) keep
    // working without touching every <KeyBinding> and <Button>.
    public RelayCommand BackCommand => _nav.BackCommand;
    public RelayCommand ForwardCommand => _nav.ForwardCommand;
    public RelayCommand UpCommand => _nav.UpCommand;
    public RelayCommand NavigateCommand => _nav.NavigateCommand;
    public RelayCommand OpenCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand CutCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand NewFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand StopSearchCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand RestoreFromRecycleBinCommand { get; }

    /// <summary>Takes the selection out of an archive into a folder the user picks.</summary>
    public RelayCommand ExtractCommand { get; }

    public RelayCommand SetViewModeCommand { get; }
    public RelayCommand SetGalleryBackgroundCommand { get; }
    public RelayCommand SetSortKeyCommand { get; }
    public RelayCommand ToggleSortAscendingCommand { get; }
    public RelayCommand ToggleGroupFoldersFirstCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand OptionsCommand { get; }
    public RelayCommand ReportIssueCommand { get; }
    public RelayCommand HelpCommand { get; }
    public RelayCommand PropertiesCommand { get; }
    public RelayCommand TogglePreviewCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand PermanentDeleteCommand { get; }
    public RelayCommand OpenLogFileCommand { get; }
    public RelayCommand ToggleBookmarksCommand { get; }
    public RelayCommand AddBookmarkCommand { get; }

    public RelayCommand RemoveBookmarkCommand { get; }
    public RelayCommand RemoveMissingBookmarkCommand { get; }
    public RelayCommand RelocateMissingBookmarkCommand { get; }
    public RelayCommand OpenWithCommand { get; }
    public RelayCommand OpenJournalCommand { get; }
    public RelayCommand OpenInTerminalCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public RelayCommand CopyNameCommand { get; }
    public RelayCommand CreateShortcutCommand { get; }


    /// <summary>
    /// Context-menu preferences in the shape <c>ContextMenuBuilder</c> wants.
    /// Rebuilt per right-click rather than cached — the settings dialog is
    /// live-applied, so a cache would only be a way to show a stale menu.
    /// </summary>
    public ContextMenuSettings MenuSettings => ContextMenuSettings.From(Settings.ToRecord());

    public bool IsBookmarksExpanded {
        get => _isBookmarksExpanded;
        set {
            if (SetField(ref _isBookmarksExpanded, value)) {
                SaveState();
            }
        }
    }

    public string UndoTooltip => _undo.NextDescription is { } next ? $"Undo: {next}" : "Nothing to undo";

    public string WindowTitle => _nav.WindowTitle;


    public void NavigateTo(string path, NavigationSource source = NavigationSource.External) {
        _log.Info($"Navigate ({source}): {path}");
        _nav.NavigateTo(path, source);
    }

    // --- Shell-namespace helpers ---------------------------------------
    // Centralised checks so Navigate / Refresh / the bookmarks panel all agree
    // on what counts as a recognised shell location.

    /// <summary>
    /// True when the user is currently browsing a shell namespace (the
    /// Recycle Bin, or inside an archive). Used to gate destructive
    /// commands — those would operate on raw $Recycle.Bin backing paths, or
    /// try to write into a container that is read-only by decision.
    ///
    /// <para>
    /// Answered from a field, not recomputed: WPF re-evaluates every
    /// <c>CanExecute</c> dozens of times a second, and the archive half of
    /// the question ends in a <c>File.Exists</c>. It is recomputed once per
    /// navigation, in <see cref="NoteCurrentLocation"/>.
    /// </para>
    /// </summary>
    public bool IsCurrentShellNamespace { get; private set; }

    /// <summary>
    /// The archive being browsed, or null anywhere else. Same cache and the
    /// same reason as <see cref="IsCurrentShellNamespace"/>.
    /// </summary>
    public ArchivePath? CurrentArchive { get; private set; }

    /// <summary>
    /// True in the Recycle Bin specifically. Read-only like any shell
    /// namespace, but with one thing you can still do to its contents —
    /// put them back.
    /// </summary>
    public bool IsCurrentRecycleBin =>
        string.Equals(_nav.Current, ShellPaths.RecycleBin, StringComparison.OrdinalIgnoreCase);

    private static IShellNamespace? TryGetShellNamespace() {
        return ServiceLocator.TryGet<IShellNamespace>();
    }

    /// <summary>
    /// Re-reads what kind of place the current path is. Called once per
    /// navigation, before anything that gates on the answer runs.
    /// </summary>
    private void NoteCurrentLocation() {
        CurrentArchive = Archives.Of(_nav.Current);
        IsCurrentShellNamespace = IsShellPath(_nav.Current);
    }

    private bool IsShellPath(string? path) {
        return !string.IsNullOrEmpty(path)
            && TryGetShellNamespace() is { } ns
            && ns.IsShellPath(path);
    }

    // --- Opening an entry -----------------------------------------------
    public void OpenEntry(FileSystemEntry? entry) {
        if (entry is null) {
            return;
        }

        if (entry.Kind == EntryKind.File) {
            // A .lnk that points at a folder should behave like that folder:
            // navigate inside Wander rather than handing the .lnk to the OS
            // (which would open it in Explorer). File-targeted shortcuts fall
            // through to the normal shell launcher — the OS resolves them.
            if (TryFollowFolderShortcut(entry.FullPath)) {
                return;
            }

            // An archive the shell can browse opens as a folder, the way
            // Explorer opens it. Only the container itself: a .zip found
            // *inside* another archive is a file like any other and is
            // unpacked to a temporary copy below.
            if (Archives.Of(entry.FullPath) is { } archive) {
                if (archive.IsRoot) {
                    NavigateTo(entry.FullPath, DescendSource());
                } else {
                    // No path on disk to hand the shell, so make one.
                    _ = OpenArchiveEntryAsync(entry.FullPath);
                }
                return;
            }

            try {
                _shell.Open(entry.FullPath);
            } catch (Exception ex) {
                Status = string.Format(Strings.StatusOpenFailed, ex.Message);
            }
            return;
        }

        NavigateTo(entry.FullPath, DescendSource());
    }


    /// <summary>
    /// Walking into a subfolder from the list keeps the panel the current
    /// folder was opened from — the same inheritance <c>NavigationService.GoUp</c>
    /// does going the other way. Without it, opening a bookmark and then
    /// stepping one folder deeper jumped the highlight to the drives tree,
    /// which is not where the user was reading.
    /// </summary>
    private NavigationSource DescendSource() {
        return _nav.CurrentSource == NavigationSource.Bookmark
            ? NavigationSource.Bookmark
            : NavigationSource.RightPane;
    }

    private bool TryFollowFolderShortcut(string path) {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        string? target;
        try {
            target = ServiceLocator.Get<IShortcutService>().Resolve(path);
        } catch (Exception ex) {
            _log.Warn($"Resolve shortcut failed: {path} ({ex.Message})");
            return false;
        }

        if (string.IsNullOrEmpty(target) || !_fs.DirectoryExists(target)) {
            return false;
        }

        _log.Info($"Follow folder shortcut: {path} -> {target}");
        NavigateTo(target, DescendSource());
        return true;
    }

    // --- Drop -----------------------------------------------------------
    public void HandleDrop(IReadOnlyList<string> sourcePaths, string? targetFolder, DropEffect effect) {
        _ = HandleDropAsync(sourcePaths, targetFolder, effect);
    }

    private async Task HandleDropAsync(IReadOnlyList<string> sourcePaths, string? targetFolder, DropEffect effect) {
        if (sourcePaths.Count == 0) {
            return;
        }

        targetFolder ??= _nav.Current;
        if (string.IsNullOrEmpty(targetFolder) || !_fs.DirectoryExists(targetFolder)) {
            Status = Strings.StatusNoDropTarget;
            return;
        }

        if (effect == DropEffect.Link) {
            CreateShortcuts(sourcePaths, targetFolder);
            return;
        }

        if (effect == DropEffect.Move && !ConfirmMove(sourcePaths, targetFolder)) {
            return;
        }

        // Also for a drag that came from outside Wander: dropping a .png
        // means dropping the asset, and Explorer had no idea about its
        // sidecar. Wander's own drags arrive pre-expanded, and the dedupe
        // makes the second pass free. Off the UI thread because this one
        // does hit the disk, once per rule per dropped path.
        var groups = await Task.Run(() => GroupPathsWithCompanions(sourcePaths));

        _log.Info($"Drop: {effect} {groups.Count} item(s) into {targetFolder}");
        var resolver = _dialogs.CreateConflictResolver(Settings.SkipIdenticalOnConflict);
        IReadOnlyList<BatchItemResult> results;
        try {
            results = await RunWithProgressDialogAsync(
                effect == DropEffect.Move ? Strings.ProgressMoving : Strings.ProgressCopying,
                ct => effect == DropEffect.Move
                    ? _ops.MoveManyAsync(groups, targetFolder, resolver, ct)
                    : _ops.CopyManyAsync(groups, targetFolder, resolver, ct));
        } catch (OperationCanceledException) {
            Status = Strings.StatusCancelled;
            return;
        } catch (Exception ex) {
            _log.Error($"Drop failed: {effect} -> {targetFolder}", ex);
            Status = string.Format(Strings.StatusDropFailed, ex.Message);
            return;
        }

        Refresh();
        ReportBatchResults(results, effect == DropEffect.Move ? Strings.VerbMoved : Strings.VerbCopied, targetFolder);
    }

    private void CreateShortcuts(IReadOnlyList<string> sources, string targetFolder) {
        var shortcuts = ServiceLocator.Get<IShortcutService>();
        var created = new List<IUndoableAction>();
        var bin = ServiceLocator.Get<IRecycleBin>();
        int ok = 0;
        foreach (string src in sources) {
            string srcName = Path.GetFileNameWithoutExtension(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string baseName = $"{srcName} - Shortcut.lnk";
            string dest = Path.Combine(targetFolder, baseName);
            int i = 1;
            while (_fs.FileExists(dest) || _fs.DirectoryExists(dest)) {
                dest = Path.Combine(targetFolder, $"{srcName} - Shortcut ({i}).lnk");
                i++;
            }

            try {
                shortcuts.Create(src, dest);
                created.Add(new CreateAction(bin, dest));
                ok++;
            } catch (Exception ex) {
                _log.Error($"Create shortcut failed: {src} -> {dest}", ex);
                Status = string.Format(Strings.StatusShortcutFailed, srcName, ex.Message);
            }
        }

        // One Ctrl+Z undoes the whole batch, same as a paste or a drop.
        if (created.Count > 0) {
            _log.Info($"Create shortcut: {created.Count} item(s) in {targetFolder}");
            _undo.Push(created.Count == 1
                ? created[0]
                : new CompositeAction($"Create {created.Count} shortcuts", created));
        }

        Refresh();
        if (ok > 0) {
            Status = string.Format(Strings.StatusShortcutsCreated, ok, targetFolder);
        }
    }


    // --- Startup state -------------------------------------------------

    /// <summary>
    /// Writes a pending save out immediately. The window calls this while
    /// closing — the debounce would otherwise drop whatever changed in the
    /// last half-second of the session, which is exactly the state "open
    /// where I left off" needs.
    /// </summary>
    public void FlushState() {
        if (!_stateSaveTimer.IsEnabled) {
            return;
        }

        _stateSaveTimer.Stop();
        WriteStateNow();
    }

    /// <summary>
    /// Puts the side panes back at the sizes they were left at, scaled to
    /// the window they are coming back into - see
    /// <see cref="PaneSizes.Restore"/>. Called from the window's Loaded
    /// handler, because that is the first moment there is a window with a
    /// size to scale against.
    /// </summary>
    public void RestorePaneSizes(double windowWidth, double windowHeight) {
        NoteWindowSize(windowWidth, windowHeight);

        // Nothing usable saved (a fresh install, a hand-edited file): the
        // defaults stand, exactly as before.
        if (_savedPreviewWidth > 0) {
            _previewWidth = PaneSizes.Restore(
                _savedPreviewWidth, _savedWindowWidth, windowWidth, PreviewMinWidth, ListMinWidth);
            Raise(nameof(PreviewWidth));
        }
        if (_savedBookmarksHeight > 0) {
            _bookmarksHeight = PaneSizes.Restore(
                _savedBookmarksHeight, _savedWindowHeight, windowHeight, BookmarksMinHeight, TreeMinHeight);
            Raise(nameof(BookmarksHeight));
        }
    }

    /// <summary>
    /// The window size the pane sizes are a share of, kept current so the
    /// two always go into <c>state.json</c> describing the same moment.
    /// </summary>
    public void NoteWindowSize(double width, double height) {
        _windowWidth = width;
        _windowHeight = height;
    }


    private void RestoreState() {
        var state = _stateStore.Load();
        var session = state.Session;

        DropThumbnailCacheOnUpgrade(state.LastRunVersion);

        // Settings before view mode / navigation: ShowHidden affects
        // what Refresh() displays, so load filters before the first
        // Refresh fires via the navigation change below. No side
        // effects fire from these setters: OnSettingsChanged is not
        // subscribed until the constructor's turn-on block, which is
        // what replaced the old _restoring flag.
        Settings.ApplyFrom(state.Settings);

        if (!string.IsNullOrEmpty(session.ViewMode) && Enum.TryParse<ViewMode>(session.ViewMode, out var mode)) {
            _userViewMode = mode;
            _viewMode = mode;
            Raise(nameof(ViewMode));
        }

        // A mode name that no longer parses (a renamed enum member in a
        // future version, a hand-edited file) is dropped rather than
        // guessed at: the automatic choice is a fine fallback.
        foreach (var folder in session.ManualViewModes) {
            if (!string.IsNullOrEmpty(folder.Path) && Enum.TryParse<ViewMode>(folder.Mode, out var saved)) {
                RememberManualViewMode(folder.Path, saved);
            }
        }

        _isPreviewVisible = session.IsPreviewVisible;
        Raise(nameof(IsPreviewVisible));
        Preview.SetVisible(_isPreviewVisible);
        // Not applied here: what a saved pane size means depends on the
        // window it was saved from and the one it is coming back into, and
        // there is no window yet - the constructor runs before it exists.
        // RestorePaneSizes, called from MainWindow.OnLoaded, finishes this.
        _savedPreviewWidth = session.PreviewWidth;
        _savedBookmarksHeight = session.BookmarksHeight;
        _savedWindowWidth = session.LayoutWindowWidth;
        _savedWindowHeight = session.LayoutWindowHeight;

        Bookmarks.Load(state.Favorites);
        _isBookmarksExpanded = session.IsBookmarksExpanded;
        Raise(nameof(IsBookmarksExpanded));

        _persistedExpandedPaths = session.ExpandedPaths.ToArray();
        // Drives-side expansions are restored immediately. Bookmark-side
        // ones wait until the bookmarks panel is built below — its rows are
        // not there yet, and the matching VM instances do not exist either.
        // expandTarget:true so the saved node itself is shown as expanded,
        // not just its ancestors (a saved stop means "this node's children
        // were visible at close").
        foreach (var stop in _persistedExpandedPaths) {
            if (stop.Source == NavigationSource.Bookmark) {
                continue;
            }
            foreach (var root in Trees.Roots) {
                if (root.TryExpandToPath(stop.Path, select: false, expandTarget: true)) {
                    break;
                }
            }
        }

        // Build the bookmarks tree *before* the initial navigation —
        // OnNavigationChanged → Trees.ExpandTo walks the bookmarks
        // for source=Bookmark, and if it's still empty the expander
        // falls back to drives, defeating the whole point of the
        // restored source.
        Bookmarks.Build(_persistedExpandedPaths);
        // Consumed: from here on the panels themselves are the record
        // of what is expanded. Keeping the startup set around made
        // every later rebuild (a bookmark added, a special folder
        // switched on) re-open branches the user had since collapsed.
        _persistedExpandedPaths = Array.Empty<NavigationStop>();

        // Before the first navigation: that navigation pushes the
        // restored folder onto the list, and it should land on top of
        // the remembered ones rather than under them.
        _nav.LoadRecentPaths(session.RecentPaths);

        // Honour the RestoreLastFolder preference: when off, ignore
        // LastPath and start at the first drive.
        _ = OpenStartFolderAsync(Settings.RestoreLastFolder ? session.LastPath : null);

        // Restore is the saved state coming back, not a change worth
        // saving: whatever armed the debounce on the way is disarmed here,
        // in one place, instead of a flag checked in some of the callers.
        _stateSaveTimer.Stop();
    }


    /// <summary>
    /// The first navigation of the session: the remembered folder when it
    /// is still there, the first drive otherwise. Whether it is still there
    /// is asked on the pool - that one <c>DirectoryExists</c> used to sit on
    /// the UI thread before the first frame, and a session closed on a
    /// drive that has since spun down or been unplugged made the next start
    /// wait for it. A user who went somewhere themselves while the disk was
    /// thinking is left where they went.
    /// </summary>
    private async Task OpenStartFolderAsync(NavigationStop? remembered) {
        bool restore = remembered is not null
            && await Task.Run(() => _fs.DirectoryExists(remembered.Path));
        if (_nav.Current is not null) {
            return;
        }

        if (restore) {
            _nav.NavigateTo(remembered!.Path, remembered.Source);
        } else if (Trees.Roots.FirstOrDefault()?.FullPath is { } first) {
            _nav.NavigateTo(first, NavigationSource.External);
        }
        // The initial navigation ends in SaveState like any other, and the
        // restored folder is not a change worth writing back.
        _stateSaveTimer.Stop();
    }

    /// <summary>
    /// Asks for the session state to be written once the current burst of
    /// changes is over. Every navigation, expansion and pane resize calls
    /// this; the actual write happens in <see cref="WriteStateNow"/> when
    /// the timer runs out.
    /// </summary>
    private void SaveState() {
        if (Bookmarks.IsBuilding) {
            return;
        }

        _stateSaveTimer.Stop();
        _stateSaveTimer.Start();
    }


    private void WriteStateNow() {
        // Read-modify-write: AppState also carries Window geometry (saved
        // by MainWindow.xaml.cs) and Settings (edited via the dialog). If
        // we replaced the whole record here we'd silently wipe those on
        // every navigation/preview toggle.
        var current = _stateStore.Load();
        _stateStore.Save(current with {
            Session = new SessionState {
                LastPath = _nav.Current is not null
                    ? new NavigationStop(_nav.Current, _nav.CurrentSource ?? NavigationSource.External)
                    : null,
                ViewMode = _userViewMode.ToString(),
                ManualViewModes = _manualViewModeOrder
                    .Where(_manualViewModes.ContainsKey)
                    .Select(p => new FolderViewMode(p, _manualViewModes[p].ToString()))
                    .ToArray(),
                ExpandedPaths = Trees.CollectExpanded(),
                IsPreviewVisible = _isPreviewVisible,
                PreviewWidth = _previewWidth,
                BookmarksHeight = _bookmarksHeight,
                LayoutWindowWidth = _windowWidth,
                LayoutWindowHeight = _windowHeight,
                IsBookmarksExpanded = _isBookmarksExpanded,
                RecentPaths = _nav.RecentPaths.ToArray(),
            },
            Favorites = Bookmarks.Paths.ToArray(),
            Settings = Settings.ToRecord(),
            LastRunVersion = Diagnostics.CrashReporter.AppVersion(),
        });
    }

    /// <summary>
    /// Wipes the thumbnail cache when the build that wrote <c>state.json</c>
    /// is not this one. See <see cref="AppState.LastRunVersion"/> for why:
    /// nothing in a thumbnail's key says which version drew it, so a decoding
    /// fix would otherwise never reach the pictures already on disk.
    ///
    /// <para>
    /// Off the UI thread — clearing is thousands of file deletions — and
    /// entirely best-effort: a cache that will not clear costs stale
    /// thumbnails, never a failed start.
    /// </para>
    /// </summary>
    private void DropThumbnailCacheOnUpgrade(string lastVersion) {
        string current = Diagnostics.CrashReporter.AppVersion();
        if (string.Equals(lastVersion, current, StringComparison.Ordinal)) {
            return;
        }

        _log.Info($"Version changed ('{lastVersion}' -> '{current}'), dropping the thumbnail cache");
        var icons = ServiceLocator.Get<IIconProvider>();

        _ = Task.Run(() => {
            try {
                icons.ClearCache();
            } catch (Exception ex) {
                _log.Warn($"Thumbnail cache drop failed: {ex.Message}");
            }
        });
    }


    // --- Navigation glue -----------------------------------------------

    private void OnNavigationChanged() {
        // The rows on screen still belong to the folder being left, so this
        // is the last moment its selection can be noted — and the last
        // moment the folder we came out of is known. The session notes it,
        // drops an intent this navigation overtook, and plans the default
        // arrival (up highlights the folder we came out of; otherwise
        // whatever was selected there last time).
        _session.OnNavigating(_nav.Current, _selectedEntry?.FullPath);

        // The focus rectangle belongs to the folder being left. The arrival
        // puts it wherever the selection lands.
        CaretPath = null;

        // What kind of place this is, asked once and read everywhere below:
        // the refresh picks its back end by it, and every command's
        // CanExecute gates on it.
        NoteCurrentLocation();

        // Drop any active filter when the user moves to a new folder — the
        // filter is scoped to "the folder I'm looking at right now".
        // SearchController.Reset cancels any in-flight pass; the upcoming
        // Refresh → SetSource will reapply the (now empty) query.
        _search.Reset();
        // Same rule one level up, and it has to run every time rather than
        // only when results are on screen: the box holds its own copy of
        // the criteria now, so clearing just the filter behind it left the
        // box claiming a filter the list was not applying. Flags included —
        // see ContentSearchController.Reset.
        // Results are dropped before the reset, not after: the reset
        // raises IsShowingResults, whose handler re-lists the folder when
        // it still sees rows — and the Refresh below would then be the
        // second listing of the same folder in one navigation.
        SearchResults.Clear();
        ContentSearch.Reset();

        // Every step below runs on the dispatcher before the new folder can
        // be drawn, and some still touch the disk — the tree enumerates
        // children as it expands. Measured separately because "opening
        // a folder is slow" has to become "this part of opening a folder is
        // slow" before it can be fixed; see PerfLog in the session log.
        using (PerfLog.Measure("nav.refresh")) {
            Refresh();
        }
        ContentSearch.NoteRootChanged();
        using (PerfLog.Measure("nav.trees")) {
            ExpandCurrentInTrees();
        }
        using (PerfLog.Measure("nav.preview")) {
            Preview.SetCurrentFolder(_nav.Current, WindowTitle);
        }
        using (PerfLog.Measure("nav.watch")) {
            UpdateFolderWatch();
        }
        using (PerfLog.Measure("nav.state")) {
            SaveState();
        }
    }


    // --- Auto-refresh ----------------------------------------------------

    /// <summary>
    /// Points the watcher at the folder on screen. Shell namespaces (the
    /// Recycle Bin) are not real directories and are simply left unwatched —
    /// there is nothing to hand <c>FileSystemWatcher</c>.
    /// </summary>
    private void UpdateFolderWatch() {
        if (_watcher is null) {
            return;
        }

        bool watchable = Settings.AutoRefresh && _nav.Current is not null && !IsCurrentShellNamespace;
        _watcher.Watch(watchable ? _nav.Current : null);

        if (!watchable) {
            _session.ForgetPendingChanges();
            _watchTimer?.Stop();
        }
    }

    private void NoteFolderChanged(DirectoryChange change) {
        _session.NoteChange(change);
        if (_watchTimer is { IsEnabled: false }) {
            _watchTimer.Start();
        }
    }

    /// <summary>
    /// The throttle. A repeating timer rather than a one-shot restarted on
    /// every event: a folder receiving a steady stream of changes (an archive
    /// being unpacked into it) would restart a one-shot for as long as the
    /// stream lasts and never actually refresh. This way the listing is at
    /// most one interval behind, whatever is happening. The timer stops
    /// itself on the first idle tick, so a quiet folder costs nothing.
    /// What to do about the collected changes is the session's decision;
    /// this handler only carries it out.
    /// </summary>
    private void OnWatchTick(object? sender, EventArgs e) {
        var decision = _session.DecideWatchTick(
            busy: RenamingPath is not null || HasActiveOperations,
            rows: _search.Source);

        // Before anything is re-read: what the caches hold about these files
        // is a picture of the file as it was. A re-listing does not fix it —
        // the thumbnail caches are keyed by path, and the path is what did
        // not change when the file behind it was replaced.
        if (decision.Stale is { Count: > 0 } stale) {
            foreach (string path in stale) {
                AsyncIcon.Invalidate(path);
            }
        }

        switch (decision.Outcome) {
            case WatchOutcome.Idle:
                _watchTimer?.Stop();
                break;

            case WatchOutcome.Hold:
                break;

            case WatchOutcome.Relist:
                Refresh();
                // Subfolders are rows in the panels as well as in the list,
                // and the composition that changed is theirs too.
                if (decision.RefreshTrees && _nav.Current is { Length: > 0 } here) {
                    Trees.RefreshFor(here);
                }
                break;

            case WatchOutcome.RefreshRows:
                _ = Ratings.RefreshRowsAsync(decision.Rows!.Select(r => r.FullPath).ToArray());
                break;
        }
    }


    // --- Folder panels ---------------------------------------------------
    // The panels themselves are FolderTreesController's. These two say
    // *which* folder to open them to, which is the only part that needs to
    // know where the user is standing.


    /// <summary>
    /// Opens one named panel down to the folder on screen and puts the
    /// highlight there — what <c>Ctrl+2</c> and <c>Ctrl+Shift+E</c> point
    /// the keyboard at. False when the folder is not reachable in that
    /// panel.
    /// </summary>
    public bool RevealCurrentIn(NavigationSource panel) {
        return _nav.Current is { } here && Trees.RevealIn(panel, here);
    }


    private void ExpandCurrentInTrees() {
        if (_nav.Current is { } here) {
            Trees.ExpandTo(here, _nav.CurrentSource ?? NavigationSource.External);
        }
    }


    // --- Listing --------------------------------------------------------
    private void Refresh() {
        // Search results are not a folder listing, and re-listing would
        // replace them with the folder underneath. Leaving results is an
        // explicit act — clearing the box, or navigating — so a refresh
        // here does the one thing it still can honestly do: drop the rows
        // whose files are gone. That is what makes a delete or a rename
        // done on a result actually leave the list.
        if (ContentSearch.IsShowingResults) {
            _ = SearchResults.PruneMissingAsync();
            // Results are a list of their own, not a folder listing — the
            // "this folder is gone" panel has nothing to sit on top of.
            SetMissingFolder(null);

            return;
        }

        // A rebuild of the list drops the row the editor was sitting on, so
        // the editor goes with it.
        RenamingPath = null;

        // Default intent: whatever is selected now stays selected once the
        // new listing lands. Callers that know better (rename, undo, a click
        // in the tree) have already filled this in.
        // Nothing selected means nothing to put back, and an intent that
        // asks for nothing would only stop ReconcileEntries doing its own
        // job below.
        if (_session.Arrival is null && _nav.Current is { } staying && _selectedEntries.Count > 0) {
            _session.SetArrival(
                ArrivalIntent.Rows(staying, _selectedEntries.Select(e => e.FullPath).ToArray()));
        }

        // Any in-flight shell enumeration from a previous navigation is
        // stale now — cancel it so its delayed SetSource doesn't clobber
        // the new folder's entries. The cancel also drops the spinner if
        // we're switching from a shell namespace to a real filesystem path.
        _listLoadCts?.Cancel();
        if (IsListLoading) {
            IsListLoading = false;
        }

        if (_nav.Current is null) {
            _hiddenCount = 0;
            _session.NoteListingGone();
            _search.SetSource(Array.Empty<FileSystemEntry>());
            Entries.Clear();
            Status = "";
            SetMissingFolder(null);
            return;
        }

        // Shell namespaces (the Recycle Bin, an archive) route through
        // IShellNamespace. Enumeration goes through COM, which can take
        // hundreds of ms with many recycled items or a large archive, so we
        // hand it off to Task.Run and show a spinner via IsListLoading until
        // it returns. No Hidden/System filtering: shell items don't carry
        // those flags and Wander's "what to hide" preference is
        // filesystem-only.
        if (IsCurrentShellNamespace && TryGetShellNamespace() is { } ns) {
            _ = RefreshShellAsync(ns, _nav.Current);
            return;
        }

        // Settings are read here, on the UI thread, and carried into the
        // worker as values — the background pass must not race the settings
        // dialog.
        var sort = new SortOptions(Settings.SortKey, Settings.SortAscending, Settings.GroupFoldersFirst);
        _ = RefreshFolderAsync(_nav.Current, Settings.Visibility, sort, Settings.IntegrateCompanions);
    }

    /// <summary>
    /// Off-UI-thread folder enumeration, mirroring
    /// <see cref="RefreshShellAsync"/>. A local folder is usually listed in
    /// a few milliseconds, but a network share, a sleeping drive or a
    /// directory with tens of thousands of entries is not — and blocking
    /// the dispatcher there froze the whole window.
    /// </summary>
    private async Task RefreshFolderAsync(string path, EntryVisibility visibility, SortOptions sort, bool integrate) {
        _listLoadCts?.Cancel();
        _listLoadCts = new CancellationTokenSource();
        var token = _listLoadCts.Token;

        // "Arriving" as opposed to re-listing what is already on screen —
        // the view mode is chosen for a folder the user walks into, not
        // every time F5 or a rename re-reads the one they are standing in.
        int epoch = _session.BeginListing(path, out bool arriving);
        string statusBeforeLoad = Status;
        var started = System.Diagnostics.Stopwatch.StartNew();
        var spinnerDelay = Task.Delay(SpinnerDelayMs);

        var work = Task.Run(() => {
            var items = new List<FileSystemEntry>();
            int hidden = 0;

            // Timed separately from the fold below it: "the folder was slow
            // to open" has two quite different answers — the disk was slow
            // to list it, or we were slow to arrange what it listed — and a
            // single figure cannot tell them apart.
            using (PerfLog.Measure("bg.enumerate")) {
                foreach (var e in _fs.Enumerate(path, sort)) {
                    token.ThrowIfCancellationRequested();
                    if (!visibility.Allows(e)) {
                        hidden++;
                        continue;
                    }
                    items.Add(e);
                }
            }

            // Folding companions happens after the visibility filters, so a
            // sidecar next to a main file the user chose not to see stays
            // visible on its own rather than disappearing with it.
            if (!integrate) {
                return (Items: (IReadOnlyList<FileSystemEntry>)items, Hidden: hidden);
            }

            using (PerfLog.Measure("bg.companions")) {
                return (Items: _companions.Collapse(items), Hidden: hidden);
            }
        }, token);

        // An arrival clears the rows of the folder being left: rows that
        // look like the new folder and are the old one invite a click on a
        // file that is not there, and read as "the folder has not opened
        // yet" (decision 2026-09-01). Tearing their containers down is the
        // one expensive UI moment of a navigation (50-100 ms in a folder
        // with thumbnails), so it does not run inside the click that
        // navigated: it steps below input priority, after the address bar
        // and the panels have drawn the move. A local folder is usually
        // listed by then, and its landing below - queued at the same
        // priority, so never ahead of this - replaces the rows in one
        // swap instead of a clear and a fill.
        if (arriving) {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (token.IsCancellationRequested) {
                return;
            }
            if (!work.IsCompleted) {
                _hiddenCount = 0;
                _search.SetSource(Array.Empty<FileSystemEntry>());
            }
        }

        // The spinner is a dimming overlay — raising it for the two frames a
        // local folder takes would make every navigation flash. Only slow
        // folders get it; the stale rows are already gone (see above).
        if (await Task.WhenAny(work, spinnerDelay) != work && !token.IsCancellationRequested) {
            IsListLoading = true;
        }

        try {
            var (items, hidden) = await work;
            if (token.IsCancellationRequested) {
                return;
            }

            // Landing the rows is the expensive UI moment of a navigation:
            // one Reset, a teardown of the old containers and a realise of
            // the new ones. It steps below input priority so that whatever
            // the user does next — a key, a click, the next navigation —
            // is served first; a landing made stale while yielding is
            // dropped by the token here and the epoch check in PublishRows.
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (token.IsCancellationRequested) {
                return;
            }

            _hiddenCount = hidden;
            _session.NoteListed(path);
            SetMissingFolder(null);
            if (arriving) {
                // The journal's backbone: "where was I when this happened".
                // Only on arrival — an F5 or a re-read after an operation is
                // the same folder, and a journal that repeated it would bury
                // the operations between them.
                Journal.Note(string.Format(Strings.JournalOpenedFolder, path), DateTime.Now);
                using (PerfLog.Measure("ui.autoview")) {
                    AutoSelectViewMode(items, path);
                }
            }
            // One line per slow arrival, with what it cost and how much
            // there was — enough to tell "a folder of forty thousand files"
            // from "a folder of forty that took two seconds", which is the
            // difference between expected and a bug.
            if (started.Elapsed.TotalMilliseconds >= SlowFolderMs) {
                _log.Info(
                    $"Folder listed in {started.ElapsedMilliseconds} ms: {items.Count} shown, " +
                    $"{hidden} hidden — {path}");
            }
            // A file operation may have reported its outcome ("Copied 3
            // items") while we were enumerating; the listing's own
            // "N items" must not eat that message.
            string reported = Status;
            PublishRows(epoch, items);
            if (reported != statusBeforeLoad) {
                Status = reported;
            }
            if (arriving && _session.IsCurrent(epoch)) {
                // The clock keeps running: the view arms FirstScreenWatch
                // on it once the rows have been laid out, and the line in
                // the log counts from the navigation, not from the landing.
                FolderArrived?.Invoke(path, started);
            }
            Ratings.StartPass(items, path, sort, epoch, arriving);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) when (ex is DirectoryNotFoundException or DriveNotFoundException) {
            // Not an error to report in the status bar and forget: the
            // folder is gone, and the file area says so — with the way out
            // when the path came from a bookmark.
            _log.Info($"Folder is gone: {path}");
            _session.NoteListingGone();
            _search.SetSource(Array.Empty<FileSystemEntry>());
            SetMissingFolder(path);
        } catch (Exception ex) {
            _log.Error($"Enumerate failed: {path}", ex);
            _session.NoteListingGone();
            Entries.Clear();
            Status = string.Format(Strings.StatusError, ex.Message);
        } finally {
            // Same handoff rule as RefreshShellAsync: a superseded load
            // leaves the flag for the load that replaced it.
            if (!token.IsCancellationRequested) {
                IsListLoading = false;
            }
        }
    }

    /// <summary>
    /// Off-UI-thread enumeration of a shell namespace, with cancellation
    /// when navigation moves on before the COM call returns. The spinner
    /// (<see cref="IsListLoading"/>) is held until the *winning* load
    /// finishes — superseded loads return early without clearing the flag
    /// so the new load owns it seamlessly.
    /// </summary>
    private async Task RefreshShellAsync(IShellNamespace ns, string shellPath) {
        _listLoadCts?.Cancel();
        _listLoadCts = new CancellationTokenSource();
        var token = _listLoadCts.Token;

        IsListLoading = true;
        _hiddenCount = 0;
        var started = System.Diagnostics.Stopwatch.StartNew();
        // Same rule as the filesystem path: clear when arriving from
        // somewhere else, reconcile in place when re-listing what is
        // already on screen.
        int epoch = _session.BeginListing(shellPath, out bool arriving);
        if (arriving) {
            _search.SetSource(Array.Empty<FileSystemEntry>());
        }

        // Read on the UI thread and carried in, like the filesystem branch:
        // the bin sorts itself (newest deletion first), an archive comes
        // back in whatever order the container holds it and is sorted here
        // by the same rules the user set for every other folder.
        var sort = new SortOptions(Settings.SortKey, Settings.SortAscending, Settings.GroupFoldersFirst);
        var archive = CurrentArchive;

        try {
            IReadOnlyList<FileSystemEntry> items;
            try {
                items = await Task.Run(() => {
                    var listed = ns.Enumerate(shellPath);

                    return archive is null ? listed : EntryComparers.Sort(listed, sort);
                }, token);
            } catch (OperationCanceledException) {
                return;
            } catch (Exception ex) {
                _log.Error($"Shell enumerate failed: {shellPath}", ex);
                Status = archive is null
                    ? string.Format(Strings.StatusError, ex.Message)
                    : string.Format(Strings.StatusArchiveUnreadable, archive.ArchiveName);
                return;
            }

            if (token.IsCancellationRequested) {
                return;
            }
            _session.NoteListed(shellPath);
            SetMissingFolder(null);
            if (arriving) {
                // An archive and the Recycle Bin are folders to the person
                // opening them, so they belong in the journal the same way.
                Journal.Note(string.Format(Strings.JournalOpenedFolder, shellPath), DateTime.Now);
            }
            // No sidecars in a shell namespace, and no picture-folder
            // guessing either: the Recycle Bin is a list of things to
            // decide about, not a folder to look at.
            Ratings.Cancel();
            HasRatings = false;
            PublishRows(epoch, items.ToList());

            // Timed like any other folder: an archive is one to the person
            // opening it, and "how long until I can see it" is the same
            // question there as on disk.
            if (arriving && _session.IsCurrent(epoch)) {
                FolderArrived?.Invoke(shellPath, started);
            }

            // An archive that lists nothing is either empty or encrypted
            // whole - 7z with -mhe hides even the names, and the two are
            // indistinguishable from here. Saying both beats an empty list
            // that looks like a mistake.
            if (archive is not null && items.Count == 0) {
                Status = string.Format(Strings.StatusArchiveEmptyOrLocked, archive.ArchiveName);
            }
        } finally {
            // Only release the spinner if our load is still the active one.
            // A superseded load (token cancelled) leaves IsListLoading=true
            // so the next RefreshShellAsync inherits it without flicker.
            if (!token.IsCancellationRequested) {
                IsListLoading = false;
            }
        }
    }

    /// <summary>
    /// Reconciles <see cref="Entries"/> with a fresh listing instead of
    /// clearing and refilling it. Rows that did not change keep their
    /// containers — that is what stops the list blinking on every refresh
    /// and what lets the selection survive a rename or a delete.
    /// </summary>
    /// <summary>
    /// The one way a computed set of rows reaches the list.
    ///
    /// <para>
    /// Every producer runs off the UI thread and can finish after the folder
    /// it was reading has been left, refreshed or replaced by search results.
    /// Each used to answer "is this still mine?" its own way — a cancellation
    /// token here, a path comparison there — and each new pass arrived with a
    /// new variation, including one that compared paths and so could not tell
    /// "the same folder" from "the same folder, listed again". They all carry
    /// the epoch they were computed for instead, and it is checked here.
    /// </para>
    /// </summary>
    private void PublishRows(int epoch, IReadOnlyList<FileSystemEntry> items) {
        if (!_session.IsCurrent(epoch)) {
            return;
        }

        // A file can have been rewritten while this folder was not on
        // screen, and nothing watched it happen. The listing just read
        // every file's stamp; the thumbnail caches are keyed by path alone
        // and would go on showing the old picture, so they are told here.
        AsyncIcon.DropStale(items);
        _search.SetSource(items);
    }


    private void SyncEntries(IReadOnlyList<FileSystemEntry> items) {
        // The moment a folder's listing lands on the UI thread — the one
        // hitch a person notices when opening a folder.
        using var applying = PerfLog.Measure("list.apply");

        // What to do lives in Core (ListingDiff), where tests reach it;
        // this method only replays the answer against the bound collection.
        var plan = ListingDiff.Compute(Entries, items);
        if (plan.Wholesale) {
            // One notification, not one per file. Filling item by item made
            // the tile panel re-measure five thousand times against a list
            // that was still growing — see BulkObservableCollection.
            Entries.ReplaceAll(items);

            return;
        }

        foreach (var edit in plan.Edits) {
            switch (edit.Kind) {
                case ListingEditKind.RemoveAt:
                    Entries.RemoveAt(edit.Index);
                    break;
                case ListingEditKind.Insert:
                    Entries.Insert(edit.Index, edit.Entry!);
                    break;
                case ListingEditKind.Move:
                    Entries.Move(edit.Index, edit.ToIndex);
                    break;
                case ListingEditKind.Replace:
                    Entries[edit.Index] = edit.Entry!;
                    break;
            }
        }
    }

    private static bool IsSamePath(string? a, string? b) {
        return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Does whatever the pending <see cref="ArrivalIntent"/> asked for, now
    /// that a listing has landed. What that is — including whether the
    /// intent keeps waiting for its own folder's listing — is the session's
    /// decision; this method only carries it out against the view.
    /// </summary>
    private void ApplyArrival() {
        var decision = _session.DecideArrival(_nav.Current, Entries);

        switch (decision.Outcome) {
            case ArrivalOutcome.None:
                return;

            case ArrivalOutcome.SelectFolder:
                SelectExternalPath(decision.FolderPath!);
                return;

            case ArrivalOutcome.NothingFound:
                // Nothing to land on — and nothing for the list to take the
                // keyboard back onto, so the request does not carry over to
                // whichever restore happens next.
                FocusListAfterRestore = false;
                return;

            case ArrivalOutcome.SelectRows:
                var found = decision.Rows;
                // Added to, never overwritten. The flag also carries a
                // request that nobody in this listing made — an operation
                // that ran behind a modal dialog, a view mode that swapped
                // the control out from under the keyboard — and clearing it
                // here is what left the file area without focus after
                // walking into a gallery folder, so that the next Backspace
                // went to whatever had the keyboard instead.
                FocusListAfterRestore |= decision.TakeFocus;
                SelectedEntry = found[0];
                SelectedEntries = found;
                SelectionRestoreRequested?.Invoke(found);

                if (decision.RenameTarget is not null) {
                    InlineRenameRequested?.Invoke(found[0]);
                }
                return;
        }
    }

    // --- Ratings and the rating filter ---------------------------------

    /// <summary>
    /// True when something in the folder on screen carries a rating. The
    /// filter bar and the Details rating column hang off this: neither has
    /// anything to say in a folder of source code, and a permanently empty
    /// star column would be one more thing to look past in every other
    /// folder.
    /// </summary>
    public bool HasRatings {
        get => _hasRatings;
        private set => SetField(ref _hasRatings, value);
    }

    /// <summary>
    /// The whole filter. The star row binds to it rather than to a number:
    /// which stars are lit is a set now, not a threshold, and only the
    /// filter itself knows it.
    /// </summary>
    public RatingFilter RatingFilter => _search.RatingFilter;

    /// <summary>
    /// Whether the crossed-out star is lit. A property of its own rather
    /// than a converter on the filter, because the star is drawn by a
    /// template trigger and a trigger needs something to compare.
    /// </summary>
    public bool FilterIncludesUnrated => _search.RatingFilter.HasRank(RatingFilter.Unrated);

    public bool HasRatingFilter => _search.HasRatingFilter;

    /// <summary>The five swatches of the filter bar. Their own instances — see <see cref="ColorLabelViewModel"/>.</summary>
    public IReadOnlyList<ColorLabelViewModel> FilterColorChoices { get; }

    public RelayCommand SetFilterRankCommand { get; }
    public RelayCommand SetRankForSelectionCommand { get; }
    public RelayCommand SetFilterColorCommand { get; }
    public RelayCommand ClearRatingFilterCommand { get; }


    /// <summary>
    /// Sets a rating on the current selection. The gallery's number keys go
    /// here; so does every star and swatch in the preview footer.
    /// </summary>
    public void SetRankForSelection(string? parameter) {
        if (!int.TryParse(parameter, out int rank) || rank < 0 || rank > Pp3Sidecar.MaxRank) {
            return;
        }

        var target = _selectedEntries.Count > 0
            ? _selectedEntries
            : _selectedEntry is { } single ? new[] { single } : Array.Empty<FileSystemEntry>();

        Ratings.Apply(target, RatingField.Rank, rank);
    }


    /// <summary>
    /// A click on one of the stars, with the gesture already read off the
    /// keyboard. Split from the command for one reason: the keyboard is the
    /// one thing an offscreen harness must not touch — synthesising a held
    /// <c>Ctrl</c> is real input on somebody's real machine — so what the
    /// click <em>does</em> has to be reachable without pretending to press
    /// anything. <paramref name="toggle"/> is the held <c>Ctrl</c>.
    /// </summary>
    public void ClickRankFilter(int rank, bool toggle) {
        _search.RatingFilter = toggle
            ? _search.RatingFilter.ToggleRank(rank)
            : _search.RatingFilter.PickRank(rank);
    }


    /// <summary>The same for a colour swatch — see <see cref="ClickRankFilter"/>.</summary>
    public void ClickColorFilter(int color, bool toggle) {
        _search.RatingFilter = toggle
            ? _search.RatingFilter.ToggleColor(color)
            : _search.RatingFilter.PickColor(color);
    }


    /// <summary>
    /// Swaps updated copies of rows into <see cref="Entries"/> without
    /// touching anything else, and puts the selection back on them.
    ///
    /// <para>
    /// A record cannot be edited in place, so the row has to be replaced —
    /// and the list drops a replaced object out of its selection on the way.
    /// <see cref="_rowsReplacing"/> holds the two properties that would
    /// otherwise broadcast that gap (the preview would clear and reload, the
    /// status bar would count down and back up) until the selection is
    /// restored.
    /// </para>
    /// </summary>
    private void ReplaceRows(IReadOnlyList<FileSystemEntry> changed) {
        ReconcileEntries(() => {
            foreach (var entry in changed) {
                for (int i = 0; i < Entries.Count; i++) {
                    if (IsSamePath(Entries[i].FullPath, entry.FullPath)) {
                        Entries[i] = entry;
                        break;
                    }
                }
            }
        });
    }


    /// <summary>
    /// Runs a rebuild of <see cref="Entries"/> with the selection held
    /// steady across it.
    ///
    /// <para>
    /// <b>Why this wraps every rebuild and not just some.</b> A
    /// <c>FileSystemEntry</c> is a record and cannot be edited in place, so
    /// any change to a row — a rating arriving, a size changing, a listing
    /// coming back from disk — replaces the object. The list drops a
    /// replaced object out of its selection, and unless somebody puts it
    /// back, the selection is gone. It used to be put back only when a
    /// caller had filled in an <see cref="ArrivalIntent"/>
    /// (navigation, rename, undo) — which meant the second rebuild of every
    /// listing, the one where the ratings arrive, silently dropped whatever
    /// the user had selected. That is the bug this closes, and it closes it
    /// for every future rebuild too rather than for the one that was
    /// noticed.
    /// </para>
    ///
    /// <para>
    /// When a caller <em>has</em> left an <see cref="ArrivalIntent"/>,
    /// this keeps out of the way: that caller knows something better than
    /// "whatever was selected before" — the new name after a rename, what
    /// an undo put back — and <see cref="ApplyArrival"/> runs right after
    /// with scrolling and focus, which is right there and wrong here.
    /// </para>
    /// </summary>
    private void ReconcileEntries(Action rebuild) {
        // A caller that left an intent knows something better than
        // "whatever was selected before" — the new name after a rename, what
        // an undo put back — and ApplyArrival runs right after with
        // scrolling and focus. We still put the view model back in step with
        // the rows below, so it never holds entries that have left the list;
        // we just do not tell the view where to go.
        bool ownsSelection = _session.Arrival is null;
        var keep = new HashSet<string>(
            _selectedEntries.Select(e => e.FullPath), StringComparer.OrdinalIgnoreCase);
        string? primary = _selectedEntry?.FullPath;

        FileSystemEntry[] found;
        FileSystemEntry? next = null;

        _rowsReplacing = true;
        try {
            rebuild();

            found = keep.Count == 0
                ? Array.Empty<FileSystemEntry>()
                : Entries.Where(e => keep.Contains(e.FullPath)).ToArray();

            if (found.Length > 0) {
                next = found.FirstOrDefault(e => IsSamePath(e.FullPath, primary)) ?? found[0];

                // Order matters, and it is not obvious: SelectedEntry is
                // bound to the list's SelectedItem, and assigning that
                // collapses an extended selection down to the single row.
                // So the primary goes back first and the rest of the set
                // after it — the other way round throws the set away again.
                _selectedEntry = next;
                Raise(nameof(SelectedEntry));

                if (ownsSelection) {
                    // No scrolling and no focus move: nothing happened that
                    // the user should be taken anywhere for. Raised inside
                    // the guard because the view restores the rows one at a
                    // time, and each step is another report of a selection
                    // that is still half-built.
                    SelectionRefreshRequested?.Invoke(found);
                }
            }
        } finally {
            _rowsReplacing = false;
        }

        if (keep.Count == 0) {
            return;
        }

        if (found.Length == 0) {
            // Everything that was selected has left the folder. Saying so is
            // the honest answer; the guard above only held the report back
            // while the rows were mid-flight.
            SelectedEntries = Array.Empty<FileSystemEntry>();
            SelectedEntry = null;

            return;
        }

        AdoptSelection(found, next!);
    }


    /// <summary>
    /// Takes the restored selection as the view model's own, without
    /// pushing any of it back at the list.
    ///
    /// <para>
    /// The fields are assigned directly rather than through the properties
    /// because the list is already showing exactly this selection — it was
    /// just put there — and going through <see cref="SelectedEntry"/> would
    /// send it round again through the binding that collapses an extended
    /// selection. Everyone who needs telling is told here instead.
    /// </para>
    /// </summary>
    private void AdoptSelection(IReadOnlyList<FileSystemEntry> selection, FileSystemEntry primary) {
        _selectedEntries = selection;
        _selectedEntry = primary;
        NoteSelectionKind();

        Raise(nameof(SelectedEntries));
        Raise(nameof(SelectedEntry));
        Raise(nameof(SelectionSummary));
        Preview.SetSelection(selection);
        Preview.SetPrimary(primary);
    }


    /// <summary>
    /// A star in the filter bar. A plain click picks it and everything above
    /// it — "three and up", the question you ask when deciding what to keep.
    /// <c>Ctrl</c> held adds or removes that one rank, which is how "three
    /// and up, but not five" gets said. The leftmost star is the crossed-out
    /// one and stands for unrated; a plain click there picks it alone,
    /// because "unrated and above" is every photograph in the folder.
    ///
    /// <para>
    /// <c>Alt</c> does nothing at all. It used to mean "exactly this rank",
    /// which the set of ranks says better and without a modifier nobody can
    /// see; leaving it inert is better than leaving it doing something the
    /// bar no longer has a way to show.
    /// </para>
    /// </summary>
    private void SetFilterRank(string? parameter) {
        if (int.TryParse(parameter, out int rank) && ReadFilterGesture() is { } toggle) {
            ClickRankFilter(rank, toggle);
        }
    }

    /// <summary>A swatch in the filter bar. Same two gestures as the stars.</summary>
    private void SetFilterColor(string? parameter) {
        if (int.TryParse(parameter, out int color) && ReadFilterGesture() is { } toggle) {
            ClickColorFilter(color, toggle);
        }
    }


    /// <summary>
    /// Which gesture the modifiers make this click: null for "none, ignore
    /// it", false for a plain click, true for the toggling one.
    /// </summary>
    private static bool? ReadFilterGesture() {
        var mods = Keyboard.Modifiers;

        return mods.HasFlag(ModifierKeys.Alt) ? null : mods.HasFlag(ModifierKeys.Control);
    }


    private void ClearRatingFilter() {
        _search.RatingFilter = RatingFilter.None;
    }

    private void SyncFilterChoices() {
        foreach (var choice in FilterColorChoices) {
            choice.IsSelected = _search.RatingFilter.HasColor(choice.Index);
        }
    }


    /// <summary>
    /// The status line, without a journal entry. For the messages that
    /// describe what the list <em>is</em> rather than report an event —
    /// see <see cref="Journal"/>.
    /// </summary>
    private void SetStatusQuietly(string text) {
        SetField(ref _status, text, nameof(Status));
    }


    /// <summary>
    /// The count under the list. Written past the journal
    /// (<see cref="SetStatusQuietly"/>): it is not something that happened,
    /// it is what the list is, and it is rewritten on every filter
    /// keystroke and every landing. In the journal it drowned the lines
    /// that matter — the journal says which folder was opened, and the
    /// count of what is in it belongs to the folder, not to a moment.
    /// </summary>
    private void UpdateFilterStatus(int shown, int total) {
        if (_search.HasRatingFilter && !_search.HasQuery) {
            SetStatusQuietly(string.Format(Strings.StatusRatingFilterMatches, shown, total));
        } else if (_search.HasQuery) {
            SetStatusQuietly(total > 0
                ? string.Format(Strings.StatusFilterMatches, shown, total, _search.Query)
                : string.Format(Strings.StatusItems, shown));
        } else if (_hiddenCount > 0) {
            SetStatusQuietly(string.Format(Strings.StatusItemsWithHidden, shown, _hiddenCount));
        } else {
            SetStatusQuietly(string.Format(Strings.StatusItems, shown));
        }
    }

    // --- Search ---------------------------------------------------------
    //
    // Two interactions share the box above the list. The shallow one is the
    // live name filter that has always been there and is handled by
    // SearchController on every keystroke. The deep one — subfolders, file
    // contents, the system index — runs on Enter, replaces the listing with
    // its results, and is everything below.

    /// <summary>
    /// F5 while results are on screen. Re-running the search is what
    /// "refresh" means there — re-listing the folder underneath would throw
    /// the results away, which is the opposite of what the key is for.
    /// </summary>
    private void RefreshOrRerunSearch() {
        // The panels are part of "what is on screen": a folder expanded
        // there caches its subfolders from the moment it was opened, and
        // nothing else re-reads them. F5 is where the whole window catches
        // up with the disk, not just the middle of it.
        Trees.RefreshAll();

        if (ContentSearch.IsShowingResults || ContentSearch.IsDeep) {
            ContentSearch.Rerun();

            return;
        }

        Refresh();
    }


    /// <summary>
    /// A pass is starting. The folder listing and the rating pass are both
    /// about to be replaced on screen; leaving them running would only let
    /// a late arrival overwrite the results.
    /// </summary>
    private void BeginSearchResults() {
        // Results are a different listing, not this folder's. Bumping the
        // epoch is what drops a folder read or a rating pass that is still
        // in flight for the folder underneath.
        _listLoadCts?.Cancel();
        _session.InvalidateListings();
        Ratings.Cancel();
        IsListLoading = false;
        RenamingPath = null;

        SearchResults.Begin(_nav.Current, Entries);
    }


    /// <summary>
    /// Empties both fields and puts the folder back. Bound to the box's own
    /// Esc, to the clear button and to the search window's Esc.
    /// </summary>
    private void ClearSearch() {
        bool hadResults = ContentSearch.IsShowingResults;
        ContentSearch.Clear();
        if (!hadResults) {
            return;
        }

        SearchResults.Clear();
        Refresh();
    }


    /// <summary>
    /// Points the live name filter at the mask, or takes it off. Only the
    /// shallow case filters live: once contents or a wider scope are in
    /// play the folder on screen is not the answer to anything, and
    /// narrowing it would be a second, contradictory result on the same
    /// screen.
    /// </summary>
    private void SyncLiveFilter() {
        // The live filter only ever gets the name half: the box may read
        // "*.cs:budget", but a filter over the folder on screen has no way
        // to honour the second half, and pretending otherwise would narrow
        // the list by a rule it is not applying.
        _search.Query = ContentSearch.IsDeep ? "" : ContentSearch.NameQuery;
        Raise(nameof(SearchQuery));
        Raise(nameof(HasSearchQuery));
    }


    private void OnContentSearchChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(ContentSearchController.IsShowingResults):
                Raise(nameof(IsSearchResults));
                if (!ContentSearch.IsShowingResults && SearchResults.Count > 0) {
                    // Something dropped results without going through
                    // ClearSearch — a criterion falling back to the shallow
                    // kind, for instance. The list has to follow.
                    SearchResults.Clear();

                    // The folder was never thrown away: the search only took
                    // the list over, and the listing it borrowed is still in
                    // hand. Re-projecting it puts the folder back without
                    // touching the disk — which matters now that every word
                    // typed in the quick filter passes through here.
                    if (IsSamePath(_session.ListedPath, _nav.Current)) {
                        _search.SetSource(_search.Source);
                    } else {
                        Refresh();
                    }
                }
                break;

            case nameof(ContentSearchController.FilterText):
                Raise(nameof(SearchQuery));
                Raise(nameof(HasSearchQuery));
                break;
        }
    }


    // --- View modes ----------------------------------------------------

    /// <summary>
    /// Ctrl + wheel over the file list: makes the current view bigger or
    /// smaller by <paramref name="steps"/> notches.
    ///
    /// <para>
    /// It writes the same settings the dialog edits — there is no separate
    /// "zoom level" to fall out of step with them, and the size the user
    /// scrolled to is the size that persists. Each view is stepped by what
    /// actually reads as "bigger" in it: the row and its icon in the table,
    /// the icon in the tiles, and in the icon grid the picture together with
    /// the cell around it, so the proportions the user chose survive the
    /// zoom instead of the tiles drifting apart or crowding together.
    /// </para>
    /// </summary>
    public void ZoomList(int steps) {
        if (steps == 0) {
            return;
        }

        switch (ViewMode) {
            case ViewMode.Details:
                Settings.DetailsRowHeight += 2 * steps;
                Settings.DetailsIconSize += 2 * steps;
                break;

            case ViewMode.Tiles:
                Settings.TileIconSize += 4 * steps;
                break;

            case ViewMode.LargeIcons:
                // The cell follows the picture keeping the **gap** it had,
                // not the ratio. Scaling proportionally is the obvious thing
                // and it is wrong: at twice the icon the air around it also
                // doubles, and a grid of large photographs ends up mostly
                // empty space. What the user chose when they set these two
                // numbers is how much room there is around the picture.
                int gap = Settings.LargeIconCellWidth - Settings.LargeIconImageSize;
                Settings.LargeIconImageSize += 8 * steps;
                Settings.LargeIconCellWidth = Settings.LargeIconImageSize + gap;
                break;

            case ViewMode.Gallery:
                // Same rule as LargeIcons, in bigger steps: the gallery
                // starts where that view ends, and 8 px a notch would make
                // getting from 200 to 400 a wrist exercise.
                int galleryGap = Settings.GalleryCellWidth - Settings.GalleryImageSize;
                Settings.GalleryImageSize += 16 * steps;
                Settings.GalleryCellWidth = Settings.GalleryImageSize + galleryGap;
                break;
        }

        ReportViewSize();
    }


    /// <summary>
    /// Back to the size this view ships with — <c>Ctrl</c> + the wheel
    /// pressed, in the same list the wheel resizes. There is no other way
    /// home once the wheel has been turned: the numbers are settings, not a
    /// zoom level with a neutral position, and hunting for "96" by ear is
    /// not a thing anyone should have to do.
    /// </summary>
    public void ResetListSize() {
        var defaults = new AppSettings();
        switch (ViewMode) {
            case ViewMode.Details:
                Settings.DetailsRowHeight = defaults.DetailsRowHeight;
                Settings.DetailsIconSize = defaults.DetailsIconSize;
                break;

            case ViewMode.Tiles:
                Settings.TileCellWidth = defaults.TileCellWidth;
                Settings.TileIconSize = defaults.TileIconSize;
                Settings.TileLabelFontSize = defaults.TileLabelFontSize;
                break;

            case ViewMode.LargeIcons:
                Settings.LargeIconCellWidth = defaults.LargeIconCellWidth;
                Settings.LargeIconImageSize = defaults.LargeIconImageSize;
                Settings.LargeIconMargin = defaults.LargeIconMargin;
                Settings.LargeIconLabelFontSize = defaults.LargeIconLabelFontSize;
                break;

            case ViewMode.Gallery:
                Settings.GalleryCellWidth = defaults.GalleryCellWidth;
                Settings.GalleryImageSize = defaults.GalleryImageSize;
                Settings.GalleryMargin = defaults.GalleryMargin;
                Settings.GalleryLabelFontSize = defaults.GalleryLabelFontSize;
                break;
        }

        ReportViewSize();
    }


    /// <summary>
    /// The user picking a view, as opposed to Wander picking one. Both
    /// halves matter: the choice becomes the one that persists, and the
    /// folder it was made in is marked as spoken for, so the gallery does
    /// not switch itself back on the next time the user walks in.
    /// </summary>
    private void SetViewMode(string? name) {
        if (!Enum.TryParse<ViewMode>(name, out var mode)) {
            return;
        }

        _userViewMode = mode;
        ViewMode = mode;
        if (_nav.Current is { Length: > 0 } here) {
            RememberManualViewMode(here, mode);
        }
        SaveState();
    }


    private void RememberManualViewMode(string path, ViewMode mode) {
        if (!_manualViewModes.ContainsKey(path)) {
            _manualViewModeOrder.Enqueue(path);
        }
        _manualViewModes[path] = mode;

        while (_manualViewModeOrder.Count > ManualViewModeLimit) {
            _manualViewModes.Remove(_manualViewModeOrder.Dequeue());
        }
    }


    private void SetGalleryBackground(string? name) {
        if (Enum.TryParse<GalleryBackground>(name, out var background)) {
            Settings.GalleryBackground = background;
        }
    }


    /// <summary>
    /// Picks the view for a folder we have just arrived in: the gallery
    /// when the folder is mostly pictures, otherwise whatever the user
    /// chose last.
    ///
    /// <para>
    /// The second half is as important as the first. Without it the gallery
    /// would be sticky — one photo folder and every text folder afterwards
    /// is a wall of generic icons — so leaving a folder of photographs puts
    /// the user's own view back.
    /// </para>
    ///
    /// <para>
    /// Silent in a folder where the user has chosen a view by hand this
    /// session, and silent altogether when the setting is off.
    /// </para>
    /// </summary>
    private void AutoSelectViewMode(IReadOnlyList<FileSystemEntry> items, string path) {
        // A folder the user has assigned a view to keeps it, and keeps it
        // across restarts. That is what "the automation stays out of this
        // folder" has to mean to be worth saying.
        if (_manualViewModes.TryGetValue(path, out var chosen)) {
            ViewMode = chosen;

            return;
        }
        if (!Settings.AutoGallery) {
            return;
        }

        ViewMode = ImageFolderProbe.IsImageFolder(items, _companions, Settings.AutoGalleryPercent)
            ? ViewMode.Gallery
            : _userViewMode;
    }


    /// <summary>
    /// Says in the status bar what size the view is now, and how to get the
    /// default back. Without it the wheel changes something with no number
    /// attached to it and no way back — the two complaints this answers.
    /// </summary>
    private void ReportViewSize() {
        var defaults = new AppSettings();
        (string name, int now, int standard) = ViewMode switch {
            ViewMode.Details => (Strings.MenuViewDetails, Settings.DetailsRowHeight, defaults.DetailsRowHeight),
            ViewMode.Tiles => (Strings.MenuViewTiles, Settings.TileIconSize, defaults.TileIconSize),
            ViewMode.Gallery => (Strings.MenuViewGallery, Settings.GalleryImageSize, defaults.GalleryImageSize),
            _ => (Strings.MenuViewLargeIcons, Settings.LargeIconImageSize, defaults.LargeIconImageSize),
        };

        Status = now == standard
            ? string.Format(Strings.StatusViewSizeDefault, name, now)
            : string.Format(Strings.StatusViewSize, name, now, standard);
    }

    private void SetSortKey(string? name) {
        if (Enum.TryParse<SortKey>(name, out var key)) {
            // Click-the-same-column toggles direction; Explorer parity.
            if (Settings.SortKey == key) {
                Settings.SortAscending = !Settings.SortAscending;
            } else {
                Settings.SortKey = key;
            }
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        // Side effects: re-list when filters change, persist always.
        // Switching the Categories list-selection in the dialog should
        // NOT trigger a Save — that's a UI-only property, not a setting.
        if (e.PropertyName == nameof(SettingsViewModel.SelectedCategory)) {
            return;
        }

        // Tile geometry and the icon column's width are projections of the
        // size settings, not settings of their own: the knob that moved has
        // already come through here and saved. Falling through would just
        // save the same state a second time on every keystroke in the
        // settings dialog.
        if (e.PropertyName == nameof(SettingsViewModel.GalleryPalette)) {
            // Cosmetic like the metrics below, but the pane derives its own
            // colours from it, so the derived property has to be told.
            Raise(nameof(ContentPalette));

            return;
        }

        if (e.PropertyName == nameof(SettingsViewModel.IconsMetrics) ||
            e.PropertyName == nameof(SettingsViewModel.TilesMetrics) ||
            e.PropertyName == nameof(SettingsViewModel.GalleryMetrics) ||
            e.PropertyName == nameof(SettingsViewModel.GalleryLightSwatch) ||
            e.PropertyName == nameof(SettingsViewModel.GalleryGreySwatch) ||
            e.PropertyName == nameof(SettingsViewModel.GalleryDarkSwatch) ||
            e.PropertyName == nameof(SettingsViewModel.DetailsIconColumnWidth)) {
            return;
        }

        if (e.PropertyName == nameof(SettingsViewModel.ShowHidden) ||
            e.PropertyName == nameof(SettingsViewModel.ShowSystem)) {
            Refresh();
            // File-list filter is one half; tree (drives + bookmarks) caches
            // its loaded children, so it needs an explicit reload to drop or
            // surface hidden / system folders.
            Trees.RefreshAll();
        }

        if (e.PropertyName == nameof(SettingsViewModel.IntegrateCompanions)) {
            // Folding sidecars in or out changes the listing itself, so the
            // cheap re-list is exactly what's needed.
            Refresh();
        }

        if (e.PropertyName == nameof(SettingsViewModel.SortKey) ||
            e.PropertyName == nameof(SettingsViewModel.SortAscending) ||
            e.PropertyName == nameof(SettingsViewModel.GroupFoldersFirst)) {
            // Sort only affects the file list — tree always uses default
            // (name asc, folders first). Sort knobs are FS-layer params, not
            // a re-filter, so the cheap path is enough.
            //
            // Results are the exception: their order comes from the pass
            // that found them, not from an enumerator that can be asked
            // again, so the rows already on screen are re-sorted in place.
            if (ContentSearch.IsShowingResults) {
                SearchResults.Resort();
            } else {
                Refresh();
            }
        }

        if (e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDownloads) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDocuments) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkPictures) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkRecycleBin)) {
            Bookmarks.Build(_persistedExpandedPaths);
        }

        if (e.PropertyName == nameof(SettingsViewModel.AutoRefresh)) {
            // Switching it on has to start watching the folder already on
            // screen, not only the next one navigated to.
            UpdateFolderWatch();
        }

        SaveState();
    }


    /// <summary>
    /// Pushes the user's cache limits into the icon provider. Called on every
    /// relevant settings change and once at startup — the provider deliberately
    /// knows nothing about <see cref="AppSettings"/>.
    /// </summary>
    private void ApplyThumbnailCacheSettings() {
        ServiceLocator.Get<IIconProvider>().ConfigureCache(new ThumbnailCacheOptions(
            Settings.ThumbnailMemoryEntries,
            Settings.ThumbnailDiskCacheEnabled,
            Settings.ThumbnailDiskCacheMb * 1024L * 1024L));
    }


    // --- A folder that is no longer there --------------------------------

    /// <summary>
    /// The folder the file area could not list because it is not on disk
    /// any more, or null while the listing is fine. Set by the enumeration
    /// itself rather than by a probe before it: the answer is already in
    /// the exception, and one more <c>DirectoryExists</c> on the UI thread
    /// is one more chance to hang on a dead network share.
    /// </summary>
    public string? MissingFolderPath => _missingFolderPath;

    public bool IsMissingFolder => _missingFolderPath is not null;

    /// <summary>
    /// The missing folder is one of the user's bookmarks — the case where
    /// the panel can offer to do something about it rather than only
    /// report it.
    /// </summary>
    public bool IsMissingBookmark => _missingFolderPath is not null && Bookmarks.Contains(_missingFolderPath);


    private void SetMissingFolder(string? path) {
        if (string.Equals(_missingFolderPath, path, StringComparison.OrdinalIgnoreCase)) {
            return;
        }
        _missingFolderPath = path;
        RaiseMissingFolder();
    }

    private void RaiseMissingFolder() {
        Raise(nameof(MissingFolderPath));
        Raise(nameof(IsMissingFolder));
        Raise(nameof(IsMissingBookmark));
    }


    // --- Bookmarks ------------------------------------------------------
    //
    // The list itself lives in BookmarksController. What stays here is the
    // part that is a window's job — asking the user where the folder went —
    // and the navigation that follows a successful answer.


    /// <summary>
    /// Points a bookmark at where its folder went, and walks into it. Only
    /// the folder picker and the navigation are here; whether the move is
    /// allowed and what it does to the list is the panel's own rule.
    ///
    /// <para>
    /// The picker opens on the deepest part of the old path that still
    /// exists: a bookmark on "A:\B\C\D" that lost D starts the search in C,
    /// which is where the folder was last seen and almost always where it
    /// went. Opening on the dead path itself puts the dialog wherever
    /// Windows last was instead - usually another drive entirely.
    /// </para>
    /// </summary>
    public void RelocateBookmark(string? oldPath) {
        if (string.IsNullOrEmpty(oldPath) || !Bookmarks.Contains(oldPath)) {
            return;
        }

        string? startAt = PathCrumbs.NearestExisting(oldPath, _fs.DirectoryExists);
        string? folder = _dialogs.PickFolder(Strings.BookmarksLocateTitle, startAt);
        if (folder is null) {
            return;
        }

        if (Bookmarks.Relocate(oldPath, folder)) {
            NavigateAndSelectFolder(folder, NavigationSource.Bookmark);
        }
    }


    /// <summary>
    /// Moves a bookmark and puts the keyboard back on it. The row is a new
    /// instance after the rebuild, so the caller cannot keep the old one.
    /// </summary>
    public TreeNodeViewModel? MoveBookmark(TreeNodeViewModel? node, int delta) {
        var moved = Bookmarks.Move(node, delta);
        if (moved is not null) {
            Trees.Select(moved);
        }

        return moved;
    }


    /// <summary>
    /// Makes one path — a folder picked in the tree or the bookmarks — the
    /// current selection: the commands that read
    /// <see cref="SelectedEntries"/> act on it, and the preview pane shows
    /// its census.
    ///
    /// <para>
    /// <see cref="SelectedEntry"/> is deliberately left alone and the
    /// preview is told directly. That property is two-way bound to the
    /// list's <c>SelectedItem</c>, and assigning an item the list does not
    /// contain makes the list push <c>null</c> straight back — which is
    /// exactly the case here, since a folder is never inside its own
    /// listing.
    /// </para>
    /// </summary>
    public void SelectExternalPath(string path) {
        var entry = _fs.GetEntry(path);
        SelectedEntries = entry is null ? Array.Empty<FileSystemEntry>() : new[] { entry };
        Preview.SetPrimary(entry);
    }


    /// <summary>
    /// Navigates into a folder and, once its listing lands, selects the
    /// folder itself — what clicking a row in the tree or the bookmarks
    /// means: go there, and show me what is there.
    /// </summary>
    public void NavigateAndSelectFolder(string path, NavigationSource source) {
        _session.SetArrival(ArrivalIntent.Folder(path));
        NavigateTo(path, source);
    }


    /// <summary>
    /// "Show me where that actually is": goes to the folder holding
    /// <paramref name="path"/>, selects the row and scrolls it into view.
    /// What the preview pane's button for a shortcut's target does, and the
    /// same move Explorer calls "Open file location".
    ///
    /// <para>
    /// Works for a folder as well as a file — the target is selected in its
    /// parent's listing either way, rather than opened, because the point
    /// is to be shown the item, not to walk into it.
    /// </para>
    /// </summary>
    public void RevealPath(string path) {
        if (string.IsNullOrEmpty(path)) {
            return;
        }

        string? folder = Path.GetDirectoryName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(folder)) {
            return;
        }

        _session.SetArrival(ArrivalIntent.Rows(folder, new[] { path }, takeFocus: true));

        // Already there: no navigation will happen, so no listing will land
        // to consume the intent — apply it right away instead.
        if (IsSamePath(folder, _nav.Current)) {
            ApplyArrival();

            return;
        }

        NavigateTo(folder, NavigationSource.External);
    }


    // --- Settings dialog and About --------------------------------------
    /// <summary>
    /// "Версия v0.2.1-beta R, 04f26, 31.08.26" for the «О Wander» submenu —
    /// everything a bug report has to carry, in the order it gets read, and
    /// the same line the session log opens with. The raw +sha suffix is not
    /// shown: forty hex characters in a menu row is not a version, it is a
    /// wall.
    /// </summary>
    public string VersionLabel => string.Format(Strings.MenuVersion, BuildInfo.Line);


    private void OpenSettingsDialog() {
        // Lazy import: the View type lives in Wander.App.Views and is
        // referenced via its full namespace to keep MainViewModel free
        // of view-layer using directives at the top of the file.
        var dlg = new Wander.App.Views.SettingsWindow {
            DataContext = Settings,
            Owner = Application.Current?.MainWindow,
        };
        dlg.ShowDialog();

        // Closing a modal dialog leaves the keyboard wherever WPF's first-
        // focusable search happens to land in the owner window. Put it back
        // on the list, which is where it was when the dialog opened.
        (Application.Current?.MainWindow as MainWindow)?.FocusWorkArea();
    }


    // --- Context-menu verbs ---------------------------------------------
    // These exist because the context menu needs them; the toolbar and the
    // hotkey table are unchanged. What is left here is target resolution —
    // the selection if there is one, the listed folder otherwise, so the
    // same command backs both the item menu and the background menu. The
    // call to the system itself is ShellCommandsController's.

    /// <summary>Selected item, or the folder being listed when nothing is selected.</summary>
    private string? PropertiesTarget() {
        return _selectedEntry?.FullPath ?? _nav.Current;
    }

    /// <summary>What "copy path" acts on: the selection, else the current folder.</summary>
    private IReadOnlyList<string> SelectedPathsOrCurrent() {
        return _selectedEntries.Count > 0
            ? _selectedEntries.Select(e => e.FullPath).ToArray()
            : new[] { _nav.Current ?? "" };
    }

    private void ShowProperties() {
        if (PropertiesTarget() is { } path) {
            Shell.ShowProperties(path);
        }
    }

    /// <summary>
    /// Shows the session's status-bar journal. The write and the open are
    /// the controller's; what is here is only that the journal belongs to
    /// this view model.
    /// </summary>
    private void OpenJournal() {
        Shell.OpenJournal(Journal);
    }

    private void OpenWith() {
        if (_selectedEntry is { } entry) {
            Shell.OpenWith(entry.FullPath);
        }
    }

    private void OpenInTerminal() {
        if (TerminalFolder() is { } folder) {
            Shell.OpenInTerminal(folder);
        }
    }

    /// <summary>Folder a terminal should start in: the selected folder, else the current one.</summary>
    private string? TerminalFolder() {
        if (IsCurrentShellNamespace) {
            return null;
        }
        if (_selectedEntries.Count == 1 && _selectedEntries[0].Kind == EntryKind.Directory) {
            return _selectedEntries[0].FullPath;
        }

        return _nav.Current;
    }


    private void CreateShortcutsForSelection() {
        if (_selectedEntries.Count == 0 || _nav.Current is null) {
            return;
        }
        CreateShortcuts(_selectedEntries.Select(e => e.FullPath).ToList(), _nav.Current);
    }


    // --- Destructive / clipboard ops (always confirm, Cancel-default) --

    private async Task DeleteSelectedAsync(bool permanent) {
        if (_selectedEntries.Count == 0) {
            return;
        }

        var snapshot = _selectedEntries.ToList();
        var paths = WithCompanions(snapshot);
        int extras = paths.Count - snapshot.Count;

        // Permanent (Shift+Delete) always asks. Recycle asks only when the
        // user kept the "confirm" preference on — Ctrl+Z still restores from
        // the bin so skipping the prompt is safe by default.
        bool needsConfirm = permanent || Settings.ConfirmRecycle;
        if (needsConfirm) {
            string title = permanent ? Strings.ConfirmDeleteTitle : Strings.ConfirmRecycleTitle;
            string message;
            if (snapshot.Count == 1) {
                var e0 = snapshot[0];
                string kind = e0.Kind == EntryKind.Directory ? Strings.KindFolder : Strings.KindFile;
                message = string.Format(
                    permanent ? Strings.ConfirmDeleteOne : Strings.ConfirmRecycleOne,
                    kind, e0.Name, e0.FullPath);
            } else {
                message = string.Format(
                    permanent ? Strings.ConfirmDeleteMany : Strings.ConfirmRecycleMany,
                    snapshot.Count,
                    string.Join("\n", snapshot.Take(5).Select(e => "• " + e.Name))
                        + (snapshot.Count > 5 ? "\n" + string.Format(Strings.AndMore, snapshot.Count - 5) : ""));
            }
            // The companions are about to go too; a confirmation that hides
            // that would be a confirmation of the wrong thing.
            if (extras > 0) {
                message += "\n\n" + string.Format(Strings.ConfirmWithCompanions, extras);
            }
            if (permanent) {
                message += "\n\n" + Strings.ConfirmIrreversible;
            }

            bool accepted = _dialogs.Ask(new DialogRequest(
                permanent ? DialogKind.PermanentDeleteConfirm : DialogKind.RecycleConfirm,
                title, message, DialogButtons.OkCancel,
                permanent ? DialogIcon.Error : DialogIcon.Warning));

            if (!accepted) {
                _log.Info($"Delete cancelled by user (permanent={permanent}, items={snapshot.Count})");
                return;
            }
        }

        var readOnlys = snapshot.Where(en => en.IsReadOnly).ToList();
        if (readOnlys.Count > 0) {
            string list = string.Join("\n", readOnlys.Take(5).Select(en => "• " + en.Name)) +
                (readOnlys.Count > 5 ? "\n" + string.Format(Strings.AndMore, readOnlys.Count - 5) : "");
            string roMsg = string.Format(
                readOnlys.Count == 1 ? Strings.ConfirmReadOnlyOne : Strings.ConfirmReadOnlyMany, list);

            bool roAccepted = _dialogs.Ask(new DialogRequest(
                DialogKind.ReadOnlyConfirm, Strings.ConfirmReadOnlyTitle, roMsg,
                DialogButtons.OkCancel, DialogIcon.Warning));
            if (!roAccepted) {
                return;
            }
            foreach (var ro in readOnlys) {
                try {
                    _fs.ClearReadOnly(ro.FullPath);
                } catch (Exception ex) {
                    _log.Error($"ClearReadOnly failed: {ro.FullPath}", ex);
                }
            }
        }

        IReadOnlyList<DeleteResult> results;
        try {
            results = await RunWithProgressDialogAsync(
                permanent ? Strings.ProgressDeleting : Strings.ProgressRecycling,
                ct => _ops.DeleteManyAsync(paths, permanent, ct));
        } catch (OperationCanceledException) {
            Status = Strings.StatusCancelled;
            return;
        } catch (Exception ex) {
            _log.Error($"Delete batch failed", ex);
            Status = string.Format(Strings.StatusDeleteFailed, ex.Message);
            return;
        }

        int ok = results.Count(r => r.Status == DeleteStatus.Ok);
        int failed = results.Count(r => r.Status == DeleteStatus.Failed);

        // Point the keyboard at what will be under it once the rows are
        // gone, and take it back from the progress dialog. Only when
        // something actually went: a delete that failed outright leaves the
        // rows on screen, and moving off them would hide what went wrong.
        if (ok > 0 && _nav.Current is { } folder) {
            var next = NextAfterRemoval(snapshot);
            _session.SetArrival(ArrivalIntent.Rows(folder, next, takeFocus: next.Length > 0));
        }
        Refresh();

        if (failed > 0) {
            var firstFail = results.First(r => r.Status == DeleteStatus.Failed);
            string detail = firstFail.Error is null ? "" : ": " + DescribeError(firstFail.Error, firstFail.Path);
            Status = string.Format(
                permanent ? Strings.StatusDeletedPartly : Strings.StatusRecycledPartly, ok, failed, detail);
        } else {
            Status = string.Format(permanent ? Strings.StatusDeleted : Strings.StatusRecycled, ok);
        }
    }

    /// <summary>
    /// The row the selection should land on once <paramref name="removed"/>
    /// is gone: the first survivor after the last of them, or — when they
    /// were at the end of the folder — the last survivor before the first.
    /// Empty when the folder is being emptied outright, and there is nothing
    /// to land on.
    /// </summary>
    private string[] NextAfterRemoval(IReadOnlyList<FileSystemEntry> removed) {
        var gone = new HashSet<string>(removed.Select(e => e.FullPath), StringComparer.OrdinalIgnoreCase);

        int last = -1;
        for (int i = 0; i < Entries.Count; i++) {
            if (gone.Contains(Entries[i].FullPath)) {
                last = i;
            }
        }
        if (last < 0) {
            return Array.Empty<string>();
        }

        for (int i = last + 1; i < Entries.Count; i++) {
            if (!gone.Contains(Entries[i].FullPath)) {
                return new[] { Entries[i].FullPath };
            }
        }
        for (int i = last - 1; i >= 0; i--) {
            if (!gone.Contains(Entries[i].FullPath)) {
                return new[] { Entries[i].FullPath };
            }
        }

        return Array.Empty<string>();
    }


    private void UndoLast() {
        try {
            var action = _undo.Undo();
            if (action is null) {
                return;
            }
            _log.Info($"Undo: {action.Description}");
            Status = string.Format(Strings.StatusUndone, action.Description);

            // An undo that only put a rating back leaves the folder exactly
            // as it was — same files, same names, same order — so re-listing
            // it would be the same jump the write itself avoids. Only the
            // rows it touched are re-read.
            if (action.MetadataTargets.Count > 0) {
                _ = Ratings.RefreshRowsAsync(action.MetadataTargets);
            } else {
                // Point the user at what came back, not at wherever the
                // selection happened to be.
                _session.SetArrival(ArrivalIntent.Rows(_nav.Current!, action.PathsAfterUndo.ToArray()));
                Refresh();
            }

            // A rating undo rewrites a sidecar the footer is already
            // showing; neither path above would touch it.
            Preview.ReloadCompanions();
        } catch (Exception ex) {
            _log.Error("Undo failed", ex);
            Status = string.Format(Strings.StatusUndoFailed, ex.Message);
        }
    }

    private void Rename(FileSystemEntry? entry, string? newName) {
        if (entry is null || string.IsNullOrWhiteSpace(newName)) {
            return;
        }
        if (newName == entry.Name) {
            return;
        }

        try {
            // Companions ride along under the matching new name, as one
            // undo step — renaming Sprite.png and leaving Sprite.png.meta
            // behind is precisely the breakage this feature exists to stop.
            IReadOnlyList<(string Path, string NewName)> plan = Settings.IntegrateCompanions
                ? _companions.RenamePlan(entry.FullPath, newName, entry.Companions)
                : new[] { (entry.FullPath, newName) };
            _ops.RenameMany(plan);
            // Keep the file the user just renamed selected: its path changed,
            // so "whatever was selected" would no longer match anything.
            string folder = Path.GetDirectoryName(entry.FullPath) ?? "";
            _session.SetArrival(ArrivalIntent.Rows(folder, new[] { Path.Combine(folder, newName) }));
            Refresh();
            if (plan.Count > 1) {
                Status = string.Format(Strings.StatusRenamedWithCompanions, plan.Count - 1);
            }
        } catch (Exception ex) {
            _log.Error($"Rename failed: {entry.FullPath} -> {newName}", ex);
            Status = string.Format(Strings.StatusRenameFailed, DescribeError(ex, entry.FullPath));
        }
    }


    // --- Inline rename ---------------------------------------------------
    // The list templates carry a hidden TextBox per row; RenamingPath is what
    // makes exactly one of them visible. The VM owns the flag (rather than the
    // window) because a refresh, a navigation or a failed rename all have to
    // put the editor away, and those all happen here.

    /// <summary>Full path of the row currently showing its rename editor.</summary>
    public string? RenamingPath {
        get => _renamingPath;
        private set => SetField(ref _renamingPath, value);
    }

    public void BeginRename(FileSystemEntry entry) {
        if (IsCurrentShellNamespace) {
            return;
        }
        RenamingPath = entry.FullPath;
    }

    /// <summary>
    /// Applies the edited name to whichever row the editor belongs to. The
    /// entry is looked up by path rather than taken from the selection: a
    /// commit on lost focus can arrive after the selection has moved on.
    /// </summary>
    public void CommitRename(string? newName) {
        string? path = RenamingPath;
        RenamingPath = null;
        if (path is null) {
            return;
        }

        var entry = Entries.FirstOrDefault(
            e => string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        Rename(entry, newName);
    }

    public void CancelRename() {
        RenamingPath = null;
    }


    // --- Restoring from the recycle bin ---------------------------------
    /// <summary>
    /// Puts the selected recycle-bin items back where they came from. The
    /// shell does the work through <see cref="IRecycleBin.Restore"/> — the
    /// same call <c>Ctrl+Z</c> after a delete already uses, matched by
    /// original path plus deletion time.
    ///
    /// <para>
    /// Deliberately not undoable: Explorer does not offer it either, and
    /// "undo a restore" means deleting a file the user has just asked to
    /// get back. Recording it would make <c>Ctrl+Z</c> destructive.
    /// </para>
    /// </summary>
    private void RestoreFromRecycleBin() {
        var bin = ServiceLocator.Get<IRecycleBin>();
        int restored = 0;
        var failures = new List<string>();

        foreach (var entry in _selectedEntries.ToList()) {
            if (entry.OriginalLocation is null) {
                failures.Add(entry.Name);
                continue;
            }

            try {
                // ModifiedUtc carries the deletion time for bin entries —
                // that is what the enumerator puts there.
                bin.Restore(new RecycleHandle(
                    Path.Combine(entry.OriginalLocation, entry.Name), entry.ModifiedUtc));
                restored++;
            } catch (Exception ex) {
                _log.Error($"Restore failed: {entry.Name}", ex);
                failures.Add(entry.Name);
            }
        }

        Refresh();
        Status = failures.Count == 0
            ? string.Format(Strings.StatusRestored, restored)
            : string.Format(Strings.StatusRestoredPartly, restored, failures.Count, failures[0]);
    }


    // --- Clipboard ------------------------------------------------------
    /// <summary>
    /// Re-reads the OS clipboard so <c>Ctrl+V</c> pastes what the user
    /// copied in another application. Called when the window is activated:
    /// to paste, they have to come back here anyway, so that is the moment
    /// the answer has to be right. The gap — the clipboard changing while
    /// Wander is already the active window — closes itself on the next
    /// activation.
    /// </summary>
    public void SyncClipboardFromSystem() {
        if (!_clipboard.SyncFromSystem()) {
            return;
        }

        if (_clipboard.LastSystemIssue == ClipboardController.SystemIssue.VirtualFiles) {
            // The user did copy something; it just isn't a file on disk (an
            // Outlook attachment, something inside an open .zip). Saying so
            // beats a Paste that is silently greyed out.
            Status = Strings.StatusClipboardVirtualFiles;
        }
    }

    /// <summary>
    /// Every path an operation on <paramref name="entries"/> really touches:
    /// the entries themselves plus their companions.
    ///
    /// <para>
    /// Selected rows already know their sidecars — the folder listing put
    /// them there — so this costs nothing. That matters: it runs on the UI
    /// thread from Copy / Cut / Delete / drag-start, and probing the disk
    /// once per rule per file would stall the window on a large selection.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> WithCompanions(IEnumerable<FileSystemEntry> entries) {
        var expanded = new List<string>();
        foreach (var entry in entries) {
            expanded.Add(entry.FullPath);
            if (Settings.IntegrateCompanions && entry.Companions is { } companions) {
                expanded.AddRange(companions);
            }
        }

        return expanded;
    }


    private void Copy() {
        if (_selectedEntries.Count == 0) {
            return;
        }
        _clipboard.Copy(WithCompanions(_selectedEntries));

        // From inside an archive the clipboard holds paths no other
        // application can open — pasting them in Wander extracts, pasting
        // them elsewhere does nothing. Said out loud rather than discovered.
        if (CurrentArchive is not null) {
            Status = string.Format(Strings.StatusArchiveCopied, _selectedEntries.Count);
            return;
        }

        Status = ClipboardWriteIssue()
            ?? string.Format(Strings.StatusCopied, _selectedEntries.Count);
    }

    private void Cut() {
        if (_selectedEntries.Count == 0) {
            return;
        }
        _clipboard.Cut(WithCompanions(_selectedEntries));
        Status = ClipboardWriteIssue()
            ?? string.Format(Strings.StatusCut, _selectedEntries.Count);
    }


    /// <summary>
    /// The message to show when a copy could not reach the OS clipboard, or
    /// null when it did. The copy itself always worked — only the hand-off
    /// to other applications was lost.
    /// </summary>
    private string? ClipboardWriteIssue() {
        return _clipboard.LastSystemIssue == ClipboardController.SystemIssue.WriteFailed
            ? Strings.StatusClipboardNotShared
            : null;
    }


    /// <summary>
    /// Grouping for paths that did not come from our own listing — a drop
    /// from Explorer, or a clipboard payload. Here the sidecars have to be
    /// looked for on disk, so callers keep this off the UI thread.
    /// </summary>
    private IReadOnlyList<BatchGroup> GroupPathsWithCompanions(IReadOnlyList<string> paths) {
        if (!Settings.IntegrateCompanions) {
            return paths.Select(BatchGroup.Single).ToList();
        }

        var seen = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var expanded = new List<string>(paths);
        foreach (string path in paths) {
            foreach (string companion in _companions.FindCompanions(path, _fs)) {
                if (seen.Add(companion)) {
                    expanded.Add(companion);
                }
            }
        }

        return _companions.Group(expanded);
    }

    private async Task PasteAsync() {
        if (!_clipboard.HasContent || _nav.Current is null) {
            return;
        }

        string target = _nav.Current;
        var sources = _clipboard.Paths.ToList();

        var reason = PathSafety.DetectSelfDrop(sources, target, out string? offender);
        if (reason == SelfDropReason.IntoOwnDescendant || reason == SelfDropReason.Same) {
            string text = PathSafety.FormatReason(reason, offender, target);
            _dialogs.Ask(new DialogRequest(
                DialogKind.CannotPaste, Strings.CannotPasteTitle, text, DialogButtons.Ok, DialogIcon.Warning));
            Status = text;
            return;
        }

        // Paths that came out of an archive cannot be copied by
        // IFileSystem - nothing on disk is at the other end of them. The
        // clipboard carries them as they are, and Paste is where they turn
        // into an extraction.
        if (sources.Any(Archives.Inside)) {
            await ExtractAsync(sources, target);
            return;
        }

        // A cut pasted back where it came from has nothing to move. The cut
        // is dropped and the status line says so: no confirmation, no
        // window, nothing on disk.
        if (_clipboard.IsCut && PathSafety.AllAlreadyIn(sources, target)) {
            _clipboard.Clear();
            Status = Strings.StatusCutAlreadyHere;
            _log.Info($"Paste: cut into its own folder, cut dropped ({sources.Count} item(s) in {target})");
            return;
        }

        bool wasCut = _clipboard.IsCut;
        if (wasCut && !ConfirmMove(sources, target)) {
            return;
        }

        // The clipboard holds a flat list (that is all a clipboard can hold);
        // regrouping it here is what keeps a companion from producing its own
        // conflict dialog.
        var groups = await Task.Run(() => GroupPathsWithCompanions(sources));

        _log.Info($"Paste: {(wasCut ? "move" : "copy")} {groups.Count} item(s) into {target}");
        var resolver = _dialogs.CreateConflictResolver(Settings.SkipIdenticalOnConflict);
        IReadOnlyList<BatchItemResult> results;
        try {
            results = await RunWithProgressDialogAsync(
                wasCut ? Strings.ProgressMoving : Strings.ProgressCopying,
                ct => wasCut
                    ? _ops.MoveManyAsync(groups, target, resolver, ct)
                    : _ops.CopyManyAsync(groups, target, resolver, ct));
        } catch (OperationCanceledException) {
            Status = Strings.StatusCancelled;
            return;
        } catch (Exception ex) {
            _log.Error($"Paste failed into {target}", ex);
            Status = string.Format(Strings.StatusPasteFailed, ex.Message);
            return;
        }

        if (wasCut) {
            _clipboard.Clear();
        }
        // Select what just arrived — the whole point of the operation is now
        // on screen, and the keyboard should already be on it.
        var arrived = results
            .Where(r => r.Status is BatchItemStatus.Ok or BatchItemStatus.Replaced or BatchItemStatus.Renamed or BatchItemStatus.Merged)
            .Select(r => r.FinalDestination)
            .ToArray();
        _session.SetArrival(ArrivalIntent.Rows(target, arrived, takeFocus: arrived.Length > 0));
        Refresh();
        ReportBatchResults(results, wasCut ? Strings.VerbMoved : Strings.VerbCopied, target);
    }

    // --- Archives: extraction and the temporary copy --------------------

    /// <summary>
    /// True when "Извлечь…" has something to work on: rows inside an
    /// archive, or archives selected in an ordinary folder.
    /// </summary>
    private bool CanExtractSelection() {
        return _selectedEntries.Count > 0
            && TryGetShellNamespace() is not null
            && (CurrentArchive is not null || SelectionIsArchive);
    }

    /// <summary>
    /// Re-reads whether the selection is a set of archives. Called from the
    /// two places a selection lands, so the answer is computed once per
    /// change rather than on every <c>CanExecute</c>.
    /// </summary>
    private void NoteSelectionKind() {
        SelectionIsArchive = _selectedEntries.Count > 0
            && _selectedEntries.All(e => e.Kind == EntryKind.File && Archives.Of(e.FullPath) is { IsRoot: true });
    }

    /// <summary>
    /// "Извлечь…" — asks where, then extracts. Inside an archive the
    /// selection is what comes out; on an archive standing in an ordinary
    /// folder it is everything the archive holds, which is what the shell's
    /// own "Извлечь все…" does one row above.
    /// </summary>
    private async Task ExtractSelectionAsync() {
        if (!CanExtractSelection() || TryGetShellNamespace() is not { } ns) {
            return;
        }

        var sources = CurrentArchive is not null
            ? _selectedEntries.Select(e => e.FullPath).ToList()
            : _selectedEntries.SelectMany(e => TopLevelOf(ns, e.FullPath)).ToList();
        if (sources.Count == 0) {
            Status = string.Format(Strings.StatusArchiveEmptyOrLocked, _selectedEntries[0].Name);
            return;
        }

        string? target = _dialogs.PickFolder(Strings.ExtractPickFolderTitle);
        if (string.IsNullOrEmpty(target)) {
            return;
        }

        await ExtractAsync(sources, target);
    }

    /// <summary>What an archive holds at its top level, listed off the disk it sits on.</summary>
    private IReadOnlyList<string> TopLevelOf(IShellNamespace ns, string archivePath) {
        try {
            return ns.Enumerate(archivePath).Select(e => e.FullPath).ToArray();
        } catch (Exception ex) {
            _log.Error($"Archive listing failed: {archivePath}", ex);

            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The one route bytes leave an archive by: the paste of archive
    /// sources into a real folder and the "Извлечь…" row both end here.
    /// </summary>
    private async Task ExtractAsync(IReadOnlyList<string> sources, string target) {
        if (TryGetShellNamespace() is not { } ns) {
            return;
        }

        var service = new ExtractionService(ns, _fs, ServiceLocator.Get<IRecycleBin>(), _undo, _tracker, _log);
        var resolver = _dialogs.CreateConflictResolver(Settings.SkipIdenticalOnConflict);
        _log.Info($"Extract: {sources.Count} item(s) into {target}");

        IReadOnlyList<BatchItemResult> results;
        try {
            results = await RunWithProgressDialogAsync(
                Strings.ProgressExtracting,
                ct => service.ExtractAsync(sources, target, resolver, ct));
        } catch (OperationCanceledException) {
            Status = Strings.StatusCancelled;
            return;
        } catch (Exception ex) {
            _log.Error($"Extract failed into {target}", ex);
            Status = string.Format(Strings.StatusExtractFailed, ex.Message);
            return;
        }

        // Select what arrived, but only when the folder it arrived in is the
        // one on screen: an extraction started from inside the archive lands
        // somewhere the user is not standing.
        var arrived = results
            .Where(r => r.Status is BatchItemStatus.Ok or BatchItemStatus.Replaced or BatchItemStatus.Renamed or BatchItemStatus.Merged)
            .Select(r => r.FinalDestination)
            .ToArray();
        if (string.Equals(_nav.Current, target, StringComparison.OrdinalIgnoreCase)) {
            _session.SetArrival(ArrivalIntent.Rows(target, arrived, takeFocus: arrived.Length > 0));
        }

        Refresh();
        ReportBatchResults(results, Strings.VerbExtracted, target);

        // Nothing came out and nothing was refused: the shell walked the
        // whole batch and wrote no bytes, which is what a password does.
        if (arrived.Length == 0 && results.All(r => r.Status is BatchItemStatus.Failed)) {
            Status = Strings.StatusArchiveLocked;
        }
    }

    /// <summary>
    /// Opening a file that lives inside an archive: copy it out to a
    /// scratch folder and hand that copy to the shell. Said plainly in the
    /// status bar, because the copy is a dead end - editing it changes
    /// nothing in the archive, and Wander cannot write it back.
    /// </summary>
    private async Task OpenArchiveEntryAsync(string path) {
        if (TryGetShellNamespace() is not { } ns) {
            return;
        }

        var service = new ExtractionService(ns, _fs, ServiceLocator.Get<IRecycleBin>(), _undo, _tracker, _log);
        try {
            string copy = await service.ExtractToTempAsync(path, TempFiles.FolderFor(path), CancellationToken.None);
            _shell.Open(copy);
            Status = string.Format(Strings.StatusArchiveTempCopy, Path.GetFileName(copy));
        } catch (Exception ex) {
            _log.Error($"Open from archive failed: {path}", ex);
            Status = string.Format(Strings.StatusOpenFailed, ex.Message);
        }
    }


    private void ReportBatchResults(IReadOnlyList<BatchItemResult> results, string verb, string target) {
        int ok = results.Count(r =>
            r.Status == BatchItemStatus.Ok ||
            r.Status == BatchItemStatus.Replaced ||
            r.Status == BatchItemStatus.Renamed ||
            r.Status == BatchItemStatus.Merged);
        int skipped = results.Count(r => r.Status == BatchItemStatus.Skipped);
        int failed = results.Count(r => r.Status == BatchItemStatus.Failed);
        int cancelled = results.Count(r => r.Status == BatchItemStatus.Cancelled);

        if (ok == 0 && skipped == 0 && failed == 0 && cancelled == results.Count) {
            Status = Strings.StatusCancelled;
            return;
        }

        var parts = new List<string> { string.Format(Strings.StatusBatchDone, verb, ok, target) };
        if (skipped > 0) {
            parts.Add(string.Format(Strings.StatusBatchSkipped, skipped));
        }
        if (cancelled > 0) {
            parts.Add(string.Format(Strings.StatusBatchCancelled, cancelled));
        }
        if (failed > 0) {
            var firstFail = results.First(r => r.Status == BatchItemStatus.Failed);
            string detail = firstFail.Error is null ? "" : ": " + DescribeError(firstFail.Error, firstFail.Source);
            parts.Add(string.Format(Strings.StatusBatchFailed, failed, detail));
        }
        Status = string.Join(", ", parts);
    }

    private void NewFolder() {
        if (_nav.Current is null) {
            return;
        }

        string baseName = Strings.NewFolderName;
        string name = baseName;
        int i = 2;
        while (_fs.DirectoryExists(Path.Combine(_nav.Current, name))) {
            name = $"{baseName} ({i++})";
        }

        try {
            _ops.CreateFolder(_nav.Current, name);
        } catch (Exception ex) {
            _log.Error($"CreateFolder failed in {_nav.Current}: {name}", ex);
            Status = string.Format(Strings.StatusCreateFailed, ex.Message);

            return;
        }

        // "New folder" is never the name anyone wanted, so the next thing
        // the user does is type over it. Selecting it and opening the editor
        // are both for the listing Refresh is about to start — the row does
        // not exist to select, let alone edit, until it lands.
        string created = Path.Combine(_nav.Current, name);
        _session.SetArrival(ArrivalIntent.Rows(
            _nav.Current, new[] { created }, takeFocus: true, renameTarget: created));
        Refresh();

        // The panels beside the list show folders too, and the folder they
        // are standing on just gained one. Leaving it to the watcher would
        // not do: its tick is held for as long as the editor we just opened
        // is up.
        Trees.RefreshFor(_nav.Current);
    }

    private string DescribeError(Exception ex, string path) {
        if (ex is IOException && _lockInspector is not null) {
            var lockers = _lockInspector.WhoIsLocking(path);
            if (lockers.Count > 0) {
                string procs = string.Join(", ", lockers.Select(l => $"{l.ProcessName} (PID {l.ProcessId})"));
                return string.Format(Strings.ErrorFileInUse, procs);
            }
        }
        return ex.Message;
    }

    // --- Operation progress (status bar) -------------------------------

    private void OnTrackerChanged(object? sender, EventArgs e) {
        if (_dispatcher.CheckAccess()) {
            RebuildOperations();
        } else {
            _dispatcher.BeginInvoke(RebuildOperations);
        }
    }

    private void RebuildOperations() {
        var snapshots = _tracker.Snapshot();
        Operations.Clear();
        long totalCompleted = 0;
        long totalSteps = 0;
        foreach (var s in snapshots) {
            Operations.Add(new OperationViewModel(s));
            totalCompleted += s.Completed;
            totalSteps += s.Total;
        }
        AggregateProgress = totalSteps > 0 ? (double)totalCompleted * 100.0 / totalSteps : 0.0;
        Raise(nameof(HasActiveOperations));
    }


    /// <summary>
    /// Run an async batch op inside a modal <see cref="Wander.App.Views.ProgressDialog"/>.
    /// The dialog opens before the await, watches <see cref="_tracker"/> for
    /// per-item progress, and auto-closes when <paramref name="work"/>
    /// finishes (success, failure, or user cancel). Returns whatever the
    /// work returned; rethrows <see cref="OperationCanceledException"/> when
    /// the user clicks Cancel so callers can show a uniform message.
    /// </summary>
    private async Task<TResult> RunWithProgressDialogAsync<TResult>(string headline, Func<CancellationToken, Task<TResult>> work) {
        var dlg = new Wander.App.Views.ProgressDialog(headline, _tracker) {
            Owner = Application.Current?.MainWindow,
        };
        var task = work(dlg.Token);
        dlg.TrackTask(task);
        // ShowDialog blocks this continuation but keeps the dispatcher
        // pumping — when the task completes, the Dispatcher.BeginInvoke
        // posted by TrackTask runs and closes the dialog.
        dlg.ShowDialog();
        return await task.ConfigureAwait(true);
    }

    /// <summary>
    /// The move dialog, or a straight yes when the user has turned it off
    /// (Settings.ConfirmMove). Off is not "move silently, whatever happens":
    /// a move that has to overwrite something still asks, through the
    /// conflict resolver, and Ctrl+Z still takes the whole batch back.
    /// </summary>
    private bool ConfirmMove(IReadOnlyList<string> sources, string target) {
        if (!Settings.ConfirmMove) {
            return true;
        }

        string message;
        if (sources.Count == 1) {
            message = string.Format(
                Strings.ConfirmMoveOne,
                sources[0],
                Path.Combine(target, Path.GetFileName(sources[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))));
        } else {
            message = string.Format(Strings.ConfirmMoveMany, sources.Count, target);
        }

        return _dialogs.Ask(new DialogRequest(
            DialogKind.MoveConfirm, Strings.ConfirmMoveTitle, message,
            DialogButtons.OkCancel, DialogIcon.Warning));
    }
}
