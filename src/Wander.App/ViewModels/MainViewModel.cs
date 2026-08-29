using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wander.App.Conflict;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core;
using Wander.Core.Companions;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Menu;
using Wander.Core.Navigation;
using Wander.Core.Operations;
using Wander.Core.Persistence;
using Wander.Core.Search;
using Wander.Core.Shell;
using Wander.Core.Undo;

namespace Wander.App.ViewModels;

public sealed class MainViewModel : ObservableObject {
    /// <summary>How long a folder may take to list before the spinner shows.</summary>
    private const int SpinnerDelayMs = 150;

    /// <summary>
    /// How often an outside change to the current folder may cost a
    /// re-listing. Half a second: fast enough that a file saved by another
    /// application appears while the user is still looking at the folder,
    /// slow enough that unpacking an archive into it does not re-list a
    /// thousand times.
    /// </summary>
    private const int WatchIntervalMs = 500;

    /// <summary>
    /// How often a running search may repaint the list. Refilling the bound
    /// collection raises one Reset and one full re-layout, and a search over
    /// a deep tree finds something in hundreds of folders — pushing each
    /// batch as it lands would spend the whole search re-laying-out. A fifth
    /// of a second still looks like results streaming in.
    /// </summary>
    private const int ResultFlushIntervalMs = 200;

    private readonly IFileSystem _fs;
    private readonly IShellLauncher _shell;
    private readonly IAppStateStore _stateStore;
    private readonly IFileLockInspector? _lockInspector;
    private readonly NavigationController _nav;
    private readonly FileOperationService _ops;
    private readonly UndoService _undo;
    private readonly OperationTracker _tracker;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _log;
    private readonly CompanionResolver _companions;

    private string _status = "";
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
    // insertion order so the cap below drops the oldest, and persisted in
    // the session bucket of state.json: "the gallery stops guessing here"
    // has to survive a restart, or the promise lasts until teatime.
    //
    // Capped rather than unbounded: this grows one entry per folder the
    // user ever set a view in, and state.json is loaded on every launch.
    private readonly Dictionary<string, ViewMode> _manualViewModes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _manualViewModeOrder = new();
    private const int ManualViewModeLimit = 128;

    private bool _isPreviewVisible;
    private double _previewWidth = 280;
    private double _bookmarksHeight = 200;

    private readonly ClipboardController _clipboard;

    private readonly List<string> _favorites = new();
    private bool _isBookmarksExpanded = true;
    private bool _buildingBookmarks;
    private IReadOnlyList<NavigationStop> _persistedExpandedPaths = Array.Empty<NavigationStop>();

    private CancellationTokenSource? _listLoadCts;
    private bool _isListLoading;

    // --- Ratings --------------------------------------------------------
    // Read after the listing has landed, never as part of it: the folder
    // has to appear at once, and the stars a moment later. Cancelled the
    // same way the listing is, because a pass that finishes after the user
    // has walked into another folder would push that folder's rows over
    // this one's.
    private readonly CompanionMetadataService? _companionMetadata;
    private CancellationTokenSource? _ratingCts;
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

    // What has changed in the folder since the last tick. An accumulator
    // rather than a flag, because "something changed" can only be answered
    // by re-listing everything, and that is exactly the answer that makes
    // the folder jump under the cursor when the something was one number in
    // one sidecar. See FolderChanges.
    private readonly FolderChanges _folderChanges = new();

    // Which folder the rows currently in Entries belong to. A refresh of the
    // same folder reconciles the list in place; only a move to a *different*
    // folder empties it first.
    private string? _listedPath;

    // Paths to re-select once the pending listing lands. Set by whoever knows
    // better than "whatever was selected before" — a rename knows the new
    // name, an undo knows what it put back.
    private string[] _restoreSelection = Array.Empty<string>();

    // A folder picked in the tree or the bookmarks. Applied once its own
    // listing has landed: doing it before would only be overwritten, because
    // rebuilding the list clears whatever the containers had selected.
    private string? _selectFolderAfterListing;
    /// <summary>Path a <see cref="RevealPath"/> is waiting to select once its folder lists.</summary>
    private string? _revealPathAfterListing;

    // Where the user was in each folder they have been in, so coming back
    // lands on the same row. Capped and oldest-first: a long session walks
    // through a lot of folders, and none of this is worth keeping forever.
    private readonly Dictionary<string, string> _selectionMemory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _selectionMemoryOrder = new();
    private const int SelectionMemoryLimit = 64;

    // Search filter is owned by SearchController; we only keep the hidden-
    // count separately for the "X items (N hidden)" status-bar message.
    private readonly SearchController _search = new();
    private int _hiddenCount;


    // Rows the running (or last) search found, in arrival order. Kept apart
    // from Entries so a re-sort has something to sort: Entries is the
    // projection on screen, this is the result set behind it.
    private readonly List<FileSystemEntry> _searchResults = new();
    private DispatcherTimer? _resultFlushTimer;
    private bool _resultsDirty;
    private bool _isSearchWindowOpen;

    private bool _restoring;


    public MainViewModel() {
        _fs = ServiceLocator.Get<IFileSystem>();
        _shell = ServiceLocator.Get<IShellLauncher>();
        _stateStore = ServiceLocator.Get<IAppStateStore>();
        _lockInspector = ServiceLocator.IsRegistered<IFileLockInspector>()
            ? ServiceLocator.Get<IFileLockInspector>()
            : null;
        _ops = ServiceLocator.Get<FileOperationService>();
        _undo = ServiceLocator.Get<UndoService>();
        _tracker = ServiceLocator.Get<OperationTracker>();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _log = ServiceLocator.IsRegistered<ILogger>() ? ServiceLocator.Get<ILogger>() : NullLogger.Instance;
        _companions = ServiceLocator.IsRegistered<CompanionResolver>()
            ? ServiceLocator.Get<CompanionResolver>()
            : CompanionResolver.Default;
        // Without a system clipboard registered the controller keeps its
        // paths to itself, exactly as it did before the mirroring existed.
        _clipboard = new ClipboardController(
            ServiceLocator.IsRegistered<ISystemClipboard>()
                ? ServiceLocator.Get<ISystemClipboard>()
                : null);

        if (ServiceLocator.IsRegistered<IDirectoryWatcher>()) {
            _watcher = ServiceLocator.Get<IDirectoryWatcher>();
            _watcher.Changed += (_, change) => _dispatcher.BeginInvoke(() => NoteFolderChanged(change));
            _watchTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) {
                Interval = TimeSpan.FromMilliseconds(WatchIntervalMs),
            };
            _watchTimer.Tick += OnWatchTick;
        }

        _companionMetadata = ServiceLocator.IsRegistered<CompanionMetadataService>()
            ? ServiceLocator.Get<CompanionMetadataService>()
            : null;

        Preview = new PreviewController(
            ServiceLocator.IsRegistered<IImageMetadataReader>()
                ? ServiceLocator.Get<IImageMetadataReader>()
                : null,
            _companionMetadata,
            ApplyRatingToPrimary,
            RevealPath);

        _nav = new NavigationController(
            new NavigationService(),
            canNavigate: PathIsNavigable,
            onInvalidPath: path => {
                _log.Warn($"Navigate: path not found {path}");
                Status = string.Format(Strings.StatusPathNotFound, path);
            },
            resolveDisplayName: path => {
                if (TryGetShellNamespace() is { } ns && ns.IsShellPath(path)) {
                    return ns.GetDisplayName(path) ?? path;
                }
                return null;
            });

        Entries = new BulkObservableCollection<FileSystemEntry>();
        Roots = new ObservableCollection<TreeNodeViewModel>();
        Bookmarks = new ObservableCollection<TreeNodeViewModel>();
        Operations = new ObservableCollection<OperationViewModel>();

        // Settings VM is owned by MainVM and shared with the dialog when it
        // opens. Any mutation triggers a refresh-or-save side effect via
        // OnSettingsChanged.
        Settings = new SettingsViewModel();
        Settings.PropertyChanged += OnSettingsChanged;

        _tracker.Changed += OnTrackerChanged;

        OpenCommand = new RelayCommand(p => OpenEntry(p as FileSystemEntry ?? _selectedEntry), _ => _selectedEntry is not null);
        // Destructive ops are blocked inside shell namespaces (Recycle Bin):
        // the entries' FullPaths point at $Recycle.Bin backing files, and
        // copying / deleting / renaming those would bypass the shell's
        // restore-tracking and corrupt the bin's state. Read-only browsing
        // only in this iteration.
        DeleteCommand = new RelayCommand(_ => _ = DeleteSelectedAsync(permanent: false), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        RenameCommand = new RelayCommand(p => Rename(_selectedEntry, p as string), _ => _selectedEntry is not null && !IsCurrentShellNamespace);
        CopyCommand = new RelayCommand(_ => Copy(), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
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
        ReportIssueCommand = new RelayCommand(_ => ReportIssue());
        ProjectHomeCommand = new RelayCommand(_ => OpenUrl(Diagnostics.CrashReporter.ProjectUrl));
        // Properties falls back to the folder being listed, so a background
        // right-click (and Alt+Enter with nothing selected) opens the
        // folder's own sheet — Explorer parity.
        PropertiesCommand = new RelayCommand(_ => ShowProperties(), _ => PropertiesTarget() is not null);
        OpenWithCommand = new RelayCommand(_ => OpenWith(), _ => _selectedEntry is not null && !IsCurrentShellNamespace);
        OpenInTerminalCommand = new RelayCommand(_ => OpenInTerminal(), _ => TerminalFolder() is not null);
        CopyPathCommand = new RelayCommand(_ => CopyPathsToClipboard(), _ => PropertiesTarget() is not null);
        CopyNameCommand = new RelayCommand(_ => CopyNamesToClipboard(), _ => _selectedEntries.Count > 0);
        CreateShortcutCommand = new RelayCommand(
            _ => CreateShortcutsForSelection(),
            _ => _selectedEntries.Count > 0 && _nav.Current is not null && !IsCurrentShellNamespace);
        TogglePreviewCommand = new RelayCommand(_ => IsPreviewVisible = !IsPreviewVisible);
        UndoCommand = new RelayCommand(_ => UndoLast(), _ => _undo.CanUndo);
        PermanentDeleteCommand = new RelayCommand(_ => _ = DeleteSelectedAsync(permanent: true), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        OpenLogFileCommand = new RelayCommand(_ => OpenLogFile(), _ => ServiceLocator.IsRegistered<ILogFile>());
        ToggleBookmarksCommand = new RelayCommand(_ => IsBookmarksExpanded = !IsBookmarksExpanded);
        AddBookmarkCommand = new RelayCommand(p => AddBookmark(p as string));
        RemoveBookmarkCommand = new RelayCommand(p => RemoveBookmark(p as TreeNodeViewModel));

        // Batch executors push undo steps from thread-pool workers, so this
        // event can arrive off the UI thread; CommandManager requery only
        // works on the dispatcher thread.
        _undo.Changed += (_, _) => {
            if (_dispatcher.CheckAccess()) {
                UndoCommand.RaiseCanExecuteChanged();
                Raise(nameof(UndoTooltip));
            } else {
                _dispatcher.BeginInvoke(() => {
                    UndoCommand.RaiseCanExecuteChanged();
                    Raise(nameof(UndoTooltip));
                });
            }
        };

        _clipboard.Changed += (_, _) => PasteCommand.RaiseCanExecuteChanged();

        // The controller decides for itself when to run, so it needs the
        // two things only the view model knows — where we are and what may
        // be shown. Handed as callbacks rather than copied, because both
        // change under it while a search is being set up.
        ContentSearch = new ContentSearchController(
            _dispatcher,
            ServiceLocator.IsRegistered<ContentSearchService>()
                ? ServiceLocator.Get<ContentSearchService>()
                : null,
            () => _nav.Current,
            () => Settings.Visibility,
            _log);
        ContentSearch.Started += BeginSearchResults;
        ContentSearch.BatchArrived += AppendSearchResults;
        ContentSearch.Progressed += ShowSearchProgress;
        ContentSearch.Finished += FinishSearch;
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

            ReconcileEntries(() => SyncEntries(filtered));
            UpdateFilterStatus(filtered.Count, _search.Source.Count);
            ApplyRestoreSelection();
            ApplyPendingFolderSelection();
            ApplyPendingReveal();
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

        LoadRoots();
        RestoreState();
        // Restored settings decide how big the thumbnail caches may be; the
        // provider starts idle until it is told.
        ApplyThumbnailCacheSettings();
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


    public BulkObservableCollection<FileSystemEntry> Entries { get; }
    public ObservableCollection<TreeNodeViewModel> Roots { get; }
    public ObservableCollection<TreeNodeViewModel> Bookmarks { get; }
    public ObservableCollection<OperationViewModel> Operations { get; }

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
        set => SetField(ref _status, value);
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
                Preview.SetSelection(value);
                Raise(nameof(SelectionSummary));
            }
        }
    }

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
        set => SetField(ref _viewMode, value);
    }

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
            double clamped = Math.Max(120, Math.Min(900, value));
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
            double clamped = Math.Max(44, Math.Min(900, value));
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
    public RelayCommand SetViewModeCommand { get; }
    public RelayCommand SetGalleryBackgroundCommand { get; }
    public RelayCommand SetSortKeyCommand { get; }
    public RelayCommand ToggleSortAscendingCommand { get; }
    public RelayCommand ToggleGroupFoldersFirstCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand OptionsCommand { get; }
    public RelayCommand ReportIssueCommand { get; }
    public RelayCommand ProjectHomeCommand { get; }
    public RelayCommand PropertiesCommand { get; }
    public RelayCommand TogglePreviewCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand PermanentDeleteCommand { get; }
    public RelayCommand OpenLogFileCommand { get; }
    public RelayCommand ToggleBookmarksCommand { get; }
    public RelayCommand AddBookmarkCommand { get; }

    public RelayCommand RemoveBookmarkCommand { get; }
    public RelayCommand OpenWithCommand { get; }
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
    // Centralised checks so Navigate / Refresh / BuildBookmarks all agree
    // on what counts as a recognised shell location. Caching the lookup
    // would be premature — IsRegistered + Get are cheap dictionary hits.

    private static IShellNamespace? TryGetShellNamespace() {
        return ServiceLocator.IsRegistered<IShellNamespace>()
            ? ServiceLocator.Get<IShellNamespace>()
            : null;
    }

    private bool IsShellPath(string? path) {
        return !string.IsNullOrEmpty(path)
            && TryGetShellNamespace() is { } ns
            && ns.IsShellPath(path);
    }

    private bool PathIsNavigable(string path) {
        return IsShellPath(path) || _fs.DirectoryExists(path);
    }

    /// <summary>
    /// True when the user is currently browsing a shell namespace (e.g.
    /// the Recycle Bin). Used to gate destructive commands — those would
    /// operate on raw $Recycle.Bin backing paths and produce surprising
    /// results.
    /// </summary>
    public bool IsCurrentShellNamespace => IsShellPath(_nav.Current);

    /// <summary>
    /// True in the Recycle Bin specifically. Read-only like any shell
    /// namespace, but with one thing you can still do to its contents —
    /// put them back.
    /// </summary>
    public bool IsCurrentRecycleBin =>
        string.Equals(_nav.Current, ShellPaths.RecycleBin, StringComparison.OrdinalIgnoreCase);

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

            try {
                _shell.Open(entry.FullPath);
            } catch (Exception ex) {
                Status = string.Format(Strings.StatusOpenFailed, ex.Message);
            }
            return;
        }

        NavigateTo(entry.FullPath, NavigationSource.RightPane);
    }

    private bool TryFollowFolderShortcut(string path) {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (!ServiceLocator.IsRegistered<IShortcutService>()) {
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
        NavigateTo(target, NavigationSource.RightPane);
        return true;
    }

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
        var resolver = new DispatcherConflictResolver(new InteractiveConflictResolver());
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
        if (!ServiceLocator.IsRegistered<IShortcutService>()) {
            Status = Strings.StatusShortcutsUnsupported;
            return;
        }

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

    private void RestoreState() {
        var state = _stateStore.Load();
        var session = state.Session;

        _restoring = true;
        try {
            // Settings before view mode / navigation: ShowHidden affects
            // what Refresh() displays, so load filters before the first
            // Refresh fires via the navigation change below.
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
            if (session.PreviewWidth >= 120 && session.PreviewWidth <= 900) {
                _previewWidth = session.PreviewWidth;
                Raise(nameof(PreviewWidth));
            }
            if (session.BookmarksHeight >= 44 && session.BookmarksHeight <= 900) {
                _bookmarksHeight = session.BookmarksHeight;
                Raise(nameof(BookmarksHeight));
            }

            _favorites.Clear();
            _favorites.AddRange(state.Favorites);
            _isBookmarksExpanded = session.IsBookmarksExpanded;
            Raise(nameof(IsBookmarksExpanded));

            _persistedExpandedPaths = session.ExpandedPaths.ToArray();
            // Drives-side expansions are restored immediately. Bookmark-side
            // ones wait until BuildBookmarks below — Bookmarks collection is
            // still empty here, and the matching VM instances don't exist yet.
            // expandTarget:true so the saved node itself is shown as expanded,
            // not just its ancestors (a saved stop means "this node's children
            // were visible at close").
            foreach (var stop in _persistedExpandedPaths) {
                if (stop.Source == NavigationSource.Bookmark) {
                    continue;
                }
                foreach (var root in Roots) {
                    if (root.TryExpandToPath(stop.Path, select: false, expandTarget: true)) {
                        break;
                    }
                }
            }

            // Build the bookmarks tree *before* the initial navigation —
            // OnNavigationChanged → ExpandTreeToCurrent walks Bookmarks
            // for source=Bookmark, and if it's still empty the expander
            // falls back to drives, defeating the whole point of the
            // restored source.
            BuildBookmarks();

            // Before the first navigation: that navigation pushes the
            // restored folder onto the list, and it should land on top of
            // the remembered ones rather than under them.
            _nav.LoadRecentPaths(session.RecentPaths);

            // Honour the RestoreLastFolder preference: when off, ignore
            // LastPath and start at the first drive.
            bool wantRestore = Settings.RestoreLastFolder
                && session.LastPath is not null
                && _fs.DirectoryExists(session.LastPath.Path);
            if (wantRestore) {
                _nav.NavigateTo(session.LastPath!.Path, session.LastPath.Source);
            } else {
                string? first = Roots.FirstOrDefault()?.FullPath;
                if (first is not null) {
                    _nav.NavigateTo(first, NavigationSource.External);
                }
            }
        } finally {
            _restoring = false;
        }
    }

    private void SaveState() {
        if (_restoring || _buildingBookmarks) {
            return;
        }

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
                ExpandedPaths = CollectExpanded(),
                IsPreviewVisible = _isPreviewVisible,
                PreviewWidth = _previewWidth,
                BookmarksHeight = _bookmarksHeight,
                IsBookmarksExpanded = _isBookmarksExpanded,
                RecentPaths = _nav.RecentPaths.ToArray(),
            },
            Favorites = _favorites.ToArray(),
            Settings = Settings.ToRecord(),
        });
    }

    private List<NavigationStop> CollectExpanded() {
        var result = new List<NavigationStop>();
        foreach (var root in Roots) {
            CollectExpandedRecursive(root, result, NavigationSource.Drives);
        }
        foreach (var bookmark in Bookmarks) {
            CollectExpandedRecursive(bookmark, result, NavigationSource.Bookmark);
        }
        // Dedupe on (Path, Source). The same path can legitimately appear
        // in both panels (e.g. a user-favourite that is also reachable via
        // drives) — those are separate expansion states and both kept.
        return result.Distinct().ToList();
    }

    private static void CollectExpandedRecursive(TreeNodeViewModel node, List<NavigationStop> result, NavigationSource source) {
        if (node.IsExpanded && !string.IsNullOrEmpty(node.FullPath)) {
            result.Add(new NavigationStop(node.FullPath, source));
        }
        foreach (var child in node.Children) {
            CollectExpandedRecursive(child, result, source);
        }
    }


    // --- Navigation glue -----------------------------------------------

    private void OnNavigationChanged() {
        // The rows on screen still belong to the folder being left, so this
        // is the last moment its selection can be noted — and the last
        // moment the folder we came out of is known.
        RememberSelection();
        PlanArrivalSelection();

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
        StopResultFlushTimer();
        _searchResults.Clear();
        ContentSearch.Reset();
        Refresh();
        ContentSearch.NoteRootChanged();
        ExpandTreeToCurrent();
        Preview.SetCurrentFolder(_nav.Current, WindowTitle);
        UpdateFolderWatch();
        SaveState();
    }


    /// <summary>
    /// Notes where the user was in the folder currently on screen, so that
    /// coming back to it — Back, Forward, or simply walking in again —
    /// lands on the same row instead of at the top.
    /// </summary>
    private void RememberSelection() {
        if (_listedPath is not { } path || _selectedEntry is not { } entry) {
            return;
        }

        if (!_selectionMemory.ContainsKey(path)) {
            _selectionMemoryOrder.Enqueue(path);
            if (_selectionMemoryOrder.Count > SelectionMemoryLimit) {
                _selectionMemory.Remove(_selectionMemoryOrder.Dequeue());
            }
        }

        _selectionMemory[path] = entry.FullPath;
    }


    /// <summary>
    /// What to select once the folder being moved into has listed.
    ///
    /// <para>
    /// Going up highlights the folder we came out of, which is what makes
    /// Backspace a way to look around rather than a way to lose your place;
    /// anything else falls back to whatever was selected there last time.
    /// Callers that already know better are left alone: a rename, an undo
    /// and a click in the tree have each filled in their own intent.
    /// </para>
    /// </summary>
    private void PlanArrivalSelection() {
        // Whatever was pending belongs to the folder being left — including
        // an intent that an empty folder never got to consume.
        _restoreSelection = Array.Empty<string>();

        if (_selectFolderAfterListing is not null
            || _revealPathAfterListing is not null
            || _nav.Current is not { } arriving) {
            return;
        }

        if (_listedPath is { } left && IsSamePath(arriving, ParentOf(left))) {
            _restoreSelection = new[] { left };

            return;
        }

        if (_selectionMemory.TryGetValue(arriving, out string? remembered)) {
            _restoreSelection = new[] { remembered };
        }
    }


    /// <summary>The folder one level up, or null at a drive root.</summary>
    private static string? ParentOf(string path) {
        return Path.GetDirectoryName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
            _folderChanges.Clear();
            _watchTimer?.Stop();
        }
    }

    private void NoteFolderChanged(DirectoryChange change) {
        _folderChanges.Note(change);
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
    /// </summary>
    private void OnWatchTick(object? sender, EventArgs e) {
        if (_folderChanges.IsEmpty) {
            _watchTimer?.Stop();

            return;
        }

        // Two things must not be interrupted: a name being edited in place
        // (the editor lives in a row that would be rebuilt) and our own file
        // operation (which re-lists when it finishes anyway, and would
        // otherwise re-list on every file it copies). The changes stay
        // pending — they are not dropped — and are answered on a later tick.
        if (RenamingPath is not null || HasActiveOperations) {
            return;
        }

        // A file appeared, vanished or was renamed: the folder holds a
        // different set of files than the one on screen, and only a fresh
        // listing can say what it is now.
        if (_folderChanges.NeedsRelisting) {
            _folderChanges.Clear();
            Refresh();

            return;
        }

        // Nothing appeared or vanished — some files were written to. If
        // every one of them belongs to a row we are showing, those rows are
        // re-read and swapped in place: no new listing, no rebuilt
        // containers, no selection to put back. This is the path a rating
        // written into a sidecar takes, whoever wrote it — us or RawTherapee
        // in the next window.
        var touched = FolderChanges.RowsFor(_search.Source, _folderChanges.ChangedPaths);
        _folderChanges.Clear();

        if (touched is null) {
            Refresh();

            return;
        }
        if (touched.Count > 0) {
            _ = RefreshMetadataRowsAsync(touched.Select(r => r.FullPath).ToArray());
        }
    }

    private TreeNodeViewModel? _lastSelectedTreeNode;

    private void ExpandTreeToCurrent() {
        if (_nav.Current is null) {
            return;
        }

        // Clear the previously selected tree node. IsSelected is two-way
        // bound to the VM, so leaving it set keeps the prior bookmark/drive
        // node visually highlighted when navigation jumps between panels.
        if (_lastSelectedTreeNode is not null) {
            _lastSelectedTreeNode.IsSelected = false;
            _lastSelectedTreeNode = null;
        }

        var src = _nav.CurrentSource ?? NavigationSource.External;

        // Source-aware expansion: a navigation that originated in the
        // bookmarks panel (including replayed history) re-expands only the
        // bookmarks tree, never the drives tree. Falls back to drives when
        // the path is no longer reachable via any bookmark — typically
        // because the user removed the bookmark since the history entry
        // was recorded.
        bool ok = false;
        if (src == NavigationSource.Bookmark) {
            ok = TryExpandAndSelectIn(Bookmarks, _nav.Current);
        }
        if (!ok) {
            TryExpandAndSelectIn(Roots, _nav.Current);
        }
    }

    /// <summary>
    /// Expands one of the two folder panels down to the folder on screen and
    /// selects its node — what Ctrl+2 and Ctrl+Shift+E point the keyboard
    /// at. False when the folder is not reachable in that panel (a path
    /// outside every bookmark, typically); the highlight is then put back
    /// where it was rather than leaving both panels blank.
    /// </summary>
    public bool RevealCurrentIn(NavigationSource panel) {
        if (_nav.Current is null) {
            return false;
        }

        var previous = _lastSelectedTreeNode;
        if (previous is not null) {
            // Cleared before the search: FindSelectedDescendant looks for a
            // selected node and would otherwise find this one.
            previous.IsSelected = false;
            _lastSelectedTreeNode = null;
        }

        if (TryExpandAndSelectIn(panel == NavigationSource.Bookmark ? Bookmarks : Roots, _nav.Current)) {
            return true;
        }

        if (previous is not null) {
            previous.IsSelected = true;
            _lastSelectedTreeNode = previous;
        }

        return false;
    }


    private bool TryExpandAndSelectIn(IEnumerable<TreeNodeViewModel> nodes, string path) {
        foreach (var node in nodes) {
            if (node.TryExpandToPath(path, select: true)) {
                _lastSelectedTreeNode = FindSelectedDescendant(node);
                return true;
            }
        }
        return false;
    }

    private static TreeNodeViewModel? FindSelectedDescendant(TreeNodeViewModel node) {
        if (node.IsSelected) {
            return node;
        }
        foreach (var child in node.Children) {
            var found = FindSelectedDescendant(child);
            if (found is not null) {
                return found;
            }
        }
        return null;
    }

    private void Refresh() {
        // Search results are not a folder listing, and re-listing would
        // replace them with the folder underneath. Leaving results is an
        // explicit act — clearing the box, or navigating — so a refresh
        // here does the one thing it still can honestly do: drop the rows
        // whose files are gone. That is what makes a delete or a rename
        // done on a result actually leave the list.
        if (ContentSearch.IsShowingResults) {
            PruneMissingResults();

            return;
        }

        // A rebuild of the list drops the row the editor was sitting on, so
        // the editor goes with it.
        RenamingPath = null;

        // Default intent: whatever is selected now stays selected once the
        // new listing lands. Callers that know better (rename, undo) have
        // already filled this in.
        if (_restoreSelection.Length == 0) {
            _restoreSelection = _selectedEntries.Select(e => e.FullPath).ToArray();
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
            _listedPath = null;
            _search.SetSource(Array.Empty<FileSystemEntry>());
            Entries.Clear();
            Status = "";
            return;
        }

        // Shell namespaces (Recycle Bin etc.) route through IShellNamespace.
        // Enumeration goes through Shell.Application COM, which can take
        // hundreds of ms with many recycled items, so we hand it off to
        // Task.Run and show a spinner via IsListLoading until it returns.
        // No Hidden/System filtering: shell items don't carry those flags
        // and Wander's "what to hide" preference is filesystem-only.
        if (IsShellPath(_nav.Current) && TryGetShellNamespace() is { } ns) {
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

        // Emptying the list first is right when we are *leaving* a folder:
        // stale rows on screen invite a double-click on a file that is not
        // here any more. Refreshing the folder already on screen is the
        // opposite case — clearing it there is what made every rename,
        // delete and F5 blink and drop the selection, so that listing is
        // reconciled in place once the new one arrives.
        // "Arriving" as opposed to re-listing what is already on screen —
        // the view mode is chosen for a folder the user walks into, not
        // every time F5 or a rename re-reads the one they are standing in.
        bool arriving = !IsSamePath(path, _listedPath);
        if (arriving) {
            _hiddenCount = 0;
            _listedPath = null;
            _search.SetSource(Array.Empty<FileSystemEntry>());
        }
        string statusBeforeLoad = Status;

        var work = Task.Run(() => {
            var items = new List<FileSystemEntry>();
            int hidden = 0;
            foreach (var e in _fs.Enumerate(path, sort)) {
                token.ThrowIfCancellationRequested();
                if (!visibility.Allows(e)) {
                    hidden++;
                    continue;
                }
                items.Add(e);
            }
            // Folding companions happens after the visibility filters, so a
            // sidecar next to a main file the user chose not to see stays
            // visible on its own rather than disappearing with it.
            return (Items: integrate ? _companions.Collapse(items) : items, Hidden: hidden);
        }, token);

        // The spinner is a dimming overlay — raising it for the two frames a
        // local folder takes would make every navigation flash. Only slow
        // folders get it.
        if (await Task.WhenAny(work, Task.Delay(SpinnerDelayMs)) != work && !token.IsCancellationRequested) {
            IsListLoading = true;
        }

        try {
            var (items, hidden) = await work;
            if (token.IsCancellationRequested) {
                return;
            }

            _hiddenCount = hidden;
            _listedPath = path;
            if (arriving) {
                AutoSelectViewMode(items, path);
            }
            // A file operation may have reported its outcome ("Copied 3
            // items") while we were enumerating; the listing's own
            // "N items" must not eat that message.
            string reported = Status;
            _search.SetSource(items);
            if (reported != statusBeforeLoad) {
                Status = reported;
            }
            StartRatingPass(items, path, sort);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            _log.Error($"Enumerate failed: {path}", ex);
            _listedPath = null;
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
        // Same rule as the filesystem path: clear when arriving from
        // somewhere else, reconcile in place when re-listing what is
        // already on screen.
        if (!IsSamePath(shellPath, _listedPath)) {
            _listedPath = null;
            _search.SetSource(Array.Empty<FileSystemEntry>());
        }

        try {
            IReadOnlyList<FileSystemEntry> items;
            try {
                items = await Task.Run(() => ns.Enumerate(shellPath), token);
            } catch (OperationCanceledException) {
                return;
            } catch (Exception ex) {
                _log.Error($"Shell enumerate failed: {shellPath}", ex);
                Status = string.Format(Strings.StatusError, ex.Message);
                return;
            }

            if (token.IsCancellationRequested) {
                return;
            }
            _listedPath = shellPath;
            // No sidecars in a shell namespace, and no picture-folder
            // guessing either: the Recycle Bin is a list of things to
            // decide about, not a folder to look at.
            _ratingCts?.Cancel();
            HasRatings = false;
            _search.SetSource(items.ToList());
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
    private void SyncEntries(IReadOnlyList<FileSystemEntry> items) {
        // The moment a folder's listing lands on the UI thread — the one
        // hitch a person notices when opening a folder.
        using var applying = PerfLog.Measure("list.apply");

        // A wholesale reorder (flipping the sort on a large folder) moves
        // every row anyway, so the straight rebuild is both cheaper and no
        // more disruptive than shuffling the collection item by item.
        if (IsWholesaleChange(items)) {
            // One notification, not one per file. Filling item by item made
            // the tile panel re-measure five thousand times against a list
            // that was still growing — see BulkObservableCollection.
            Entries.ReplaceAll(items);

            return;
        }

        var wanted = new HashSet<string>(items.Select(e => e.FullPath), StringComparer.OrdinalIgnoreCase);
        for (int i = Entries.Count - 1; i >= 0; i--) {
            if (!wanted.Contains(Entries[i].FullPath)) {
                Entries.RemoveAt(i);
            }
        }

        for (int i = 0; i < items.Count; i++) {
            var want = items[i];
            int at = IndexOfPath(want.FullPath, i);
            if (at < 0) {
                Entries.Insert(i, want);
                continue;
            }
            if (at != i) {
                Entries.Move(at, i);
            }
            // Same file, different facts (size, timestamp, sidecars): the row
            // has to show the new ones. This is the only case where a
            // surviving row loses its container.
            if (!Entries[i].SaysTheSameAs(want)) {
                Entries[i] = want;
            }
        }
    }


    /// <summary>
    /// True when so little of the incoming listing lines up with what is on
    /// screen that reconciling it row by row is pointless.
    /// </summary>
    private bool IsWholesaleChange(IReadOnlyList<FileSystemEntry> items) {
        if (Entries.Count == 0 || items.Count == 0) {
            return true;
        }

        int aligned = 0;
        int common = Math.Min(Entries.Count, items.Count);
        for (int i = 0; i < common; i++) {
            if (IsSamePath(Entries[i].FullPath, items[i].FullPath)) {
                aligned++;
            }
        }

        return aligned * 2 < Math.Max(Entries.Count, items.Count);
    }

    private int IndexOfPath(string path, int from) {
        for (int i = from; i < Entries.Count; i++) {
            if (IsSamePath(Entries[i].FullPath, path)) {
                return i;
            }
        }
        return -1;
    }

    private static bool IsSamePath(string? a, string? b) {
        return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Puts the selection back on the rows the last operation asked for.
    /// Consumed once: the next filter keystroke must not re-select anything.
    /// </summary>
    private void ApplyRestoreSelection() {
        // An empty list is not an answer, it is the gap between leaving one
        // folder and the next one listing: arriving somewhere new empties
        // the rows first (see RefreshFolderAsync), and consuming the intent
        // there is what stopped "up one level" from highlighting the folder
        // it came out of.
        if (_restoreSelection.Length == 0 || Entries.Count == 0) {
            return;
        }

        var wanted = new HashSet<string>(_restoreSelection, StringComparer.OrdinalIgnoreCase);
        _restoreSelection = Array.Empty<string>();

        var found = Entries.Where(e => wanted.Contains(e.FullPath)).ToList();
        if (found.Count == 0) {
            // Nothing to land on — and nothing for the list to take the
            // keyboard back onto, so the request does not carry over to
            // whichever restore happens next.
            FocusListAfterRestore = false;

            return;
        }

        SelectedEntry = found[0];
        SelectedEntries = found;
        SelectionRestoreRequested?.Invoke(found);
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
    /// Reads the sidecars of the rows that have one and pushes the listing
    /// back through the filter with the ratings attached.
    ///
    /// <para>
    /// Deliberately a second pass rather than part of the listing: a folder
    /// of five hundred RAW files is five hundred small reads, and making
    /// the folder appear is worth more than making it appear complete. The
    /// rows are already on screen when this lands, and
    /// <see cref="SyncEntries"/> replaces only the ones that gained a
    /// rating.
    /// </para>
    /// </summary>
    private void StartRatingPass(IReadOnlyList<FileSystemEntry> items, string path, SortOptions sort) {
        _ratingCts?.Cancel();
        _ratingCts = null;
        HasRatings = false;

        if (_companionMetadata is null || !items.Any(e => e.HasCompanions)) {
            return;
        }

        _ratingCts = new CancellationTokenSource();
        _ = LoadRatingsAsync(items, path, sort, _ratingCts.Token);
    }

    private async Task LoadRatingsAsync(
        IReadOnlyList<FileSystemEntry> items, string path, SortOptions sort, CancellationToken token) {
        IReadOnlyList<FileSystemEntry> rated;
        try {
            rated = await Task.Run(() => {
                var withRatings = _companionMetadata!.WithRatings(items, token);

                // Sorting by rating is the one key a directory scan cannot
                // answer, so the order it produced was a placeholder. Only
                // this key needs redoing; the other four were right the
                // first time.
                //
                // The name tiebreaker here is the ordinal one rather than
                // Explorer's natural order — it only decides between photos
                // with the same number of stars, and reaching the platform
                // comparer from up here would mean a new abstraction across
                // the whole filesystem interface for that.
                return sort.Key == SortKey.Rating
                    ? EntryComparers.Sort(withRatings, sort)
                    : withRatings;
            }, token);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            _log.Warn($"Rating pass failed: {path} ({ex.Message})");
            return;
        }

        if (token.IsCancellationRequested || !IsSamePath(path, _listedPath)) {
            return;
        }

        HasRatings = rated.Any(e => e.Rating is not null);
        if (!ReferenceEquals(rated, items)) {
            _search.SetSource(rated);
        }
    }


    /// <summary>
    /// Creates the sidecar behind the first star clicked on a photo that
    /// has none, and returns its path — or null if the user said no.
    ///
    /// <para>
    /// Wander does not create files nobody asked for, so this asks, with
    /// Cancel as the default answer. For a <c>.pp3</c> the question carries
    /// the part the user cannot be expected to know: RawTherapee applies
    /// its default processing profile only to photos <em>without</em> a
    /// sidecar, so the file about to be created changes how the photo opens
    /// there. That is the whole reason the format is a setting and its
    /// default is XMP.
    /// </para>
    /// </summary>
    private SidecarRating? ApplyRatingToPrimary(FileSystemEntry entry, RatingField field, int value) {
        var results = ApplyRating(new[] { entry }, field, value);

        return results.Count > 0 ? results[0].Rating : entry.Rating;
    }


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

        ApplyRating(target, RatingField.Rank, rank);
    }


    /// <summary>
    /// The single place a rating is written. Sorts the selection into
    /// "already has a sidecar" and "would need one", asks about the second
    /// group <b>once</b>, hands the lot to Core as one undoable step, and
    /// then updates the affected rows in place.
    ///
    /// <para>
    /// <b>In place is the requirement, not an optimisation.</b> Writing a
    /// star used to re-list the folder: rows were rebuilt, the selection
    /// went with them, a sort by rating reordered the grid, and the picture
    /// the user was looking at moved out from under the cursor — for a
    /// change they made to one number on one file. Nothing here re-lists.
    /// The rows that changed are swapped for updated copies, the selection
    /// is put back without scrolling, and the folder watcher is muted for a
    /// beat so it does not undo all of that on our behalf.
    /// </para>
    ///
    /// <para>
    /// The order does not change either, even when the list is sorted by
    /// rating. Re-sorting under the cursor is precisely the jump this
    /// avoids; the new order arrives with the next listing.
    /// </para>
    /// </summary>
    private IReadOnlyList<CompanionMetadataService.RatingResult> ApplyRating(
        IReadOnlyList<FileSystemEntry> entries, RatingField field, int value) {
        var empty = Array.Empty<CompanionMetadataService.RatingResult>();
        if (_companionMetadata is null || entries.Count == 0) {
            return empty;
        }

        var targets = new List<CompanionMetadataService.RatingTarget>();
        var wouldNeedSidecar = new List<FileSystemEntry>();

        foreach (var entry in entries) {
            if (entry.IsFolderLike) {
                continue;
            }
            if (RatingSidecarOf(entry) is { } sidecar) {
                targets.Add(new CompanionMetadataService.RatingTarget(entry.FullPath, sidecar));
                continue;
            }
            if (ImageFormats.IsImage(entry.Name)) {
                wouldNeedSidecar.Add(entry);
            }
        }

        // Clearing a rating never brings a file into existence: a sidecar
        // created to record "no stars" is exactly the file nobody wanted.
        if (value > 0 && wouldNeedSidecar.Count > 0 && ConfirmSidecarCreation(wouldNeedSidecar)) {
            foreach (var entry in wouldNeedSidecar) {
                targets.Add(new CompanionMetadataService.RatingTarget(entry.FullPath, null));
            }
        }

        if (targets.Count == 0) {
            return empty;
        }

        var results = _companionMetadata.ApplyRatingToMany(targets, field, value, Settings.RawRatingFormat);
        ApplyRatingResults(results);

        return results;
    }


    /// <summary>Path of the companion that holds this row's rating, or null when it has none.</summary>
    private static string? RatingSidecarOf(FileSystemEntry entry) {
        if (entry.Companions is not { Count: > 0 } companions) {
            return null;
        }

        foreach (string path in companions) {
            if (CompanionMetadataService.IsRatingSidecar(path)) {
                return path;
            }
        }

        return null;
    }


    /// <summary>
    /// One question for the whole batch. Rating six selected photos is one
    /// gesture, and six dialogs would be six answers to a question the user
    /// asked once.
    /// </summary>
    private bool ConfirmSidecarCreation(IReadOnlyList<FileSystemEntry> entries) {
        if (_companionMetadata is null) {
            return false;
        }

        var format = Settings.RawRatingFormat;
        string question = entries.Count == 1
            ? string.Format(
                Strings.ConfirmCreateSidecar,
                Path.GetFileName(_companionMetadata.SidecarPathFor(entries[0].FullPath, format)),
                entries[0].Name)
            : string.Format(Strings.ConfirmCreateSidecarMany, entries.Count, format.Suffix());

        if (format == SidecarFormat.Pp3) {
            question += Environment.NewLine + Environment.NewLine + Strings.ConfirmCreateSidecarPp3Warning;
        }

        return MessageBox.Show(
            question, Strings.ConfirmCreateSidecarTitle,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }


    private void ApplyRatingResults(IReadOnlyList<CompanionMetadataService.RatingResult> results) {
        if (results.Count == 0) {
            return;
        }

        var updated = new List<FileSystemEntry>(results.Count);
        foreach (var result in results) {
            var row = FindInSource(result.MainPath);
            if (row is null) {
                continue;
            }

            var companions = row.Companions ?? Array.Empty<string>();
            if (!companions.Any(c => IsSamePath(c, result.SidecarPath))) {
                companions = companions.Append(result.SidecarPath).ToArray();
            }
            updated.Add(row with { Companions = companions, Rating = result.Rating });
        }

        if (updated.Count == 0) {
            return;
        }

        _search.Replace(updated);
        if (updated.Any(e => e.Rating is not null)) {
            HasRatings = true;
        }
        Preview.ReloadCompanions();

        int rank = results[0].Rating.Rank ?? 0;
        Status = rank > 0
            ? string.Format(Strings.StatusRatingApplied, rank, results.Count)
            : string.Format(Strings.StatusRatingCleared, results.Count);
    }

    /// <summary>
    /// Re-reads a few rows from disk — their sidecars and what those say —
    /// and swaps them in without touching the rest of the listing. The
    /// answer to an undo that changed metadata and nothing else.
    ///
    /// <para>
    /// The companion lookup goes to the disk once per row, so it runs on a
    /// worker: undoing a rating applied to two hundred photographs is two
    /// hundred directory probes, and the folder must not stop for them.
    /// </para>
    /// </summary>
    private async Task RefreshMetadataRowsAsync(IReadOnlyList<string> mainPaths) {
        if (_companionMetadata is null || mainPaths.Count == 0) {
            return;
        }

        var rows = mainPaths
            .Select(FindInSource)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToArray();
        if (rows.Length == 0) {
            return;
        }

        bool integrate = Settings.IntegrateCompanions;
        IReadOnlyList<FileSystemEntry> updated;
        try {
            updated = await Task.Run(() => rows.Select(row => {
                var companions = integrate
                    ? _companions.FindCompanions(row.FullPath, _fs)
                    : Array.Empty<string>();
                var refreshed = row with { Companions = companions.Count > 0 ? companions : null };

                return refreshed with { Rating = _companionMetadata.ReadRatingFor(refreshed) };
            }).ToArray());
        } catch (Exception ex) {
            _log.Warn($"Metadata re-read failed: {ex.Message}");

            return;
        }

        _search.Replace(updated);
    }

    private FileSystemEntry? FindInSource(string path) {
        foreach (var entry in _search.Source) {
            if (IsSamePath(entry.FullPath, path)) {
                return entry;
            }
        }

        return null;
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
    /// caller had filled <see cref="_restoreSelection"/> with intent
    /// (navigation, rename, undo) — which meant the second rebuild of every
    /// listing, the one where the ratings arrive, silently dropped whatever
    /// the user had selected. That is the bug this closes, and it closes it
    /// for every future rebuild too rather than for the one that was
    /// noticed.
    /// </para>
    ///
    /// <para>
    /// When a caller <em>has</em> filled <see cref="_restoreSelection"/>,
    /// this keeps out of the way: that caller knows something better than
    /// "whatever was selected before" — the new name after a rename, what
    /// an undo put back — and <see cref="ApplyRestoreSelection"/> runs right
    /// after with scrolling and focus, which is right there and wrong here.
    /// </para>
    /// </summary>
    private void ReconcileEntries(Action rebuild) {
        // A caller that filled _restoreSelection knows something better than
        // "whatever was selected before" — the new name after a rename, what
        // an undo put back — and ApplyRestoreSelection runs right after with
        // scrolling and focus. We still put the view model back in step with
        // the rows below, so it never holds entries that have left the list;
        // we just do not tell the view where to go.
        bool ownsSelection = _restoreSelection.Length == 0;
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

    private void ClearRatingFilter() {
        _search.RatingFilter = RatingFilter.None;
    }

    private void SyncFilterChoices() {
        foreach (var choice in FilterColorChoices) {
            choice.IsSelected = _search.RatingFilter.HasColor(choice.Index);
        }
    }


    private void UpdateFilterStatus(int shown, int total) {
        if (_search.HasRatingFilter && !_search.HasQuery) {
            Status = string.Format(Strings.StatusRatingFilterMatches, shown, total);
        } else if (_search.HasQuery) {
            Status = total > 0
                ? string.Format(Strings.StatusFilterMatches, shown, total, _search.Query)
                : string.Format(Strings.StatusItems, shown);
        } else if (_hiddenCount > 0) {
            Status = string.Format(Strings.StatusItemsWithHidden, shown, _hiddenCount);
        } else {
            Status = string.Format(Strings.StatusItems, shown);
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
        _listLoadCts?.Cancel();
        _ratingCts?.Cancel();
        IsListLoading = false;
        RenamingPath = null;

        _searchResults.Clear();
        Entries.Clear();
        Status = string.Format(Strings.StatusSearching, 0, 0);
        StartResultFlushTimer();
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

        StopResultFlushTimer();
        _searchResults.Clear();
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


    /// <summary>
    /// A folder's worth of results, from the dispatcher. Collected rather
    /// than pushed straight into <see cref="Entries"/>: a bulk refill raises
    /// one Reset, and a search over a deep tree produces hundreds of
    /// batches — one Reset each would make the list re-lay-out hundreds of
    /// times over a collection that keeps growing.
    /// </summary>
    private void AppendSearchResults(IReadOnlyList<FileSystemEntry> batch) {
        if (!ContentSearch.IsShowingResults) {
            return;
        }

        _searchResults.AddRange(batch);
        _resultsDirty = true;
    }


    /// <summary>
    /// How far along the pass is. The search window has no status strip of
    /// its own, so this is the only place the counts appear while the walk
    /// is running — the spinner in the file area says "still going", and
    /// this says how far.
    /// </summary>
    private void ShowSearchProgress(SearchProgress progress) {
        Status = string.Format(Strings.StatusSearching, progress.Found, progress.FilesScanned);
    }


    /// <summary>
    /// End of the pass. A null outcome means the user stopped it — the rows
    /// already found stay, because a search stopped halfway has still
    /// answered part of the question.
    /// </summary>
    private void FinishSearch(SearchOutcome? outcome) {
        StopResultFlushTimer();
        FlushSearchResults();

        if (outcome is not { } result) {
            Status = string.Format(Strings.StatusSearchStopped, _searchResults.Count);

            return;
        }

        string what = SearchDescription();
        string text = result.Found == 0
            ? string.Format(Strings.StatusSearchNothing, what)
            : string.Format(Strings.StatusSearchFound, result.Found, what, result.FilesScanned);

        if (result.Truncated) {
            text += string.Format(Strings.StatusSearchTruncated, result.Found);
        }
        // Worth saying out loud: a folder of PDFs on a machine with no PDF
        // filter installed otherwise reads as "nothing in these documents"
        // when the truth is that none of them could be opened.
        if (result.UnreadableFiles > 0) {
            text += string.Format(Strings.StatusSearchUnreadable, result.UnreadableFiles);
        }

        Status = text;
    }


    /// <summary>
    /// The search as one phrase for the status bar. Both halves when both
    /// were given, because "найдено 3 по запросу «отчёт»" is a different
    /// claim from "3 файла *.docx со словом «отчёт»".
    /// </summary>
    private string SearchDescription() {
        string name = ContentSearch.NameQuery;
        string text = ContentSearch.TextQuery;

        if (name.Length > 0 && text.Length > 0) {
            return string.Format(Strings.SearchDescriptionBoth, name, text);
        }

        return text.Length > 0
            ? string.Format(Strings.SearchDescriptionText, text)
            : name;
    }


    private void OnContentSearchChanged(object? sender, PropertyChangedEventArgs e) {
        switch (e.PropertyName) {
            case nameof(ContentSearchController.IsShowingResults):
                Raise(nameof(IsSearchResults));
                if (!ContentSearch.IsShowingResults && _searchResults.Count > 0) {
                    // Something dropped results without going through
                    // ClearSearch — a criterion falling back to the shallow
                    // kind, for instance. The list has to follow.
                    StopResultFlushTimer();
                    _searchResults.Clear();
                    Refresh();
                }
                break;

            case nameof(ContentSearchController.FilterText):
                Raise(nameof(SearchQuery));
                Raise(nameof(HasSearchQuery));
                break;
        }
    }


    /// <summary>
    /// Pushes what has been found into the bound collection, keeping the
    /// current sort. Sorted rather than left in discovery order because the
    /// user's chosen sort is a setting, not a property of one folder — and
    /// because arrival order changes between two identical searches.
    /// </summary>
    private void FlushSearchResults() {
        if (!_resultsDirty) {
            return;
        }
        _resultsDirty = false;

        var sort = new SortOptions(Settings.SortKey, Settings.SortAscending, Settings.GroupFoldersFirst);
        Entries.ReplaceAll(EntryComparers.Sort(_searchResults, sort));
    }


    /// <summary>
    /// Drops result rows whose files no longer exist. One stat per row, and
    /// only on the paths that asked for a refresh in the first place — a
    /// completed file operation, the folder watcher, a visibility setting.
    /// Rows that are still there keep their place: the pass that found them
    /// is not re-run, because "delete one file" is not a reason to walk the
    /// disk again.
    /// </summary>
    private void PruneMissingResults() {
        int removed = _searchResults.RemoveAll(
            entry => !_fs.FileExists(entry.FullPath) && !_fs.DirectoryExists(entry.FullPath));
        if (removed == 0) {
            return;
        }

        _resultsDirty = true;
        FlushSearchResults();
    }


    private void StartResultFlushTimer() {
        if (_resultFlushTimer is null) {
            _resultFlushTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) {
                Interval = TimeSpan.FromMilliseconds(ResultFlushIntervalMs),
            };
            _resultFlushTimer.Tick += (_, _) => FlushSearchResults();
        }

        _resultFlushTimer.Start();
    }


    private void StopResultFlushTimer() {
        _resultFlushTimer?.Stop();
    }


    private void LoadRoots() {
        Roots.Clear();
        foreach (var root in _fs.GetRoots()) {
            bool hasChildren = _fs.HasSubdirectories(root.FullPath);
            var node = new TreeNodeViewModel(root.Name, root.FullPath, EntryKind.Drive, _fs, hasChildren, Settings);
            Roots.Add(node);
            WireTreeNode(node);
        }
    }

    private void WireTreeNode(TreeNodeViewModel node) {
        node.PropertyChanged += OnTreeNodePropertyChanged;
        node.Children.CollectionChanged += OnTreeChildrenChanged;
        foreach (var child in node.Children) {
            WireTreeNode(child);
        }
    }

    private void OnTreeChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.NewItems is null) {
            return;
        }
        foreach (TreeNodeViewModel added in e.NewItems) {
            WireTreeNode(added);
        }
    }

    private void OnTreeNodePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(TreeNodeViewModel.IsExpanded)) {
            SaveState();
        }
    }


    // --- View modes ----------------------------------------------------

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
        if (e.PropertyName == nameof(SettingsViewModel.IconsMetrics) ||
            e.PropertyName == nameof(SettingsViewModel.TilesMetrics) ||
            e.PropertyName == nameof(SettingsViewModel.GalleryMetrics) ||
            e.PropertyName == nameof(SettingsViewModel.GalleryPalette) ||
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
            foreach (var node in Roots) {
                node.RefreshChildren();
            }
            foreach (var node in Bookmarks) {
                node.RefreshChildren();
            }
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
                _resultsDirty = true;
                FlushSearchResults();
            } else {
                Refresh();
            }
        }

        if (e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDownloads) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDocuments) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkPictures) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkRecycleBin)) {
            BuildBookmarks();
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
        if (!ServiceLocator.IsRegistered<IIconProvider>()) {
            return;
        }

        ServiceLocator.Get<IIconProvider>().ConfigureCache(new ThumbnailCacheOptions(
            Settings.ThumbnailMemoryEntries,
            Settings.ThumbnailDiskCacheEnabled,
            Settings.ThumbnailDiskCacheMb * 1024L * 1024L));
    }


    // --- Bookmarks ------------------------------------------------------

    /// <summary>
    /// Rebuilds the left-pane bookmarks list from the current settings
    /// (enabled special folders) and the user's saved favourites. Idempotent;
    /// any node currently realised in the visual tree gets a fresh instance,
    /// so callers should be ready for binding refresh.
    /// </summary>
    private void BuildBookmarks() {
        // Capture the *current* expansion state of bookmark folders so it
        // survives the rebuild — BuildBookmarks creates fresh VM instances
        // for the Downloads / user-favourite branches each time. Merge with
        // the startup-loaded set, but only with bookmark-source stops —
        // drives-side entries describe the lower tree and don't belong here.
        var live = new List<NavigationStop>();
        foreach (var b in Bookmarks) {
            CollectExpandedRecursive(b, live, NavigationSource.Bookmark);
        }
        var bookmarkStops = new HashSet<NavigationStop>(live);
        foreach (var stop in _persistedExpandedPaths) {
            if (stop.Source == NavigationSource.Bookmark) {
                bookmarkStops.Add(stop);
            }
        }

        _buildingBookmarks = true;
        try {
            Bookmarks.Clear();

            if (Settings.ShowBookmarkDownloads) {
                AddSpecialFolderNode(Strings.SpecialFolderDownloads, ResolveDownloads());
            }
            if (Settings.ShowBookmarkDocuments) {
                AddSpecialFolderNode(Strings.SpecialFolderDocuments, ResolveDocuments());
            }
            if (Settings.ShowBookmarkPictures) {
                AddSpecialFolderNode(Strings.SpecialFolderPictures, ResolvePictures());
            }
            if (Settings.ShowBookmarkRecycleBin && TryGetShellNamespace() is not null) {
                AddSpecialFolderNode(Strings.SpecialFolderRecycleBin, ShellPaths.RecycleBin);
            }

            foreach (string path in _favorites) {
                var node = TryBuildFolderNode(path);
                if (node is not null) {
                    Bookmarks.Add(node);
                }
            }

            foreach (var stop in bookmarkStops) {
                foreach (var b in Bookmarks) {
                    if (b.TryExpandToPath(stop.Path, select: false, expandTarget: true)) {
                        break;
                    }
                }
            }
        } finally {
            _buildingBookmarks = false;
        }
    }

    private string? ResolveDownloads() {
        return ServiceLocator.IsRegistered<IKnownFolders>()
            ? ServiceLocator.Get<IKnownFolders>().GetDownloads()
            : null;
    }

    private string? ResolveDocuments() {
        return ServiceLocator.IsRegistered<IKnownFolders>()
            ? ServiceLocator.Get<IKnownFolders>().GetDocuments()
            : null;
    }

    private string? ResolvePictures() {
        return ServiceLocator.IsRegistered<IKnownFolders>()
            ? ServiceLocator.Get<IKnownFolders>().GetPictures()
            : null;
    }

    /// <summary>
    /// Adds one special-folder node to <see cref="Bookmarks"/>. No-op when
    /// the path can't be resolved or doesn't exist on disk (e.g. user moved
    /// the folder to a removed drive). The label is a fixed localised name,
    /// not the on-disk folder name, so the user sees a stable caption.
    /// Shell-namespace paths (Recycle Bin) take a different code path:
    /// no <see cref="IFileSystem"/> probe and no lazy-load children — the
    /// node is a clickable leaf, navigated through <see cref="IShellNamespace"/>.
    /// </summary>
    private void AddSpecialFolderNode(string label, string? path) {
        if (string.IsNullOrEmpty(path)) {
            return;
        }
        if (IsShellPath(path)) {
            // No tree children for shell namespaces in this iteration —
            // Recycle Bin is presented as a flat list in the right pane,
            // not browseable from the bookmarks tree.
            var shellNode = new TreeNodeViewModel(label, path, EntryKind.Directory, fs: null, hasChildren: false);
            WireTreeNode(shellNode);
            Bookmarks.Add(shellNode);
            return;
        }
        if (!_fs.DirectoryExists(path)) {
            return;
        }
        var node = new TreeNodeViewModel(label, path, EntryKind.Directory, _fs, _fs.HasSubdirectories(path), Settings);
        WireTreeNode(node);
        Bookmarks.Add(node);
    }

    private TreeNodeViewModel? TryBuildFolderNode(string path) {
        if (string.IsNullOrEmpty(path) || !_fs.DirectoryExists(path)) {
            return null;
        }
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) {
            // e.g. a drive root — fall back to the trimmed path itself.
            name = path;
        }
        var node = new TreeNodeViewModel(name, path, EntryKind.Directory, _fs, _fs.HasSubdirectories(path), Settings) {
            IsRemovableBookmark = true,
        };
        WireTreeNode(node);
        return node;
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
        _selectFolderAfterListing = path;
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

        // Already there: no navigation will happen, so no listing will land
        // to consume the intent — select right away instead.
        if (IsSamePath(folder, _nav.Current)) {
            _restoreSelection = new[] { path };
            FocusListAfterRestore = true;
            ApplyRestoreSelection();

            return;
        }

        _revealPathAfterListing = path;
        NavigateTo(folder, NavigationSource.External);
    }


    /// <summary>
    /// Selects the row a <see cref="RevealPath"/> was asking for, now that
    /// its folder has listed. Guarded by the same "is this the listing that
    /// was asked for" check as the folder case: a navigation that overtook
    /// this one must not drag the selection along with it.
    /// </summary>
    private void ApplyPendingReveal() {
        if (_revealPathAfterListing is not { } path) {
            return;
        }

        string? folder = Path.GetDirectoryName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!IsSamePath(folder, _nav.Current)) {
            return;
        }

        _revealPathAfterListing = null;
        _restoreSelection = new[] { path };
        FocusListAfterRestore = true;
        ApplyRestoreSelection();
    }


    private void ApplyPendingFolderSelection() {
        if (_selectFolderAfterListing is not { } path) {
            return;
        }
        _selectFolderAfterListing = null;

        // The listing that just landed has to be the one that was asked
        // for. A rejected path, or a navigation that overtook this one,
        // would otherwise select a folder the user is not looking at.
        if (IsSamePath(path, _nav.Current)) {
            SelectExternalPath(path);
        }
    }


    /// <summary>
    /// Already in the bookmarks? The drop strip asks before offering to
    /// take a folder: dropping one that is in the list does nothing, and a
    /// target that lights up for a no-op is a lie.
    /// </summary>
    public bool IsBookmarked(string path) {
        return _favorites.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }


    public void AddBookmark(string? path) {
        if (string.IsNullOrEmpty(path) || !_fs.DirectoryExists(path)) {
            return;
        }
        if (_favorites.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) {
            Status = Strings.StatusAlreadyBookmarked;
            return;
        }
        _favorites.Add(path);
        _log.Info($"Bookmark added: {path}");
        Status = string.Format(Strings.StatusBookmarkAdded, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        BuildBookmarks();
        SaveState();
    }

    public void RemoveBookmark(TreeNodeViewModel? node) {
        if (node is null || string.IsNullOrEmpty(node.FullPath)) {
            return;
        }
        int idx = _favorites.FindIndex(p => string.Equals(p, node.FullPath, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) {
            // Special folder (Downloads / This PC) — not a user favourite;
            // hiding it goes via Settings.
            return;
        }
        _favorites.RemoveAt(idx);
        _log.Info($"Bookmark removed: {node.FullPath}");
        BuildBookmarks();
        SaveState();
    }

    public bool IsUserFavorite(TreeNodeViewModel? node) {
        if (node is null || string.IsNullOrEmpty(node.FullPath)) {
            return false;
        }
        return _favorites.Any(p => string.Equals(p, node.FullPath, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenSettingsDialog() {
        // Lazy import: the View type lives in Wander.App.Views and is
        // referenced via its full namespace to keep MainViewModel free
        // of view-layer using directives at the top of the file.
        var dlg = new Wander.App.Views.SettingsWindow {
            DataContext = Settings,
            Owner = Application.Current?.MainWindow,
        };
        dlg.ShowDialog();
    }

    /// <summary>
    /// "Версия 0.2.1-beta" for the «О Wander» submenu. The +sha suffix the
    /// crash bundle carries is cut: it is there so a stack trace can be
    /// pinned to a commit, and it makes a menu row unreadable.
    /// </summary>
    public string VersionLabel {
        get {
            string version = Diagnostics.CrashReporter.AppVersion();
            int plus = version.IndexOf('+');

            return string.Format(Strings.MenuVersion, plus < 0 ? version : version[..plus]);
        }
    }


    private void ReportIssue() {
        // GitHub's template chooser lets the user pick "Bug report" or
        // "Feature request"; nothing is pre-filled, so no session data is
        // involved — unlike the crash path, which bundles diagnostics.
        OpenUrl(Wander.App.Diagnostics.CrashReporter.IssueChooserUrl);
    }

    private void OpenUrl(string url) {
        try {
            _shell.Open(url);
        } catch (Exception ex) {
            Status = string.Format(Strings.StatusBrowserFailed, ex.Message);
        }
    }

    private void OpenLogFile() {
        if (!ServiceLocator.IsRegistered<ILogFile>()) {
            Status = Strings.StatusNoLogging;
            return;
        }
        string path = ServiceLocator.Get<ILogFile>().FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            Status = Strings.StatusNoLogFile;
            return;
        }
        try {
            _shell.Open(path);
        } catch (Exception ex) {
            Status = string.Format(Strings.StatusOpenLogFailed, ex.Message);
        }
    }

    private void ShowProperties() {
        if (PropertiesTarget() is not string path) {
            return;
        }
        try {
            _shell.ShowProperties(path);
        } catch (Exception ex) {
            Status = string.Format(Strings.StatusPropertiesFailed, ex.Message);
        }
    }


    // --- Context-menu verbs ---------------------------------------------
    // These exist because the context menu needs them; the toolbar and the
    // hotkey table are unchanged. Each resolves its own target the same way:
    // the selection if there is one, the listed folder otherwise, so the
    // same command backs both the item menu and the background menu.

    /// <summary>Selected item, or the folder being listed when nothing is selected.</summary>
    private string? PropertiesTarget() {
        return _selectedEntry?.FullPath ?? _nav.Current;
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

    private void OpenWith() {
        if (_selectedEntry is null) {
            return;
        }
        try {
            _shell.OpenWith(_selectedEntry.FullPath);
        } catch (Exception ex) {
            _log.Error($"Open with failed: {_selectedEntry.FullPath}", ex);
            Status = string.Format(Strings.StatusOpenWithFailed, ex.Message);
        }
    }

    private void OpenInTerminal() {
        if (TerminalFolder() is not string folder) {
            return;
        }
        try {
            _shell.OpenTerminal(folder);
        } catch (Exception ex) {
            _log.Error($"Open terminal failed: {folder}", ex);
            Status = string.Format(Strings.StatusTerminalFailed, ex.Message);
        }
    }

    private void CopyPathsToClipboard() {
        var paths = _selectedEntries.Count > 0
            ? _selectedEntries.Select(e => e.FullPath).ToArray()
            : new[] { _nav.Current ?? "" };

        // Quoted, one per line — the shape you can paste straight into a
        // shell. Explorer's "Copy as path" does the same. The status line
        // gets the bare paths instead: it is there to show *what* landed in
        // the clipboard, and quotes only get in the way of reading it.
        SetClipboardText(
            string.Join(Environment.NewLine, paths.Select(p => $"\"{p}\"")),
            Summarize(paths));
    }

    private void CopyNamesToClipboard() {
        if (_selectedEntries.Count == 0) {
            return;
        }
        var names = _selectedEntries.Select(e => e.Name).ToArray();

        SetClipboardText(string.Join(Environment.NewLine, names), Summarize(names));
    }

    /// <summary>
    /// One line naming what was copied. The status bar trims to its width,
    /// so a long multi-select degrades to "first, second, …" on its own —
    /// but the first entries stay readable, which is the point.
    /// </summary>
    private static string Summarize(IReadOnlyList<string> items) {
        return string.Join(", ", items);
    }

    private void SetClipboardText(string text, string what) {
        try {
            Clipboard.SetText(text);
            Status = string.Format(Strings.StatusCopiedToClipboard, what);
        } catch (Exception ex) {
            // The OS clipboard is a shared, lockable resource — another app
            // holding it turns this into a COMException, not a bug in ours.
            _log.Warn($"Clipboard copy failed: {ex.Message}");
            Status = string.Format(Strings.StatusClipboardBusy, ex.Message);
        }
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

            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.OKCancel,
                permanent ? MessageBoxImage.Error : MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (result != MessageBoxResult.OK) {
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

            var roResult = MessageBox.Show(
                roMsg,
                Strings.ConfirmReadOnlyTitle,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);
            if (roResult != MessageBoxResult.OK) {
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
        if (ok > 0) {
            _restoreSelection = NextAfterRemoval(snapshot);
            FocusListAfterRestore = _restoreSelection.Length > 0;
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
                _ = RefreshMetadataRowsAsync(action.MetadataTargets);
            } else {
                // Point the user at what came back, not at wherever the
                // selection happened to be.
                _restoreSelection = action.PathsAfterUndo.ToArray();
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
            _restoreSelection = new[] {
                Path.Combine(Path.GetDirectoryName(entry.FullPath) ?? "", newName),
            };
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
        if (!ServiceLocator.IsRegistered<IRecycleBin>()) {
            return;
        }

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


    private void Copy() {
        if (_selectedEntries.Count == 0) {
            return;
        }
        _clipboard.Copy(WithCompanions(_selectedEntries));
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


    /// <summary>
    /// The selection as <see cref="BatchGroup"/>s, so a batch operation
    /// treats a file and its sidecars as one item: one conflict question,
    /// one progress step, one line in the status bar.
    /// </summary>
    private IReadOnlyList<BatchGroup> GroupWithCompanions(IEnumerable<FileSystemEntry> entries) {
        return entries
            .Select(e => Settings.IntegrateCompanions && e.Companions is { Count: > 0 } companions
                ? new BatchGroup(e.FullPath, companions)
                : BatchGroup.Single(e.FullPath))
            .ToList();
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
            MessageBox.Show(text, Strings.CannotPasteTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = text;
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
        var resolver = new DispatcherConflictResolver(new InteractiveConflictResolver());
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
        _restoreSelection = results
            .Where(r => r.Status is BatchItemStatus.Ok or BatchItemStatus.Replaced or BatchItemStatus.Renamed)
            .Select(r => r.FinalDestination)
            .ToArray();
        FocusListAfterRestore = _restoreSelection.Length > 0;
        Refresh();
        ReportBatchResults(results, wasCut ? Strings.VerbMoved : Strings.VerbCopied, target);
    }

    private void ReportBatchResults(IReadOnlyList<BatchItemResult> results, string verb, string target) {
        int ok = results.Count(r =>
            r.Status == BatchItemStatus.Ok ||
            r.Status == BatchItemStatus.Replaced ||
            r.Status == BatchItemStatus.Renamed);
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
            Refresh();
        } catch (Exception ex) {
            _log.Error($"CreateFolder failed in {_nav.Current}: {name}", ex);
            Status = string.Format(Strings.StatusCreateFailed, ex.Message);
        }
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

    private static bool ConfirmMove(IReadOnlyList<string> sources, string target) {
        string message;
        if (sources.Count == 1) {
            message = string.Format(
                Strings.ConfirmMoveOne,
                sources[0],
                Path.Combine(target, Path.GetFileName(sources[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))));
        } else {
            message = string.Format(Strings.ConfirmMoveMany, sources.Count, target);
        }

        var result = MessageBox.Show(
            message,
            Strings.ConfirmMoveTitle,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        return result == MessageBoxResult.OK;
    }
}
