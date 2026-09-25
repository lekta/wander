using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wander.App.Controllers;
using Wander.App.Controls;
using Wander.App.Dialogs;
using Wander.App.Menu;
using Wander.App.Preview;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Actions;
using Wander.Core.Companions;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Folders;
using Wander.Core.Icons;
using Wander.Core.Imaging;
using Wander.Core.Layout;
using Wander.Core.Listing;
using Wander.Core.Logging;
using Wander.Core.Menu;
using Wander.Core.Navigation;
using Wander.Core.Operations;
using Wander.Core.Panels;
using Wander.Core.Persistence;
using Wander.Core.Preview;
using Wander.Core.Rename;
using Wander.Core.Search;
using Wander.Core.Shell;
using Wander.Core.Undo;
using Wander.Core.Workspace;


namespace Wander.App;

[SuppressMessage("Design", "CA1001",
    Justification = "Cancellation sources of list loads and panel reads, managed only; the view model lives as long as the application.")]
public sealed class MainViewModel : ObservableObject {
    /// <summary>
    /// The debug menu's fourth operation row: not a scenario of its own,
    /// three plain ones at the same time (PLAN AI1).
    /// </summary>
    private const string ThreeAtOnce = "ThreeAtOnce";

    /// <summary>How long a folder may take to list before the spinner shows.</summary>
    private const int SpinnerDelayMs = 150;

    /// <summary>
    /// How long a pair's split waits for its pictures' shapes (ShowPair).
    /// Headers read in milliseconds; a slow disk gets the split this late
    /// at most, with a picture not read yet counted as square.
    /// </summary>
    private const int PairShapesWaitMs = 150;

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
    /// How long an operation runs before its window comes up (2026-09-21).
    /// Most are over long before that - a delete of one file, an undo of a
    /// rename, a refusal that takes no time at all - and a window flashing
    /// up and taking the focus for them is worse than none. The status bar
    /// shows the operation from its first moment either way; the clocks on
    /// its rows come up with the window (2026-09-23).
    /// </summary>
    private static readonly TimeSpan _operationWindowDelay = TimeSpan.FromMilliseconds(PathClaims.BadgeDelayMs);

    /// <summary>How often a window held back by a modal question looks again.</summary>
    private static readonly TimeSpan _operationWindowRecheck = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long "delete for good" stays disabled once the bin's refusal is
    /// put to the user (2026-09-21): an Enter pressed without looking, in the
    /// middle of deleting file after file, must not reach it.
    /// </summary>
    private static readonly TimeSpan _deleteForGoodArmDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Smallest the preview pane may be, and what the file list keeps of the window beside it.</summary>
    private const double PreviewMinWidth = 120;
    private const double ListMinWidth = 240;

    /// <summary>Smallest the folders pane on the left may be; the list's reserve is the same as above.</summary>
    private const double FoldersMinWidth = 120;

    /// <summary>The same pair for the bookmarks panel and the drives tree under it.</summary>
    private const double BookmarksMinHeight = 44;
    private const double TreeMinHeight = 200;

    private readonly IFileSystem _fs;
    private readonly IShellLauncher _shell;
    private readonly IAppStateStore _stateStore;
    private readonly IFolderSettingsStore _folderStore;
    private readonly IDialogs _dialogs;
    private readonly IFileLockInspector? _lockInspector;
    private readonly NavigationController _nav;
    private readonly FileOperationService _ops;
    private readonly ExternalActionRunner _actions;
    private readonly IToolLocator _toolLocator;
    private readonly UndoService _undo;
    private readonly OperationTracker _tracker;
    private readonly PathClaims _claims;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _log;
    private readonly CompanionResolver _companions;
    private readonly CompanionMetadataService? _companionMetadata;

    // The operation windows currently open, minimised ones included: how the
    // status-bar panel gets one back on screen, or stops it.
    private readonly List<Wander.App.Views.ProgressDialog> _operationWindows = new();

    private string _status = "";
    private StatusSeverity _statusSeverity;
    private string? _caretPath;
    private FileSystemEntry? _selectedEntry;
    private string? _renamingPath;
    private IReadOnlyList<FileSystemEntry> _selectedEntries = Array.Empty<FileSystemEntry>();

    // What the next operation is about is not a fact of its own (REDESIGN
    // 4.3): it is read (TargetRules) from where the keyboard is, where each
    // panel's cursor stands and the open menu - the window's model
    // (Workspace) - and the list's selection above. _target is its last
    // reading, redone whenever one of those changes.
    private Target _target = Target.None;

    // The place of the last panel row asked about (PlaceOf): an archive's
    // row asks the disk whether it is one, and CanExecute asks constantly.
    private (Target Row, PlaceFacts Place)? _panelPlace;

    // The panel row's folder the preview shows, or is reading off the UI
    // thread to show - see ShowPanelFolder.
    private string? _panelFolderPath;
    private CancellationTokenSource? _panelFolderCts;

    // The pair the split is for, and whether its pictures' shapes are in -
    // see ShowPair.
    private (string First, string Second)? _pairShown;
    private bool _pairShapesRead;
    private CancellationTokenSource? _pairShapesCts;

    // What is on screen and why (ViewChoice). The choice itself is not
    // stored here: a pin lives in _folders, the default in the settings,
    // and the gallery switching itself on in a folder of photographs is a
    // decision made again on the next arrival.
    private ViewMode _viewMode = ViewMode.LargeIcons;
    private ViewReason _viewReason = ViewReason.Default;

    // The records about folders - pinned views today (FolderSettingsBook,
    // folders.json). Written through _folderStore when a call reported a
    // change; the flag rides the same debounced save as state.json.
    private FolderSettingsBook _folders = new();
    private bool _foldersDirty;

    // Creation time of the open folder, read on the pool with its listing:
    // the second half of its key in the book (FolderRecord.CreatedUtc).
    private DateTime? _currentCreatedUtc;

    private bool _isPreviewVisible;
    private double _previewWidth = 280;
    private double _foldersWidth = 280;
    private double _bookmarksHeight = 200;

    // Pane sizes as they were persisted, plus the window they were a share
    // of, held from RestoreState until the window is loaded and can say how
    // big it is now - see RestorePaneSizes.
    private double _savedPreviewWidth;
    private double _savedFoldersWidth;
    private double _savedBookmarksHeight;
    private double _savedWindowWidth;
    private double _savedWindowHeight;
    private double _windowWidth;
    private double _windowHeight;
    private string? _lastWrittenPanes;

    private readonly ClipboardController _clipboard;

    private bool _isBookmarksExpanded = true;
    private bool _isFoldersVisible = true;
    private bool _isPreviewSplit;
    private string? _missingFolderPath;

    private CancellationTokenSource? _listLoadCts;
    private bool _isListLoading;

    // --- Ratings --------------------------------------------------------
    // Read after the listing has landed, never as part of it: the folder
    // has to appear at once, and the stars a moment later. Cancelled the
    // same way the listing is, because a pass that finishes after the user
    // has walked into another folder would push that folder's rows over
    // this one's.
    private bool _hasRatings;

    // "Sharp is lighter" in the gallery: a second pass over the photographs,
    // like the ratings one, run only in the gallery with the sharpness
    // helper on.
    private readonly SharpnessController _sharpness;

    // True while Entries is being laid down again (SyncEntries, ReplaceRows):
    // the list drops rebuilt and replaced rows out of its selection on its
    // own, and what it reports meanwhile is not the user's doing - the
    // model puts the selection back once the rows have landed.
    private bool _syncingRows;

    // The folder whose rows landed last: rows of another one are an arrival.
    private string? _landedFolder;

    // Rows renamed or moved since the last landing, old path to new - ours
    // and the ones the watcher saw - for the selection to follow (B7).
    private readonly List<(string From, string To)> _landingRenames = new();

    // Renames of ours that have not landed yet: the watcher's tick waits
    // for them (L-11) - the editor is gone before the rename is done.
    private int _renamesInFlight;

    // A folder move is being followed: the navigation it causes is the
    // listing going after the folder, not the user going somewhere.
    private bool _followingMove;

    // The rows in Entries are search results, not a listing of the folder -
    // until the next listing lands (SyncEntries).
    private bool _entriesAreResults;

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



    public MainViewModel() {
        _fs = ServiceLocator.Get<IFileSystem>();
        _shell = ServiceLocator.Get<IShellLauncher>();
        _stateStore = ServiceLocator.Get<IAppStateStore>();
        _folderStore = ServiceLocator.Get<IFolderSettingsStore>();
        _dialogs = ServiceLocator.Get<IDialogs>();
        _lockInspector = ServiceLocator.TryGet<IFileLockInspector>();
        _ops = ServiceLocator.Get<FileOperationService>();
        _actions = ServiceLocator.Get<ExternalActionRunner>();
        _toolLocator = ServiceLocator.Get<IToolLocator>();
        _undo = ServiceLocator.Get<UndoService>();
        _tracker = ServiceLocator.Get<OperationTracker>();
        _claims = ServiceLocator.Get<PathClaims>();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _log = Log.Current;
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

        var companionMetadata = ServiceLocator.TryGet<CompanionMetadataService>();
        _companionMetadata = companionMetadata;

        // Settings VM is owned by MainVM and shared with the dialog when it
        // opens. Built before every controller that takes it: RatingsController
        // used to be handed the property four lines above its assignment and
        // silently held null - the kind of bug the two-phase construction
        // below exists to make impossible. The side-effect subscription
        // (OnSettingsChanged) is at the very end of the constructor, after
        // RestoreState - see the note there.
        Settings = new SettingsViewModel();

        Ratings = new RatingsController(
            _fs, _companions, companionMetadata, _search, Settings, _log,
            isCurrent: _session.IsCurrent,
            publish: PublishRows,
            ask: question => _dialogs.Ask(new DialogRequest(
                DialogKind.CreateSidecar, Strings.ConfirmCreateSidecarTitle, question,
                DialogButtons.YesNo, DialogIcon.Question)));
        Ratings.HasRatingsChanged += (_, value) => HasRatings = value;
        Ratings.StatusReported += (_, line) => Say(line.Text, line.Severity);

        Preview = new PreviewController(
            ServiceLocator.TryGet<IImageMetadataReader>(),
            companionMetadata,
            _claims,
            _tracker) {
            // Both are assigned further down this constructor; the pane asks
            // only once something is on show. A row of a search inside files
            // carries its snippet, and the query is the search's (PLAN B6).
            Listing = () => Entries!,
            FindTextFor = entry => entry.MatchSnippet is not null && ContentSearch!.TextQuery.Length > 0
                ? ContentSearch.TextQuery
                : null,
        };
        // A click in the footer is about the whole selection the shown file
        // is part of. Split, each picture carries its own stars on its bar,
        // and a click there is about that picture alone (2026-09-24).
        Preview.RatingRequested += (_, request) =>
            request.Rating = ApplyRatingFromPane(request, wholeSelection: !IsPreviewSplit);
        Preview.RevealRequested += (_, path) => RevealPath(path);
        // The other half of a pair finds the search's text as the first one
        // does (PLAN B6, 2026-09-25): both files are rows of the results.
        PreviewSecond = new PreviewController(
            ServiceLocator.TryGet<IImageMetadataReader>(),
            companionMetadata) {
            ShowFooter = false,
            ShowPictureBar = true,
            FindTextFor = entry => entry.MatchSnippet is not null && ContentSearch!.TextQuery.Length > 0
                ? ContentSearch.TextQuery
                : null,
        };
        PreviewSecond.RatingRequested += (_, request) =>
            request.Rating = ApplyRatingFromPane(request, wholeSelection: false);
        PreviewSecond.RevealRequested += (_, path) => RevealPath(path);
        // One RAW switch for both halves: it lives in the footer, and the
        // footer belongs to the selection, not to the upper picture.
        Preview.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(PreviewController.ShowRawDecode)) {
                PreviewSecond.ShowRawDecode = Preview.ShowRawDecode;
            } else if (e.PropertyName == nameof(PreviewController.ShowSvgSource)) {
                // The same for the SVG switch (PLAN B8).
                PreviewSecond.ShowSvgSource = Preview.ShowSvgSource;
            }
        };
        // One set of review helpers for the window: the strip edits it,
        // both halves of the pane draw what it says.
        Helpers = new ReviewHelpers();
        Preview.SetHelpers(Helpers);
        PreviewSecond.SetHelpers(Helpers);
        _sharpness = new SharpnessController(
            ServiceLocator.TryGet<ISharpnessProbe>(), _log,
            isCurrent: _session.IsCurrent,
            rows: () => _search.Source,
            publish: PublishRows);
        Helpers.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(ReviewHelpers.Sharpness)) {
                _sharpness.SetActive(Helpers.Sharpness);
            }
        };
        // The gallery's cells draw the helpers themselves and ask for the
        // score of the frame they are on as they come on screen.
        ReviewThumb.Follow(Helpers);
        ReviewThumb.ScoreWanted += entry => _sharpness.Want(entry);
        Ratings.CompanionsChanged += (_, _) => {
            Preview.ReloadCompanions();
            PreviewSecond.ReloadCompanions();
        };

        _nav = new NavigationController(
            new NavigationService(), _fs, TryGetShellNamespace(), _log);
        // Its one report is a path that is not there.
        _nav.StatusReported += (_, text) => Fail(text);

        Entries = new BulkObservableCollection<FileSystemEntry>();
        Operations = new ObservableCollection<OperationViewModel>();

        Shell = new ShellCommandsController(_shell, _log);
        Shell.StatusReported += (_, line) => Say(line.Text, line.Severity);

        // The window's model (REDESIGN 4): both panels, where the keyboard
        // is, the open menu. A folder a panel opens is opened the way a
        // click on its row always has been - the folder itself selected.
        // The panels are its projection; the bookmarks hand it their rows.
        Workspace = new WorkspaceController(
            _fs, TryGetShellNamespace(), () => Settings.Visibility,
            NavigateAndSelectFolder, _dispatcher, _log);
        Workspace.StateChanged += OnWorkspaceChanged;
        Trees = new FolderTreesController(Workspace);
        Bookmarks = new BookmarksController(_fs, Settings, _log);
        Bookmarks.StatusReported += (_, text) => Status = text;
        Bookmarks.Changed += (_, _) => {
            PostBookmarks();
            SaveState();
            RaiseMissingFolder();
        };

        _tracker.Changed += OnTrackerChanged;

        // Every verb below acts on what TargetRules says it acts on: from a
        // key, the target as it is now; from a menu, the menu's snapshot,
        // handed to the command as its parameter (MenuCall). Whether it may
        // is a question about the place of that target (PlaceFacts), not
        // about the folder open in the list: a folder right-clicked in the
        // drives tree while the Recycle Bin is open has every verb.
        OpenCommand = new RelayCommand(Open, p => p is FileSystemEntry || TargetRules.Open(ResolveTarget(p).Target) != OpenRoute.None);
        // Destructive ops are blocked inside shell namespaces (Recycle Bin):
        // the entries' FullPaths point at $Recycle.Bin backing files, and
        // copying / deleting / renaming those would bypass the shell's
        // restore-tracking and corrupt the bin's state. Read-only browsing
        // only in this iteration.
        DeleteCommand = new RelayCommand(p => _ = DeleteTargetAsync(p, permanent: false), CanDelete);
        // The name from the prompt: confirmed with its own Enter, so the
        // keyboard goes onto the renamed row, as after the inline editor's.
        RenameCommand = new RelayCommand(p => Rename(_selectedEntry, p as string, takeFocus: true), _ => _selectedEntry is not null && !IsCurrentShellNamespace);
        BatchRenameCommand = new RelayCommand(
            BatchRename,
            p => ListRowsOf(p) is { } rows
                && BatchRenameGate.Classify(rows) is BatchRenameKind.Files or BatchRenameKind.Folders);
        // Copy is the one clipboard verb an archive still answers: it puts
        // the paths inside the archive on the clipboard, and Paste in a real
        // folder turns them into an extraction. The bin stays excluded -
        // pasting a $Recycle.Bin backing path copies a mangled file.
        CopyCommand = new RelayCommand(Copy, CanCopy);
        ExtractCommand = new RelayCommand(p => _ = ExtractSelectionAsync(p), CanExtractSelection);
        ExtractHereCommand = new RelayCommand(p => _ = ExtractHereAsync(p), CanExtractSelection);
        CutCommand = new RelayCommand(Cut, CanCut);
        PasteCommand = new RelayCommand(p => _ = PasteAsync(p), CanPaste);
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => _nav.Current is not null && !IsCurrentShellNamespace);
        RestoreFromRecycleBinCommand = new RelayCommand(
            _ => RestoreFromRecycleBin(),
            p => ResolveTarget(p).Place.IsRecycleBin && ListRowsOf(p) is not null);
        RefreshCommand = new RelayCommand(_ => RefreshOrRerunSearch());
        SetViewModeCommand = new RelayCommand(p => SetViewMode(p as string));
        SetViewAutoCommand = new RelayCommand(_ => SetViewAuto());
        MakeDefaultViewCommand = new RelayCommand(_ => MakeDefaultView());
        SetGalleryBackgroundCommand = new RelayCommand(p => SetGalleryBackground(p as string));
        FilterColorChoices = ColorLabelViewModel.CreateChoices();
        SetFilterRankCommand = new RelayCommand(p => SetFilterRank(p as string));
        SetRankForSelectionCommand = new RelayCommand(p => SetRankForSelection(p as string));
        SetFilterColorCommand = new RelayCommand(SetFilterColor);
        ClearRatingFilterCommand = new RelayCommand(_ => ClearRatingFilter(), _ => HasRatingFilter);
        SetSortKeyCommand = new RelayCommand(p => SetSortKey(p as string));
        ToggleSortAscendingCommand = new RelayCommand(_ => Settings.SortAscending = !Settings.SortAscending);
        ToggleGroupFoldersFirstCommand = new RelayCommand(_ => Settings.GroupFoldersFirst = !Settings.GroupFoldersFirst);
        // A close, not Shutdown: WPF ignores a cancelled close during
        // Shutdown, and the window asks before leaving operations behind.
        ExitCommand = new RelayCommand(_ => Application.Current?.MainWindow?.Close());
        OptionsCommand = new RelayCommand(_ => OpenSettingsDialog());
        ConfigureActionsCommand = new RelayCommand(
            _ => OpenSettingsDialog(Settings.Categories.OfType<ActionsSettingsCategory>().FirstOrDefault()));
        // The row carries the action's id - through a MenuCall from the menus.
        RunActionCommand = new RelayCommand(p => _ = RunActionAsync(p), p => !ResolveTarget(p).Place.IsReadOnly);
        RunActionToFolderCommand = new RelayCommand(p => _ = RunActionAsync(p, pickFolder: true), p => !ResolveTarget(p).Place.IsReadOnly);
        // GitHub's template chooser lets the user pick "Bug report" or
        // "Feature request"; nothing is pre-filled, so no session data is
        // involved — unlike the crash path, which bundles diagnostics.
        ReportIssueCommand = new RelayCommand(
            _ => Shell.OpenUrl(Diagnostics.CrashReporter.IssueChooserUrl));
        HelpCommand = new RelayCommand(_ => Shell.OpenUrl(Diagnostics.CrashReporter.GuideUrl));
        // Properties falls back to the folder being listed, so a background
        // right-click (and Alt+Enter with nothing selected) opens the
        // folder's own sheet — Explorer parity.
        PropertiesCommand = new RelayCommand(ShowProperties, p => TargetRules.PropertiesOf(ResolveTarget(p).Target, _nav.Current) is not null);
        OpenWithCommand = new RelayCommand(OpenWith, p => OpenWithTarget(p) is not null);
        OpenInTerminalCommand = new RelayCommand(OpenInTerminal, p => TerminalFolder(p) is not null);
        // The text they put on the clipboard is noted at once: Wander stays
        // in front, and Ctrl+V pastes it as a file (PLAN X).
        CopyPathCommand = new RelayCommand(
            p => {
                Shell.CopyPaths(TargetRules.CopyPaths(ResolveTarget(p).Target, _nav.Current));
                SyncClipboardFromSystem();
            },
            p => TargetRules.CopyPaths(ResolveTarget(p).Target, _nav.Current).Count > 0);
        CopyNameCommand = new RelayCommand(
            p => {
                Shell.CopyNames(TargetRules.Items(ResolveTarget(p).Target).Select(e => e.Name).ToArray());
                SyncClipboardFromSystem();
            },
            p => TargetRules.Items(ResolveTarget(p).Target).Count > 0);
        CreateShortcutCommand = new RelayCommand(
            CreateShortcutsForSelection,
            p => ListRowsOf(p) is not null && _nav.Current is not null && !ResolveTarget(p).Place.IsReadOnly);
        OpenJournalCommand = new RelayCommand(_ => OpenJournal(), _ => Journal.Count > 0);
        TogglePreviewCommand = new RelayCommand(_ => IsPreviewVisible = !IsPreviewVisible);
        ToggleFoldersCommand = new RelayCommand(_ => IsFoldersVisible = !IsFoldersVisible);
        UndoCommand = new RelayCommand(_ => UndoLast(), _ => _undo.CanUndo);
        PermanentDeleteCommand = new RelayCommand(p => _ = DeleteTargetAsync(p, permanent: true), CanDelete);
        OpenLogFileCommand = new RelayCommand(_ => Shell.OpenLogFile(), _ => ServiceLocator.IsRegistered<ILogFile>());
        DebugOperationCommand = new RelayCommand(p => _ = RunDebugOperationAsync(p as string));
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
        SearchResults.RowsChanged += (_, rows) => {
            _entriesAreResults = true;
            Entries.ReplaceAll(rows);
        };
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

            LandRows(filtered);
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

        RestoreState();
        // Restored settings decide how big the thumbnail caches may be; the
        // provider starts idle until it is told. Same for where scratch
        // copies go: AppPaths cannot read a setting, so it is told.
        ApplyThumbnailCacheSettings();
        AppPaths.UseSystemTemp = Settings.UseSystemTemp;

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
        Workspace.StateChanged += (before, after) => {
            if (!ReferenceEquals(before.Drives.Expanded, after.Drives.Expanded)
                || !ReferenceEquals(before.Bookmarks.Expanded, after.Bookmarks.Expanded)) {
                SaveState();
            }
        };
        // The restored view mode and palette decide the surround; the panes
        // were built before either was known.
        PushPalette();
        _ = LocateToolsAsync();
    }


    /// <summary>
    /// The rows of a folder the user walked into have landed. Carries the
    /// clock that has been running since the navigation, so the view can
    /// time the first screen from there (<c>FirstScreenWatch</c>).
    /// </summary>
    public event Action<string, System.Diagnostics.Stopwatch>? FolderArrived;

    /// <summary>
    /// Every line the status bar is given, repeats included - the property
    /// does not change for the same words twice. The full screen covers the
    /// status bar and says a warning or an error itself (PLAN Q5).
    /// </summary>
    public event EventHandler<StatusLine>? StatusSaid;


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
    /// — the pane takes it as its DataContext and binds to it directly.
    /// </summary>
    public PreviewController Preview { get; }

    /// <summary>
    /// The other half of a split pane: the second of exactly two selected
    /// files the pane can draw (<see cref="PreviewPair"/>). Fed only while
    /// <see cref="IsPreviewSplit"/> is on; the rest of the time it holds
    /// nothing and draws nothing.
    /// </summary>
    public PreviewController PreviewSecond { get; }

    /// <summary>
    /// Which review helpers are on (RAWHELPERS). The buttons on the strip
    /// over the list bind to it; the preview pane and the gallery's
    /// sharpness pass follow it.
    /// </summary>
    public ReviewHelpers Helpers { get; }

    /// <summary>
    /// Two files side by side in the preview column. Decided by the
    /// selection, not by a setting: it comes on with the pair and goes
    /// off with it.
    /// </summary>
    public bool IsPreviewSplit {
        get => _isPreviewSplit;
        private set {
            if (SetField(ref _isPreviewSplit, value)) {
                PreviewSecond.SetVisible(_isPreviewVisible && value);
                // Split, the first picture's stars and levels move from the
                // footer onto its own bar, as the second's are on its.
                Preview.ShowPictureBar = value;
            }
        }
    }

    /// <summary>
    /// The shapes of the split's two pictures as shown, read off their
    /// headers (2026-09-23): which way the pane splits is decided from them
    /// and from the pane (SplitOrientation). Null for a half that is not a
    /// picture or has not been read.
    /// </summary>
    public (PictureShape? First, PictureShape? Second) PreviewPairShapes { get; private set; }

    /// <summary>
    /// A preview for pictures shown on their own - the full screen (PLAN
    /// Q5): the window's helpers, surround and RAW switch; stars on its bar
    /// that write to the picture it shows, not to the selection. The window
    /// feeds it and lets it go (<see cref="PreviewController.Detach"/>).
    /// </summary>
    /// <param name="listing">What it decodes ahead from - the rows the window walks, for either half of a pair too.</param>
    public PreviewController NewPictureViewer(Func<IReadOnlyList<FileSystemEntry>>? listing) {
        var viewer = new PreviewController(ServiceLocator.TryGet<IImageMetadataReader>(), _companionMetadata) {
            ShowFooter = false,
            ShowPictureBar = true,
            PictureBarSwitches = true,
            Listing = listing,
        };
        viewer.RatingRequested += (_, request) => request.Rating = ApplyRatingFromPane(request, wholeSelection: false);
        viewer.SetHelpers(Helpers);
        viewer.SetPalette(ContentPalette);
        viewer.ShowRawDecode = Preview.ShowRawDecode;

        return viewer;
    }

    /// <summary>
    /// User preferences. XAML binds to this (e.g. tile sizes) and the
    /// settings dialog edits it directly. Side effects (re-listing the
    /// folder when ShowHidden flips, persisting to disk) run through
    /// <see cref="OnSettingsChanged"/>.
    /// </summary>
    public SettingsViewModel Settings { get; }

    private double _aggregateProgress;

    /// <summary>
    /// Everything in flight as one number, weighted by bytes where there
    /// are bytes: a 5 GB copy and a two-file delete are not half each.
    /// </summary>
    public double AggregateProgress {
        get => _aggregateProgress;
        private set => SetField(ref _aggregateProgress, value);
    }

    private string _operationsSummary = "";

    /// <summary>
    /// What the status bar says next to the bar: "Копирование: 45 %" for
    /// one, "Операций: 3 - 60 %" for several.
    /// </summary>
    public string OperationsSummary {
        get => _operationsSummary;
        private set => SetField(ref _operationsSummary, value);
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

    /// <summary>The status line as news. A warning or an error goes through <see cref="Warn"/> / <see cref="Fail"/>.</summary>
    public string Status {
        get => _status;
        set => Say(value, StatusSeverity.Info);
    }

    /// <summary>
    /// How much the status line matters: the status bar puts a mark in front
    /// of a warning and an error (PLAN block 0, step 8).
    /// </summary>
    public StatusSeverity StatusSeverity {
        get => _statusSeverity;
        private set => SetField(ref _statusSeverity, value);
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
    /// go stale or force the list to be rebuilt to move it. The window's
    /// model holds it (<c>WorkspaceState.List</c>); the row templates read it
    /// through <see cref="Converters.CaretRowConverter"/>.
    /// </para>
    /// </summary>
    public string? CaretPath => _caretPath;

    /// <summary>The list's main selected row - what Enter opens and F2 renames. The model's (<c>WorkspaceState.List</c>), as a row.</summary>
    public FileSystemEntry? SelectedEntry => _selectedEntry;

    /// <summary>The list's selected rows - the model's (<c>WorkspaceState.List</c>), as rows.</summary>
    public IReadOnlyList<FileSystemEntry> SelectedEntries => _selectedEntries;

    /// <summary>
    /// The rows are being laid down again: what the list says about its
    /// selection meanwhile is its own doing, not the user's (REDESIGN 4.8).
    /// </summary>
    public bool IsSyncingRows => _syncingRows;

    /// <summary>
    /// What the next operation is about (REDESIGN 4.3) - the rows of the
    /// list, a row of a panel, or nothing. Read, never set: it follows the
    /// window's model (<see cref="Workspace"/> - the keyboard, each panel's
    /// cursor, the open menu) and the list's selection.
    /// </summary>
    public Target Target => _target;

    /// <summary>The window's model and its one executor - the panels, the keyboard, the open menu.</summary>
    public WorkspaceController Workspace { get; }

    /// <summary>
    /// The keyboard is in <paramref name="zone"/> now, for
    /// <paramref name="reason"/>. Null for no zone (the window itself, a
    /// menu, the preview): the target then means what it meant in the last.
    /// </summary>
    public void NoteKeyboardZone(WindowZone? zone, ZoneReason reason) {
        Workspace.Post(new ZoneEntered(zone, reason));
    }

    /// <summary>
    /// A context menu is open on <paramref name="context"/>: until it closes,
    /// its subject is the target - the preview and the status bar describe
    /// what the menu is about.
    /// </summary>
    public void NoteMenuOpened(MenuContext context) {
        Workspace.Post(new MenuOpened(context));
    }

    /// <summary>The menu opened on <paramref name="context"/> has closed.</summary>
    public void NoteMenuClosed(MenuContext context) {
        Workspace.Post(new MenuClosed(context));
    }

    /// <summary>The window became the active one: the panels read their open levels again, once in a while (P-24).</summary>
    public void NoteWindowActivated() {
        Workspace.Post(new WindowActivated(WorkspaceController.Now));
    }

    public void NoteWindowDeactivated() {
        Workspace.Post(new WindowDeactivated());
    }

    /// <summary>
    /// Shows one of the window's modal dialogs, the model told around it:
    /// a closing dialog leaves the keyboard wherever WPF's first-focusable
    /// search lands in the owner window, and the model puts it back in the
    /// panel it was in, else in the list (K-3).
    /// </summary>
    private bool? ShowModal(Window dialog) {
        Workspace.Post(new DialogOpened());
        try {
            return dialog.ShowDialog();
        } finally {
            Workspace.Post(new DialogClosed());
        }
    }

    /// <summary>
    /// What a command runs on, and the place it is in: the menu's snapshot
    /// when the command came from a menu (<see cref="MenuCall"/>), the
    /// target as it is now when it came from a key or a button.
    /// </summary>
    public (Target Target, PlaceFacts Place) ResolveTarget(object? parameter) {
        return parameter is MenuCall call
            ? (call.Context.Subject, call.Context.Place)
            : (_target, PlaceOf(_target));
    }

    /// <summary>The snapshot for a menu about <paramref name="subject"/>, taken now.</summary>
    public MenuContext MenuContextFor(Target subject) {
        return MenuContext.For(subject, PlaceOf(subject), _clipboard.HasContent, SelectionIsArchive);
    }

    /// <summary>
    /// The snapshot for the list's own menu: its selection, or - on empty
    /// space - the open folder.
    /// </summary>
    public MenuContext MenuContextForList(bool isBackground) {
        return MenuContextFor(isBackground
            ? Target.OfBackground(_nav.Current)
            : Target.OfRows(_selectedEntries, _selectedEntry));
    }

    /// <summary>
    /// The target read again from its facts. Logged once per change of what
    /// it is (N11) - not per event behind it: the keyboard arriving in a
    /// panel and the panel's cursor moving there used to log it twice.
    /// </summary>
    private void UpdateTarget() {
        var next = TargetRules.Of(TargetRules.FactsOf(Workspace.State) with {
            ListSelection = _selectedEntries,
            ListPrimary = _selectedEntry,
        });
        bool moved = !next.SameAs(_target);
        _target = next;
        if (moved) {
            // The model's trace (WorkspaceController): the derived target,
            // one line per change of it - not per event behind it (N11).
            Log.Detail($"WS target: {next.Describe()}");
        }
    }

    /// <summary>The model moved: the list's selection and caret as rows, and the target read again.</summary>
    private void OnWorkspaceChanged(WorkspaceState before, WorkspaceState after) {
        if (!ReferenceEquals(before.List, after.List)) {
            ProjectList(after.List);
        }
        OnTargetFactsChanged();
    }

    /// <summary>
    /// The list's selection and caret from the model, as rows of the listing
    /// on screen - what the preview, the status bar and every command read.
    /// The rows are looked up anew each time: a rating swaps a row for an
    /// updated copy under the same path, and the selection must hold the
    /// copy on screen.
    /// </summary>
    private void ProjectList(ListState list) {
        var byPath = new Dictionary<string, FileSystemEntry>(Entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries) {
            byPath.TryAdd(entry.FullPath, entry);
        }

        var rows = new List<FileSystemEntry>(list.Selection.Length);
        foreach (string path in list.Selection) {
            if (byPath.TryGetValue(path, out var row)) {
                rows.Add(row);
            }
        }
        var primary = list.Primary is { } main && byPath.TryGetValue(main, out var found) ? found : rows.FirstOrDefault();

        bool rowsMoved = rows.Count != _selectedEntries.Count || rows.Where((r, i) => !ReferenceEquals(r, _selectedEntries[i])).Any();
        bool primaryMoved = !ReferenceEquals(primary, _selectedEntry);
        bool caretMoved = !string.Equals(list.Caret, _caretPath, StringComparison.Ordinal);
        _selectedEntries = rows;
        _selectedEntry = primary;
        _caretPath = list.Caret;

        if (rowsMoved) {
            // The action trace: what is selected now, whoever moved it.
            Log.Detail($"Selection: {rows.Count} item(s){(rows.Count > 0 ? ", first " + rows[0].FullPath : "")}");
            Raise(nameof(SelectedEntries));
            NoteSelectionKind();
        }
        if (primaryMoved) {
            Raise(nameof(SelectedEntry));
        }
        if (caretMoved) {
            Raise(nameof(CaretPath));
        }

        if (rowsMoved || primaryMoved) {
            UpdateTarget();
            SyncPreviewSelection();
            Raise(nameof(SelectionSummary));
        } else if (caretMoved && rows.Count > 1) {
            // Inside a multi-selection the caret is the file the user just
            // added - the one the preview should be showing.
            RefreshPreviewPrimary();
        }
    }

    /// <summary>A fact the target is read from changed: the preview and the status bar follow a target that moved.</summary>
    private void OnTargetFactsChanged() {
        var was = _target;
        UpdateTarget();
        if (!was.SameAs(_target)) {
            SyncPreviewSelection();
            Raise(nameof(SelectionSummary));
        }
    }

    /// <summary>
    /// The place of <paramref name="target"/>. A panel row is its own place -
    /// an ordinary folder, unless it is in an archive or is the bin; rows of
    /// the list and the empty space are in the open folder.
    /// </summary>
    private PlaceFacts PlaceOf(Target target) {
        if (target.Kind != TargetKind.PanelRow) {
            return new PlaceFacts(IsCurrentShellNamespace, IsCurrentRecycleBin, CurrentArchive is not null);
        }

        if (_panelPlace is not { } known || !known.Row.SameAs(target)) {
            known = (target, new PlaceFacts(IsReadOnly: IsShellPath(target.Folder), IsRecycleBin: false, IsArchive: false));
            _panelPlace = known;
        }

        return known.Place;
    }

    /// <summary>The rows of the list a command is about, or null when it is about something else.</summary>
    private IReadOnlyList<FileSystemEntry>? ListRowsOf(object? parameter) {
        return ResolveTarget(parameter).Target is { Kind: TargetKind.ListRows } target ? target.Rows : null;
    }

    /// <summary>
    /// Hands the target to the preview. The footer describes all of it; the
    /// picture above is the active row, or - for exactly two files the pane
    /// can draw - both, upper one first in listing order whichever was
    /// clicked last (<see cref="PreviewSubject"/>). A panel row's folder is
    /// read off the UI thread and shown once it is (<see cref="ShowPanelFolder"/>).
    /// </summary>
    private void SyncPreviewSelection() {
        var subject = PreviewSubject.Of(_target, _caretPath, Entries);
        if (subject.Kind == PreviewSubjectKind.Folder) {
            ShowPanelFolder(subject.Folder!);

            return;
        }

        _panelFolderCts?.Cancel();
        _panelFolderPath = null;
        Preview.SetSelection(subject.Selection);
        if (subject.Pair is { } p) {
            PreviewSecond.SetSelection(new[] { p.Second });
            PreviewSecond.SetPrimary(p.Second);
        } else {
            PreviewSecond.SetSelection(Array.Empty<FileSystemEntry>());
            PreviewSecond.SetPrimary(null);
        }
        Preview.SetPrimary(subject.Primary);
        ShowPair(subject.Pair);
    }

    /// <summary>
    /// The split for a pair, once its pictures' shapes are known: which way
    /// it splits depends on them, and a split that turned over after its
    /// pictures appeared would be one frame too many on screen (pillar 4).
    /// The headers are read on the pool - milliseconds; the second half does
    /// not load while it is hidden, so it comes up already the right way.
    /// A new pair under a split already on screen keeps it meanwhile.
    /// </summary>
    private void ShowPair((FileSystemEntry First, FileSystemEntry Second)? pair) {
        if (pair is not { } p) {
            HidePair();

            return;
        }

        var paths = (p.First.FullPath, p.Second.FullPath);
        if (_pairShown is { } shown && PathsEqual(shown.First, paths.Item1) && PathsEqual(shown.Second, paths.Item2)) {
            IsPreviewSplit |= _pairShapesRead;

            return;
        }

        _pairShown = paths;
        _pairShapesRead = false;
        // Not raised: a split already on screen stays as it is until the
        // new shapes are in; one coming up on the wait counts both as square.
        PreviewPairShapes = (null, null);
        _pairShapesCts?.Cancel();
        _pairShapesCts = new CancellationTokenSource();
        _ = ShowPairAsync(p.First.FullPath, p.Second.FullPath, _pairShapesCts.Token);
    }

    private async Task ShowPairAsync(string first, string second, CancellationToken ct) {
        var reader = ServiceLocator.TryGet<IImageMetadataReader>();
        var read = Task.Run(() => (ShapeOf(first), ShapeOf(second)), ct);
        await Task.WhenAny(read, Task.Delay(PairShapesWaitMs, ct));
        if (ct.IsCancellationRequested) {
            return;
        }

        // Past the wait the split comes up with what is known; the shapes
        // still count when they arrive, and the split turns only when that
        // clearly shows the two bigger (SplitOrientation.TurnAbove).
        if (!read.IsCompleted) {
            IsPreviewSplit = true;
        }
        (PictureShape?, PictureShape?) shapes;
        try {
            shapes = await read;
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            _log.Warn($"Preview pair: shapes not read ({ex.Message})");
            shapes = (null, null);
        }
        if (ct.IsCancellationRequested) {
            return;
        }

        PreviewPairShapes = shapes;
        _pairShapesRead = true;
        Raise(nameof(PreviewPairShapes));
        IsPreviewSplit = true;

        PictureShape? ShapeOf(string path) {
            return PreviewRouter.Route(path) is PreviewRoute.Image ? PictureLoader.ShapeOf(path, reader) : null;
        }
    }

    /// <summary>No pair: the split goes, and a read of its shapes is let go of.</summary>
    private void HidePair() {
        _pairShapesCts?.Cancel();
        _pairShown = null;
        _pairShapesRead = false;
        IsPreviewSplit = false;
    }

    /// <summary>
    /// Points the main pane at the file it should be showing when only the
    /// active row may have moved. Called from every setter that can change
    /// the answer, because the list reports its current item, its selection
    /// and its caret as three separate events in no fixed order; each call is
    /// cheap when nothing moved. A panel row's folder is not re-read for it.
    /// </summary>
    private void RefreshPreviewPrimary() {
        var subject = PreviewSubject.Of(_target, _caretPath, Entries);
        if (subject.Kind != PreviewSubjectKind.Folder) {
            Preview.SetPrimary(subject.Primary);
        }
    }

    /// <summary>
    /// A panel row's folder in the preview: read on the pool - the row is not
    /// in the listing, and a stat on the UI thread per arrow key was the old
    /// price of it - and shown if it is still the target by then. What was
    /// on show stays until it is.
    /// </summary>
    private void ShowPanelFolder(string folder) {
        if (_panelFolderPath is { } shown && PathsEqual(shown, folder)) {
            return;
        }

        _panelFolderPath = folder;
        _panelFolderCts?.Cancel();
        _panelFolderCts = new CancellationTokenSource();
        _ = ShowPanelFolderAsync(folder, _panelFolderCts.Token);
    }

    private async Task ShowPanelFolderAsync(string folder, CancellationToken ct) {
        FileSystemEntry? entry;
        try {
            entry = await Task.Run(() => _fs.GetEntry(folder), ct);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            _log.Warn($"Panel row not readable: {folder} ({ex.Message})");
            entry = null;
        }
        if (ct.IsCancellationRequested || _target.Kind != TargetKind.PanelRow || !PathsEqual(_target.Folder!, folder)) {
            return;
        }

        // Gone, or not a folder on disk (the bin, a folder of an archive):
        // the pane describes the open folder, as it did before.
        var shown = entry is null ? Array.Empty<FileSystemEntry>() : new[] { entry };
        Preview.SetSelection(shown);
        Preview.SetPrimary(entry);
        PreviewSecond.SetSelection(Array.Empty<FileSystemEntry>());
        PreviewSecond.SetPrimary(null);
        HidePair();
    }

    /// <summary>
    /// True when everything selected is an archive Wander can open — the
    /// precondition for "Извлечь..." outside an archive. A field rather than
    /// a computed property for the same reason as
    /// <see cref="IsCurrentShellNamespace"/>: the answer ends in a
    /// <c>File.Exists</c> per row, and <c>CanExecute</c> asks constantly.
    /// </summary>
    public bool SelectionIsArchive { get; private set; }

    /// <summary>
    /// "Выбрано: 3 · 1.2 MB" - what the target amounts to, for the status
    /// bar: the list's rows, or a panel row's folder while the keyboard is
    /// there. Empty when there is no target, so the field disappears rather
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
            var items = TargetRules.Items(_target);
            if (items.Count == 0) {
                return "";
            }

            long bytes = 0;
            int folders = 0;
            foreach (var entry in items) {
                if (entry.IsFolderLike) {
                    folders++;
                } else {
                    bytes += entry.Size ?? 0;
                }
            }

            string text = string.Format(
                Strings.StatusSelection, items.Count, SizeFormatter.Format(bytes));

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
    /// The view on screen. Written by the user (<see cref="SetViewModeCommand"/>,
    /// which also pins the choice to the open folder) and by
    /// <see cref="ChooseView"/> on arrival; the setter itself saves nothing
    /// - what persists is the pin and the default, not the screen.
    /// </summary>
    public ViewMode ViewMode {
        get => _viewMode;
        set {
            if (SetField(ref _viewMode, value)) {
                Raise(nameof(ContentPalette));
                PushPalette();
            }
        }
    }

    /// <summary>Why the open folder is drawn the way it is - the caption of the View menu.</summary>
    public ViewReason ViewReason {
        get => _viewReason;
        private set {
            if (SetField(ref _viewReason, value)) {
                Raise(nameof(ViewCaption));
                Raise(nameof(IsViewAuto));
            }
        }
    }

    /// <summary>"This folder: pinned / auto: pictures / default" - the View menu's caption.</summary>
    public string ViewCaption => string.Format(Strings.MenuViewThisFolder, _viewReason switch {
        ViewReason.Pinned => Strings.ViewReasonPinned,
        ViewReason.Pictures => Strings.ViewReasonPictures,
        _ => Strings.ViewReasonDefault,
    });

    /// <summary>The check mark on "Automatically": no pin on the open folder.</summary>
    public bool IsViewAuto => _viewReason != ViewReason.Pinned;

    /// <summary>The preview panes draw on the same surround as the list; they are told when it changes.</summary>
    private void PushPalette() {
        Preview.SetPalette(ContentPalette);
        PreviewSecond.SetPalette(ContentPalette);
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
                PreviewSecond.SetVisible(value && _isPreviewSplit);
                SaveState();
            }
        }
    }

    /// <summary>
    /// Whether the folders pane (bookmarks and the drives tree) is on
    /// screen. Put away, its width is kept in <see cref="FoldersWidth"/>
    /// and comes back with it - the same arrangement as the preview pane
    /// and <see cref="PreviewWidth"/>.
    /// </summary>
    public bool IsFoldersVisible {
        get => _isFoldersVisible;
        set {
            if (SetField(ref _isFoldersVisible, value)) {
                SaveState();
            }
        }
    }

    public double PreviewWidth {
        get => _previewWidth;
        set {
            double clamped = Math.Max(PreviewMinWidth, Math.Min(PaneCeiling(_windowWidth, ListMinWidth), value));
            if (SetField(ref _previewWidth, clamped)) {
                RebasePaneSizes();
                SaveState();
            }
        }
    }

    /// <summary>
    /// Width of the folders pane on the left, in pixels - where the user
    /// left the divider between it and the file list. Same arrangement as
    /// <see cref="PreviewWidth"/>: the window applies it to the grid, the
    /// view model persists it with the window size beside it.
    /// </summary>
    public double FoldersWidth {
        get => _foldersWidth;
        set {
            double clamped = Math.Max(FoldersMinWidth, Math.Min(PaneCeiling(_windowWidth, ListMinWidth), value));
            if (SetField(ref _foldersWidth, clamped)) {
                RebasePaneSizes();
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
            double clamped = Math.Max(BookmarksMinHeight, Math.Min(PaneCeiling(_windowHeight, TreeMinHeight), value));
            if (SetField(ref _bookmarksHeight, clamped)) {
                RebasePaneSizes();
                SaveState();
            }
        }
    }

    /// <summary>
    /// What <c>state.json</c> keeps is not what is on screen but what the
    /// user set, with the window they set it in. On a window of another
    /// size the panes are shown scaled (<see cref="RestorePaneSizes"/>),
    /// and saving those scaled sizes back was what made a monitor - laptop
    /// - monitor round trip come home a few pixels off: rounding, and the
    /// minimums, each way. So the pair is rebased only here - the user
    /// dragged a divider, and all three sizes are now theirs at this
    /// window. A file from before the window size was kept beside the
    /// sizes stays as it is until then, held to what leaves the list its
    /// room (<see cref="PaneSizes.Restore"/>), and the first drag rebases
    /// it at the window it happens in.
    /// </summary>
    private void RebasePaneSizes() {
        _savedPreviewWidth = _previewWidth;
        _savedFoldersWidth = _foldersWidth;
        _savedBookmarksHeight = _bookmarksHeight;
        _savedWindowWidth = _windowWidth;
        _savedWindowHeight = _windowHeight;
    }

    /// <summary>The pair that goes to <c>state.json</c>, for the two log lines that watch it: State loaded, State written.</summary>
    private string DescribeSavedPanes() {
        return $"folders {_savedFoldersWidth:F0}, preview {_savedPreviewWidth:F0}, bookmarks {_savedBookmarksHeight:F0} " +
            $"(expanded {_isBookmarksExpanded}) at {_savedWindowWidth:F0}x{_savedWindowHeight:F0}";
    }

    /// <summary>
    /// The most a side pane may take: the window less what its neighbour
    /// keeps, or the old fixed ceiling while the window size is not known
    /// yet - the same bound <see cref="PaneSizes.Restore"/> puts on a size
    /// coming back from disk, so a drag and a restore agree.
    /// </summary>
    private static double PaneCeiling(double window, double reserve) {
        return window > 0 ? window - reserve : PaneSizes.LegacyMax;
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

    /// <summary>Opens the batch-rename window on the selection: two or more, all files or all folders.</summary>
    public RelayCommand BatchRenameCommand { get; }

    /// <summary>Runs a catalog action, named by its id, over the selection.</summary>
    public RelayCommand RunActionCommand { get; }

    /// <summary>The same, with the outputs going to a folder the user picks first.</summary>
    public RelayCommand RunActionToFolderCommand { get; }

    /// <summary>The settings dialog, opened on the actions page.</summary>
    public RelayCommand ConfigureActionsCommand { get; }

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

    /// <summary>The same, into the folder the archive sits in, asking nothing.</summary>
    public RelayCommand ExtractHereCommand { get; }

    public RelayCommand SetViewModeCommand { get; }

    /// <summary>Takes the pin off the open folder; the view is chosen for it again.</summary>
    public RelayCommand SetViewAutoCommand { get; }

    /// <summary>The view on screen becomes the setting for folders without a pin.</summary>
    public RelayCommand MakeDefaultViewCommand { get; }

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

    /// <summary>The debug menu's fake operation; the parameter names the scenario (PLAN AI1).</summary>
    public RelayCommand DebugOperationCommand { get; }
    public RelayCommand ToggleBookmarksCommand { get; }
    public RelayCommand ToggleFoldersCommand { get; }
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

    /// <summary>
    /// The folder's name - with a warning appended while this instance is
    /// standing aside for the installed copy and saving nothing
    /// (<see cref="IAppStateStore.IsReadOnly"/>): a bookmark added in such
    /// a session is gone at the next start, and the title is where that is
    /// said before it happens.
    /// </summary>
    public string WindowTitle =>
        _stateStore.IsReadOnly ? string.Format(Strings.TitleYielding, _nav.WindowTitle) : _nav.WindowTitle;


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

    /// <summary>
    /// Enter and "Open": the list's current row is opened, a panel row's
    /// folder is gone to - the same as a click on the row. A row handed in
    /// directly is opened as it is.
    /// </summary>
    private void Open(object? parameter) {
        if (parameter is FileSystemEntry entry) {
            OpenEntry(entry);

            return;
        }

        var target = ResolveTarget(parameter).Target;
        switch (TargetRules.Open(target)) {
            case OpenRoute.ListRow:
                OpenEntry(target.Primary);
                break;
            case OpenRoute.PanelRow:
                NavigateAndSelectFolder(target.Folder!, target.Pane == Pane.Bookmarks ? NavigationSource.Bookmark : NavigationSource.Drives);
                break;
        }
    }

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
                Fail(string.Format(Strings.StatusOpenFailed, ex.Message));
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
            Fail(Strings.StatusNoDropTarget);
            return;
        }

        // Entries dragged out of an archive take the one route bytes leave
        // an archive by - the same one a paste of them takes, with its own
        // progress, conflict dialog and undo. The modifiers do not apply:
        // there is nothing to move and nothing to point a shortcut at.
        if (sourcePaths.Any(Archives.Inside)) {
            await ExtractAsync(sourcePaths, targetFolder);
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
        if (effect == DropEffect.Move) {
            ReleasePreview(groups.SelectMany(g => g.All));
        }
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
            Fail(string.Format(Strings.StatusDropFailed, ex.Message));
            return;
        } finally {
            RestorePreview();
        }

        if (!FollowMoved(results, targetFolder, moved: effect == DropEffect.Move)) {
            Refresh();
        }
        ReportBatchResults(results, effect == DropEffect.Move ? Strings.VerbMoved : Strings.VerbCopied, targetFolder);
    }


    /// <summary>
    /// What the right-button drop menu is built from. The payload of a drag
    /// carries the companions (a RAW's .xmp and .pp3 travel with it), and
    /// a copy or a move wants them; an action does not - "convert" is about
    /// the pictures, and a sidecar among the items would make every image
    /// action inapplicable. So the items are grouped the way the
    /// operations group them, and only the primaries are what the actions
    /// see and the caption counts. Looked up on the pool - a drop can be a
    /// thousand files - and out of an archive not at all: nothing runs on
    /// an entry that is not a file yet.
    /// </summary>
    public async Task<DropMenuTarget> DescribeDropAsync(IReadOnlyList<string> paths, string target, bool moveByDefault) {
        bool fromArchive = paths.Any(Archives.Inside);
        var primaries = fromArchive
            ? paths
            : await Task.Run(() => GroupPathsWithCompanions(paths).Select(g => g.Primary).ToArray());
        var entries = fromArchive
            ? Array.Empty<FileSystemEntry>()
            : await Task.Run(() => primaries.Select(_fs.GetEntry).OfType<FileSystemEntry>().ToArray());

        return new DropMenuTarget {
            Paths = primaries,
            Entries = entries,
            TargetFolder = target,
            MoveByDefault = moveByDefault,
            FromArchive = fromArchive,
            Actions = Settings.Actions,
            MissingTools = MissingTools,
            Settings = MenuSettings,
        };
    }


    /// <summary>
    /// Brings the rest of the window in line after a copy or a move. The
    /// folder panels re-read the folders that lost or gained a subfolder -
    /// the watcher only covers the folder on screen. After a move, a
    /// bookmark on a moved folder follows it, the history follows it, and
    /// when the folder on screen - or one above it - is what moved, the
    /// listing goes on where the folder is now instead of showing "folder
    /// is gone" over its old path.
    /// </summary>
    /// <returns>True when the listing was re-pointed; the caller then must not re-list the old path.</returns>
    private bool FollowMoved(IReadOnlyList<BatchItemResult> results, string target, bool moved) {
        var landed = results
            .Where(r => r.Status is BatchItemStatus.Ok or BatchItemStatus.Replaced or BatchItemStatus.Renamed or BatchItemStatus.Merged)
            .ToList();
        if (landed.Count == 0) {
            return false;
        }

        var moves = moved
            ? landed.Select(r => (r.Source, r.FinalDestination)).ToArray()
            : Array.Empty<(string, string)>();

        return FollowRelocated(moves, target);
    }


    /// <summary>
    /// <see cref="FollowMoved"/> from the moves alone - and what
    /// <c>Ctrl+Z</c> of a move runs, with the pairs pointing back
    /// (<see cref="IUndoableAction.MovesOnUndo"/>). The panels re-read both
    /// ends of every move, and <paramref name="target"/>: a copy moves
    /// nothing, yet the folder it went into gained a subfolder.
    /// </summary>
    /// <returns>True when the listing was re-pointed; the caller then must not re-list the old path.</returns>
    private bool FollowRelocated(IReadOnlyList<(string From, string To)> moves, string? target) {
        // Who is told, and in what order, is PathFollowing's rule (REDESIGN
        // 4.12); what each does with it is its own.
        var plan = PathFollowing.Plan(moves);

        // The panels: every row on a moved folder, or under it, takes the new
        // path where it stands, in both panels - open if it was open, the
        // cursor still on it (PanelRules) - and every folder that lost or
        // gained a subfolder is read again: a panel level is only read while
        // it is open, and it is folders that show there. All of it ahead of
        // the history, which navigates: the open folder's place is looked
        // for in a level being read again, and the model waits for that
        // answer rather than giving up on a level that does not hold the
        // folder yet (2026-09-22).
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (target is not null) {
            touched.Add(target);
        }
        foreach (var (_, from, to) in plan.Where(s => s.Holder == PathHolder.Panels)) {
            Workspace.Post(new Relocated(from, to));
            foreach (string? parent in new[] { Path.GetDirectoryName(from), Path.GetDirectoryName(to) }) {
                if (parent is { Length: > 0 }) {
                    touched.Add(parent);
                }
            }
        }
        foreach (string folder in touched) {
            Workspace.Post(new FolderChanged(folder));
        }

        bool followed = false;
        foreach (var (holder, from, to) in plan.Where(s => s.Holder != PathHolder.Panels)) {
            switch (holder) {
                case PathHolder.Bookmarks:
                    Bookmarks.Follow(from, to);
                    break;
                case PathHolder.FolderBook:
                    _foldersDirty |= _folders.Follow(from, to) > 0;
                    break;
                case PathHolder.RecentPaths:
                    _nav.RewriteRecentPaths(from, to);
                    break;
                case PathHolder.SelectionMemory:
                    // Where the user was in each folder, the folder whose
                    // rows are on screen, and the rows selected there: a
                    // re-listing after the move is not an arrival, and the
                    // selection follows its rows to the new path.
                    _session.RewriteMemory(from, to);
                    _landedFolder = PathRewrite.Under(_landedFolder, from, to) ?? _landedFolder;
                    _landingRenames.Add((from, to));
                    break;
                case PathHolder.Clipboard:
                    // Files cut inside the moved folder are found by the paste
                    // (decision B22) - while the clipboard is still ours.
                    _clipboard.Rewrite(from, to);
                    break;
                case PathHolder.History:
                    _followingMove = true;
                    try {
                        if (_nav.RewritePaths(from, to)) {
                            followed = true;
                            // Written always, like a navigation: the listing
                            // was re-pointed without one, and NavigateTo is
                            // what would have logged it.
                            _log.Info($"Listing follows: {from} -> {to}");
                        }
                    } finally {
                        _followingMove = false;
                    }
                    break;
            }
        }
        SaveState();

        return followed;
    }

    /// <summary>
    /// Re-reads, in both folder panels, the folders that hold
    /// <paramref name="paths"/> - they just lost or regained an item, and
    /// the watcher only covers the folder on screen.
    /// </summary>
    private void RefreshTreesAbove(IEnumerable<string> paths) {
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths) {
            if (Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } parent) {
                parents.Add(parent);
            }
        }

        foreach (string parent in parents) {
            Workspace.Post(new FolderChanged(parent));
        }
    }

    /// <summary>The bookmarks' rows handed to the model whole: it keeps what is open, the cursor and the place by path.</summary>
    private void PostBookmarks() {
        Workspace.Post(new BookmarksChanged(Bookmarks.BuildRows()));
    }

    /// <summary>The settings the model's rules read, copied in.</summary>
    private void PostWorkspaceOptions() {
        Workspace.Post(new OptionsChanged(new WorkspaceOptions(Settings.TreeKeyboardNavigates, Settings.ShowHidden)));
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
                Fail(string.Format(Strings.StatusShortcutFailed, srcName, ex.Message));
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
    /// handler, the first moment there is a window with a size to scale
    /// against, and again from ContentRendered: at Loaded the window is
    /// not yet the size it comes up in - one restored maximized is still
    /// at its normal bounds there (1762x700 in the log, 2062x1118 once
    /// shown), and panes scaled to that came back short on every start
    /// (2026-09-17). A pure function of the saved pair and the window, so
    /// the second call for the same size gives the same answer; a guard
    /// against "the same window" here compared with the size SizeChanged
    /// had already noted, and skipped the call that mattered.
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
        if (_savedFoldersWidth > 0) {
            _foldersWidth = PaneSizes.Restore(
                _savedFoldersWidth, _savedWindowWidth, windowWidth, FoldersMinWidth, ListMinWidth);
            Raise(nameof(FoldersWidth));
        }
        if (_savedBookmarksHeight > 0) {
            _bookmarksHeight = PaneSizes.Restore(
                _savedBookmarksHeight, _savedWindowHeight, windowHeight, BookmarksMinHeight, TreeMinHeight);
            Raise(nameof(BookmarksHeight));
        }

        // One line per call, so a report of "the pane came back wrong"
        // arrives with the numbers it is about, and says which window the
        // panes were last sized for.
        _log.Info(
            $"Pane sizes: window {windowWidth:F0}x{windowHeight:F0}, set at {_savedWindowWidth:F0}x{_savedWindowHeight:F0}; " +
            $"folders {_foldersWidth:F0} (saved {_savedFoldersWidth:F0}), preview {_previewWidth:F0} (saved {_savedPreviewWidth:F0}), " +
            $"bookmarks {_bookmarksHeight:F0} (saved {_savedBookmarksHeight:F0})");
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
        // Before the first navigation, so its lines are written the way
        // the settings say.
        ApplyLogSettings();

        // Until the first folder lands and ChooseView decides, the list
        // wears the default rather than a hard-coded view.
        _viewMode = Settings.DefaultViewMode;
        Raise(nameof(ViewMode));

        // The records about folders - read in one go like state.json; a
        // few thousand one-line records parse in milliseconds. The pins of
        // 0.4.x lived in state.json (SessionState.ManualViewModes): carried
        // over once, into the book, unless the book already has a word on
        // that folder. A mode name that no longer parses (a renamed enum
        // member, a hand-edited file) is dropped rather than guessed at: the
        // automatic choice is a fine fallback.
        _folders = new FolderSettingsBook(_folderStore.Load());
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var folder in session.ManualViewModes) {
            if (!string.IsNullOrEmpty(folder.Path)
                && Enum.TryParse<ViewMode>(folder.Mode, out var saved)
                && _folders.Find(folder.Path) is null) {
                _foldersDirty |= _folders.SetView(folder.Path, saved, createdUtc: null, today);
            }
        }
        if (_foldersDirty) {
            _log.Info($"Pinned views moved from state.json into folders.json: {session.ManualViewModes.Count}");
        }

        _isPreviewVisible = session.IsPreviewVisible;
        Raise(nameof(IsPreviewVisible));
        Preview.SetVisible(_isPreviewVisible);
        PreviewSecond.SetVisible(_isPreviewVisible && _isPreviewSplit);
        _isFoldersVisible = session.IsFoldersVisible;
        Raise(nameof(IsFoldersVisible));
        // Not applied here: what a saved pane size means depends on the
        // window it was saved from and the one it is coming back into, and
        // there is no window yet - the constructor runs before it exists.
        // RestorePaneSizes, called from MainWindow.OnLoaded, finishes this.
        _savedPreviewWidth = session.PreviewWidth;
        _savedFoldersWidth = session.FoldersWidth;
        _savedBookmarksHeight = session.BookmarksHeight;
        _savedWindowWidth = session.LayoutWindowWidth;
        _savedWindowHeight = session.LayoutWindowHeight;

        Bookmarks.Load(state.Favorites);
        _isBookmarksExpanded = session.IsBookmarksExpanded;
        Raise(nameof(IsBookmarksExpanded));
        _log.Info($"State loaded: {DescribeSavedPanes()}{(_stateStore.IsReadOnly ? "; read-only, nothing will be written" : "")}");

        // A remembered branch whose folder has gone opens as far as it
        // still goes; one on a medium that is not the machine's own stays
        // closed (NavigationFallback.AfterRestore). Answered here, on the UI
        // thread.
        var expanded = session.ExpandedPaths
            .Select(stop => NavigationFallback.AfterRestore(stop.Path, IsStillThere, VolumeKindOf) is { } path
                ? stop with { Path = path }
                : null)
            .OfType<NavigationStop>()
            .ToArray();

        // The model's settings first: they decide how it reads. The
        // bookmarks before the first navigation - a folder opened from them
        // has its place looked for among them, and with none there it would
        // fall back to the drives. Then the panels come up: the branches
        // saved open open again, their levels read off the UI thread, the
        // drives listed there too - nothing of it before the first frame.
        PostWorkspaceOptions();
        PostBookmarks();
        Workspace.Post(new WorkspaceStarted(expanded));

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
    /// is still there, its nearest surviving ancestor when it has gone from
    /// one of the machine's own drives, the working folder (a setting; the
    /// system Documents by default) when it was on a medium that has most
    /// likely been taken out, and the first drive when nothing was
    /// remembered or the working folder is not there either. Whether it is still there is asked on
    /// the pool - that one <c>DirectoryExists</c> used to sit on the UI
    /// thread before the first frame, and a session closed on a drive that
    /// has since spun down or been unplugged made the next start wait for
    /// it. A user who went somewhere themselves while the disk was thinking
    /// is left where they went.
    /// </summary>
    private async Task OpenStartFolderAsync(NavigationStop? remembered) {
        // Read here, on the UI thread, and carried into the pool.
        string? work = Settings.ResolveWorkFolder();
        var (restored, home, first) = await Task.Run(() => {
            string? back = remembered is null ? null : NavigationFallback.AfterRestore(remembered.Path, CanOpen, VolumeKindOf);
            string? parked = remembered is not null && back is null && work is not null && _fs.DirectoryExists(work) ? work : null;
            // The drives are listed on the pool as well: the panel reads
            // them there, and this is before the first frame.
            string? drive = back is null && parked is null ? _fs.GetRoots().FirstOrDefault()?.FullPath : null;

            return (back, parked, drive);
        });
        if (_nav.Current is not null) {
            return;
        }

        if (restored is not null) {
            if (!string.Equals(restored, remembered!.Path, StringComparison.OrdinalIgnoreCase)) {
                _log.Info($"Start: {remembered.Path} is gone, opening {restored}");
            }
            _nav.NavigateTo(restored, remembered.Source);
        } else if (home is not null) {
            _log.Info($"Start: {remembered!.Path} is not on this machine's own drives any more, opening the working folder {home}");
            _nav.NavigateTo(home, NavigationSource.External);
        } else if (first is not null) {
            _nav.NavigateTo(first, NavigationSource.External);
        }
        // The initial navigation ends in SaveState like any other, and the
        // restored folder is not a change worth writing back.
        _stateSaveTimer.Stop();
    }

    /// <summary>
    /// Whether a remembered place is still there, cheaply enough for the UI
    /// thread: a folder on disk is asked about, the bin is always there, and
    /// an archive is taken as there while its file is - what is inside it
    /// the tree finds out as it expands.
    /// </summary>
    private bool IsStillThere(string path) {
        if (!IsShellPath(path)) {
            return _fs.DirectoryExists(path);
        }

        return Archives.Of(path) is not { } archive || _fs.FileExists(archive.Archive);
    }

    /// <summary>
    /// <see cref="IsStillThere"/> in full, for the pool: an archive path is
    /// opened to see whether it lists.
    /// </summary>
    private bool CanOpen(string path) {
        return IsShellPath(path)
            ? TryGetShellNamespace()!.CanNavigate(path)
            : _fs.DirectoryExists(path);
    }

    private static VolumeKind VolumeKindOf(string path) {
        return ServiceLocator.TryGet<IVolumeInfoProvider>()?.Describe(path)?.Kind ?? VolumeKind.Unknown;
    }

    /// <summary>
    /// Asks for the session state to be written once the current burst of
    /// changes is over. Every navigation, expansion and pane resize calls
    /// this; the actual write happens in <see cref="WriteStateNow"/> when
    /// the timer runs out.
    /// </summary>
    private void SaveState() {
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
                ExpandedPaths = Trees.CollectExpanded(),
                // The pair the user set, not the scaled sizes on screen -
                // see RebasePaneSizes.
                IsPreviewVisible = _isPreviewVisible,
                PreviewWidth = _savedPreviewWidth,
                IsFoldersVisible = _isFoldersVisible,
                FoldersWidth = _savedFoldersWidth,
                BookmarksHeight = _savedBookmarksHeight,
                LayoutWindowWidth = _savedWindowWidth,
                LayoutWindowHeight = _savedWindowHeight,
                IsBookmarksExpanded = _isBookmarksExpanded,
                RecentPaths = _nav.RecentPaths.ToArray(),
            },
            Favorites = Bookmarks.Paths.ToArray(),
            Settings = Settings.ToRecord(),
            LastRunVersion = BuildInfo.Version,
        });

        if (_foldersDirty) {
            _foldersDirty = false;
            _folderStore.Save(_folders.Records);
        }

        // Only when the pane pair changed: the state is written after every
        // navigation, and the line is about the panes.
        string panes = DescribeSavedPanes();
        if (panes != _lastWrittenPanes) {
            _lastWrittenPanes = panes;
            _log.Info($"State written{(_stateStore.IsReadOnly ? " (not really: read-only)" : "")}: {panes}");
        }
    }

    /// <summary>
    /// Wipes the thumbnail cache when the version that wrote
    /// <c>state.json</c> is not this one - the three numbers and the suffix,
    /// so the commit and the build number of AH do not drop it every time
    /// the project is rebuilt. See <see cref="AppState.LastRunVersion"/> for why:
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
        string current = BuildInfo.Version;
        if (string.Equals(lastVersion, current, StringComparison.Ordinal)) {
            return;
        }

        // No quotes round the versions: the log masks quoted words as names.
        _log.Info($"Version changed ({(lastVersion.Length > 0 ? lastVersion : "none")} -> {current}), dropping the thumbnail cache");
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

        // Renames noted for the rows of the folder being left mean nothing
        // in the next one - unless this navigation is the listing following
        // that folder to where it moved.
        if (!_followingMove) {
            _landingRenames.Clear();
        }

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
            Preview.SetCurrentFolder(_nav.Current, _nav.WindowTitle);
            PreviewSecond.SetCurrentFolder(_nav.Current, _nav.WindowTitle);
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
            busy: RenamingPath is not null || _renamesInFlight > 0 || HasActiveOperations,
            rows: _search.Source);
        // Renames another program made ride with the re-listing: a selected
        // file follows its new name (decision B7).
        if (decision.Renames is { Count: > 0 } renames) {
            _landingRenames.AddRange(renames);
        }

        // Before anything is re-read: what the caches hold about these files
        // is a picture of the file as it was. A re-listing does not fix it —
        // the thumbnail caches are keyed by path, and the path is what did
        // not change when the file behind it was replaced.
        if (decision.Stale is { Count: > 0 } stale) {
            foreach (string path in stale) {
                AsyncIcon.Invalidate(path);
                ReviewThumbs.Invalidate(path);
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
                    Workspace.Post(new FolderChanged(here));
                }
                break;

            case WatchOutcome.RefreshRows:
                _ = Ratings.RefreshRowsAsync(decision.Rows!.Select(r => r.FullPath).ToArray());
                break;
        }
    }


    // --- Folder panels ---------------------------------------------------
    // The panels are the model's (Workspace) and drawn by
    // FolderTreesController. What the view model tells the model is where
    // the user is standing.


    /// <summary>
    /// The open folder moved: the model puts its place in the panel it was
    /// opened from and opens that panel down to it (PanelRules).
    /// </summary>
    private void ExpandCurrentInTrees() {
        if (_nav.Current is { } here) {
            Workspace.Post(new Navigated(
                here, _nav.CurrentSource ?? NavigationSource.External,
                _followingMove ? NavigationKind.Rewrite : NavigationKind.Go));
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
        // the editor goes with it. What was selected stays selected by path
        // once the new listing lands - the model's rule (ListingArrival), no
        // intent needed for it.
        RenamingPath = null;

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

        // Only a folder being walked into gets its creation time read and
        // its record looked up; a re-read of the folder on screen changes
        // neither the view (ViewChoice) nor the book.
        var known = arriving && _folders.Find(path) is null ? _folders.Records : null;

        var work = Task.Run(() => {
            var items = new List<FileSystemEntry>();
            int hidden = 0;
            bool sawDesktopIni = false;
            DateTime? created = arriving ? _fs.GetCreationTimeUtc(path) : null;
            // A folder with no record may be one the book knows under a
            // former name (renamed outside Wander): the candidates by
            // creation time, and a stat on each to see whose path is gone -
            // here, on the pool, never on the UI thread.
            IReadOnlyList<string>? vacated = null;
            if (known is not null && created is { } birth) {
                vacated = FolderSettingsBook.AdoptCandidates(known, path, birth)
                    .Where(c => !_fs.DirectoryExists(c.Path))
                    .Select(c => c.Path)
                    .ToList();
            }

            // Timed separately from the fold below it: "the folder was slow
            // to open" has two quite different answers — the disk was slow
            // to list it, or we were slow to arrange what it listed — and a
            // single figure cannot tell them apart.
            using (PerfLog.Measure("bg.enumerate")) {
                foreach (var e in _fs.Enumerate(path, sort, token)) {
                    token.ThrowIfCancellationRequested();
                    // Seen before the visibility filter: the file is hidden
                    // and a system one, and the setting that hides it has
                    // nothing to say about what it tells.
                    sawDesktopIni |= e.Kind == EntryKind.File
                        && string.Equals(e.Name, DesktopIni.FileName, StringComparison.OrdinalIgnoreCase);
                    if (!visibility.Allows(e)) {
                        hidden++;
                        continue;
                    }
                    items.Add(e);
                }
            }

            // What the folder's desktop.ini says it is (H1) - asked only on
            // arrival, when the view is chosen, and only when there is one.
            bool picturesHint = arriving && sawDesktopIni && ReadsAsPictures(path);

            // Folding companions happens after the visibility filters, so a
            // sidecar next to a main file the user chose not to see stays
            // visible on its own rather than disappearing with it.
            if (!integrate) {
                return (Items: (IReadOnlyList<FileSystemEntry>)items, Hidden: hidden, Created: created, Vacated: vacated, Hint: picturesHint);
            }

            using (PerfLog.Measure("bg.companions")) {
                return (Items: _companions.Collapse(items), Hidden: hidden, Created: created, Vacated: vacated, Hint: picturesHint);
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
            var (items, hidden, created, vacated, picturesHint) = await work;
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
                    ChooseView(items, path, created, vacated, inRecycleBin: false, picturesHint);
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
            var reportedSeverity = StatusSeverity;
            PublishRows(epoch, items);
            if (reported != statusBeforeLoad) {
                Say(reported, reportedSeverity);
            }
            if (arriving && _session.IsCurrent(epoch)) {
                // The clock keeps running: the view arms FirstScreenWatch
                // on it once the rows have been laid out, and the line in
                // the log counts from the navigation, not from the landing.
                FolderArrived?.Invoke(path, started);
            }
            Ratings.StartPass(items, path, sort, epoch, arriving);
            _sharpness.Listed(path, epoch);
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
            Fail(string.Format(Strings.StatusError, ex.Message));
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

        // The Recycle Bin gives what it has read so far while a slow listing
        // goes on (PLAN AD2): the first portion lands as the whole listing
        // would - arrival, view, first screen - and the next ones land on it
        // as a re-listing of the same folder, the rows on screen kept in
        // place. One that comes after the whole is dropped.
        bool landed = false;
        bool complete = false;
        var portions = archive is null
            ? new Progress<IReadOnlyList<FileSystemEntry>>(rows => {
                if (!complete && !token.IsCancellationRequested) {
                    Land(rows);
                }
            })
            : null;

        try {
            IReadOnlyList<FileSystemEntry> items;
            try {
                // Watched: an archive the shell has to open to list - a
                // solid RAR - can take seconds, and that is the spinner
                // the person is looking at.
                items = await LongWait.WatchAsync(
                    Task.Run(() => {
                        var listed = ns.Enumerate(shellPath, token, portions);

                        return archive is null ? listed : EntryComparers.Sort(listed, sort);
                    }, token),
                    _log,
                    $"list: enumerating {shellPath}");
            } catch (OperationCanceledException) {
                return;
            } catch (Exception ex) {
                _log.Error($"Shell enumerate failed: {shellPath}", ex);
                Fail(archive is null
                    ? string.Format(Strings.StatusError, ex.Message)
                    : string.Format(Strings.StatusArchiveUnreadable, archive.ArchiveName));
                return;
            }

            if (token.IsCancellationRequested) {
                return;
            }
            complete = true;
            // Always, not only when slow as on disk: shell listings are few,
            // and their cost is the number the bin's slowness (AD2) is
            // decided on.
            _log.Info($"Folder listed in {started.ElapsedMilliseconds} ms: {items.Count} shown - {shellPath}");
            Land(items);

            // An archive that lists nothing is either empty or encrypted
            // whole - 7z with -mhe hides even the names, and the two are
            // indistinguishable from here. Saying both beats an empty list
            // that looks like a mistake.
            if (archive is not null && items.Count == 0) {
                Warn(string.Format(Strings.StatusArchiveEmptyOrLocked, archive.ArchiveName));
            }
        } finally {
            // Only release the spinner if our load is still the active one.
            // A superseded load (token cancelled) leaves IsListLoading=true
            // so the next RefreshShellAsync inherits it without flicker.
            if (!token.IsCancellationRequested) {
                IsListLoading = false;
            }
        }

        // Rows on the list - a portion or the whole. The first landing is
        // the arrival; after it the veil goes, since the rows under it are
        // there to be looked at and more are only being added.
        void Land(IReadOnlyList<FileSystemEntry> rows) {
            bool first = !landed;
            if (first) {
                landed = true;
                _session.NoteListed(shellPath);
                SetMissingFolder(null);
                if (arriving) {
                    // An archive and the Recycle Bin are folders to the person
                    // opening them, so they belong in the journal the same way.
                    Journal.Note(string.Format(Strings.JournalOpenedFolder, shellPath), DateTime.Now);
                }
                // No sidecars in a shell namespace. The view is chosen below as
                // on disk - by the names in an archive; the Recycle Bin is a
                // list of things to decide about, not a folder to look at, and
                // ViewChoice keeps it out of the gallery.
                Ratings.Cancel();
                _sharpness.Cancel();
                HasRatings = false;
                if (arriving) {
                    ChooseView(rows, shellPath, createdUtc: null, vacated: null, inRecycleBin: archive is null);
                }
            }
            PublishRows(epoch, rows.ToList());

            // Timed like any other folder: an archive is one to the person
            // opening it, and "how long until I can see it" is the same
            // question there as on disk.
            if (first && arriving && _session.IsCurrent(epoch)) {
                FolderArrived?.Invoke(shellPath, started);
            }
            if (first && !complete) {
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
        // Rows listed or rated before the sharpness pass came round would
        // wash its answers out of the list; they are put back here.
        _search.SetSource(_sharpness.Decorate(items));
    }


    private void SyncEntries(IReadOnlyList<FileSystemEntry> items) {
        // The moment a folder's listing lands on the UI thread — the one
        // hitch a person notices when opening a folder.
        using var applying = PerfLog.Measure("list.apply");

        // Search results on the way out: another list, whatever rows the
        // two share. Reconciled against them, a folder opened from the
        // results kept their scroll.
        if (_entriesAreResults) {
            _entriesAreResults = false;
            Entries.ReplaceAll(items);

            return;
        }

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
    /// A listing's rows land on the list: laid down against the rows on
    /// screen (<see cref="SyncEntries"/>), then told to the window's model -
    /// what stood before, why, what the pending intent came to, the renames
    /// since the last landing. What is selected, where the keyboard goes and
    /// putting the selection back on the list are the model's
    /// (ListingArrival); the list's own reports while the rows are laid down
    /// are its doing, not the user's (<see cref="IsSyncingRows"/>).
    /// </summary>
    private void LandRows(IReadOnlyList<FileSystemEntry> rows) {
        var before = PathsOf(Entries);
        var reason = _entriesAreResults ? ListingReason.ResultsLeft
            : IsSamePath(_nav.Current, _landedFolder) ? ListingReason.Relist
            : ListingReason.Arrival;
        using (PerfLog.Measure("ui.rows")) {
            _syncingRows = true;
            try {
                SyncEntries(rows);
            } catch (Exception ex) when (ex is not OutOfMemoryException) {
                // A list control threw in the middle of the notification: the
                // plan is half applied and the other listeners never heard of
                // it. Said now, not minutes later by a finalizer, and the rows
                // laid down whole (TECHDEBT, 2026-09-22).
                _log.Error("Listing: laying the rows down failed; laid down whole", ex);
                Entries.ReplaceAll(rows);
            } finally {
                _syncingRows = false;
            }
        }
        _landedFolder = _nav.Current;
        UpdateFilterStatus(rows.Count, _search.Source.Count);
        using (PerfLog.Measure("ui.restore")) {
            PostLanding(before, reason, _session.DecideArrival(_nav.Current, Entries));
        }
    }

    /// <summary>The rows on screen landed for <paramref name="reason"/>: the model is told, with the renames since the last landing.</summary>
    private void PostLanding(IReadOnlyList<string> before, ListingReason reason, ArrivalDecision intent) {
        var renames = _landingRenames.ToArray();
        _landingRenames.Clear();
        Workspace.Post(new ListingLanded(before, PathsOf(Entries), reason, intent, renames));
    }

    private static string[] PathsOf(IEnumerable<FileSystemEntry> rows) {
        return rows.Select(r => r.FullPath).ToArray();
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
        if (int.TryParse(parameter, out int rank)) {
            Rate(RatingTargets(), RatingField.Rank, rank);
        }
    }

    /// <summary>
    /// Sets a colour label on the current selection - the gallery's
    /// Shift + digits. Unlike the stars, pressing the colour every file
    /// already carries takes it away again (<see cref="RatingToggle"/>);
    /// zero clears outright.
    /// </summary>
    public void SetColorForSelection(int color) {
        Rate(RatingTargets(), RatingField.ColorLabel, color);
    }

    /// <summary>
    /// A digit on a picture shown on its own - full screen (PLAN Q5): the
    /// gallery's keys, for that one file rather than for the selection.
    /// </summary>
    public void RatePicture(FileSystemEntry picture, RatingField field, int digit) {
        if (IsCurrentShellNamespace) {
            return;
        }

        // The row as the list has it now: the one a viewer holds can be a
        // star behind.
        Rate(new[] { Ratings.FindInSource(picture.FullPath) ?? picture }, field, digit);
    }

    /// <summary>
    /// A digit's worth of rating on <paramref name="targets"/>: stars are
    /// set and 0 clears; a colour every target already carries comes off
    /// again (<see cref="RatingToggle"/>). A digit past the scale does nothing.
    /// </summary>
    private void Rate(IReadOnlyList<FileSystemEntry> targets, RatingField field, int digit) {
        int max = field == RatingField.Rank ? Pp3Sidecar.MaxRank : ColorLabels.Max;
        if (digit < 0 || digit > max) {
            return;
        }

        int value = field == RatingField.ColorLabel && digit > 0
            ? RatingToggle.Resolve(digit, targets.Select(e => e.Rating?.ColorLabel))
            : digit;
        Ratings.Apply(targets, field, value);
    }

    /// <summary>
    /// What a rating gesture on the list is about: the selection, or the
    /// current item alone. Nothing in the Recycle Bin or inside an archive -
    /// there is nowhere to write a sidecar there.
    /// </summary>
    private IReadOnlyList<FileSystemEntry> RatingTargets() {
        if (IsCurrentShellNamespace) {
            return Array.Empty<FileSystemEntry>();
        }

        return _selectedEntries.Count > 0
            ? _selectedEntries
            : _selectedEntry is { } single ? new[] { single } : Array.Empty<FileSystemEntry>();
    }

    /// <summary>
    /// A star or a swatch clicked in a preview footer. The click is about
    /// every selected file when the pane shows one of them
    /// (<paramref name="wholeSelection"/>), and about the pane's own file
    /// otherwise; whether it sets or clears is decided against all of
    /// them at once. The answer is what the clicked file's sidecar says
    /// afterwards - the footer redraws from it.
    /// </summary>
    private SidecarRating? ApplyRatingFromPane(RatingRequestedEventArgs request, bool wholeSelection) {
        var entry = request.Entry;
        bool inSelection = _selectedEntries.Any(e => PathsEqual(e.FullPath, entry.FullPath));
        IReadOnlyList<FileSystemEntry> targets = wholeSelection && inSelection && _selectedEntries.Count > 1
            ? _selectedEntries
            : new[] { entry };

        // The pane's own reading of its file is fresher than the row's:
        // the row learns of a rating from a pass, the pane read the sidecar.
        int value = RatingToggle.Resolve(
            request.Clicked,
            targets.Select(e => PathsEqual(e.FullPath, entry.FullPath)
                ? request.Current
                : request.Field == RatingField.Rank ? e.Rating?.Rank : e.Rating?.ColorLabel));

        var results = Ratings.Apply(targets, request.Field, value);
        foreach (var result in results) {
            if (PathsEqual(result.MainPath, entry.FullPath)) {
                return result.Rating;
            }
        }

        return entry.Rating;
    }

    private static bool PathsEqual(string a, string b) {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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
    /// touching anything else - a rating arriving, a size changing. A record
    /// cannot be edited in place, so the row is replaced, and the list drops
    /// a replaced object out of its selection on the way: the model puts the
    /// selection back (ListingArrival), nothing scrolling, the keyboard where
    /// it was.
    /// </summary>
    private void ReplaceRows(IReadOnlyList<FileSystemEntry> changed) {
        var paths = PathsOf(Entries);
        _syncingRows = true;
        try {
            foreach (var entry in changed) {
                for (int i = 0; i < Entries.Count; i++) {
                    if (IsSamePath(Entries[i].FullPath, entry.FullPath)) {
                        Entries[i] = entry;
                        break;
                    }
                }
            }
        } finally {
            _syncingRows = false;
        }

        Workspace.Post(new ListingLanded(
            paths, paths, ListingReason.RowsReplaced, ArrivalDecision.None, Array.Empty<(string, string)>()));
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

    /// <summary>
    /// A swatch in the filter bar. Same two gestures as the stars. The
    /// parameter arrives as the swatch's index - an <c>int</c> bound from
    /// <c>ColorLabelViewModel</c>, not the string literal the stars carry;
    /// reading it as a string is how the colour half of the bar did nothing
    /// for a while.
    /// </summary>
    private void SetFilterColor(object? parameter) {
        int color = parameter switch {
            int i => i,
            string s when int.TryParse(s, out int parsed) => parsed,
            _ => -1,
        };
        if (color >= 0 && ReadFilterGesture() is { } toggle) {
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
        StatusSeverity = StatusSeverity.Info;
        SetField(ref _status, text, nameof(Status));
    }

    /// <summary>A status line for something that went through only in part, or not as asked.</summary>
    private void Warn(string text) {
        Say(text, StatusSeverity.Warning);
    }

    /// <summary>A status line for something that did not happen: failed or refused.</summary>
    private void Fail(string text) {
        Say(text, StatusSeverity.Error);
    }

    private void Say(string text, StatusSeverity severity) {
        // Noted before the property changes, so the journal holds every
        // line the user could have seen - including the ones a second
        // message replaced before the eye got to them. That is the whole
        // reason it exists.
        Journal.Note(text, DateTime.Now, severity);
        StatusSeverity = severity;
        SetField(ref _status, text, nameof(Status));
        StatusSaid?.Invoke(this, new StatusLine(text, severity));
    }

    /// <summary>
    /// How much an operation's outcome line matters: news when nothing
    /// failed; a warning when some of it went through, or when all that
    /// stopped the rest is a holder or the bin - held, claimed, too long
    /// for the bin - things that pass or are asked about; an error when
    /// nothing went and the reason is a failure.
    /// </summary>
    private static StatusSeverity OutcomeSeverity(int done, IReadOnlyList<Exception?> failures, string busyNote) {
        if (failures.Count == 0) {
            return busyNote.Length > 0 ? StatusSeverity.Warning : StatusSeverity.Info;
        }

        return done > 0 || failures.All(e => FileInUse.Is(e) || e is ClaimedByOperationException or RecycleUnavailableException)
            ? StatusSeverity.Warning
            : StatusSeverity.Error;
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
    /// The text a search inside files is looking for while the list shows
    /// its results, null otherwise - what a pane opened on files of it
    /// finds at once: the comparison of two of them (PLAN B6, 2026-09-25).
    /// </summary>
    public string? FoundText => IsSearchResults && ContentSearch.TextQuery.Length > 0 ? ContentSearch.TextQuery : null;

    /// <summary>
    /// The row F3 goes on to past the last match in the preview (PLAN B6):
    /// the next one a search inside files found after the file on show, in
    /// the list's order; null when the list is not such a search's results,
    /// or the file on show is the last of them.
    /// </summary>
    public FileSystemEntry? NextFoundRow() {
        return FoundText is null
            ? null
            : FindWalk.Next(Entries, PreviewSubject.Of(_target, _caretPath, Entries).Primary?.FullPath);
    }


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
        Workspace.Post(new PanelsRefreshRequested());

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
        _sharpness.Cancel();
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
    /// The user picking a view, as opposed to Wander picking one: the
    /// choice is pinned to the open folder - and only to it, the default for
    /// every other folder is a setting - so the gallery does not switch
    /// itself back on the next time the user walks in.
    /// </summary>
    private void SetViewMode(string? name) {
        if (!Enum.TryParse<ViewMode>(name, out var mode)) {
            return;
        }

        ViewMode = mode;
        if (_nav.Current is { Length: > 0 } here) {
            _foldersDirty |= _folders.SetView(here, mode, _currentCreatedUtc, DateOnly.FromDateTime(DateTime.Now));
            ViewReason = ViewReason.Pinned;
            _log.Info($"View pinned: {mode} - {here}");
        }
        SaveState();
    }

    /// <summary>
    /// "Automatically": the pin comes off the open folder and the view is
    /// chosen for it again, by the rows on screen.
    /// </summary>
    private void SetViewAuto() {
        if (_nav.Current is not { Length: > 0 } here) {
            return;
        }

        if (_folders.SetView(here, null, null, DateOnly.FromDateTime(DateTime.Now))) {
            _foldersDirty = true;
            _log.Info($"View unpinned - {here}");
        }
        ApplyViewDecision(ViewChoice.Decide(
            pinned: null, Settings.AutoGallery, IsCurrentRecycleBin,
            () => ImageFolderProbe.IsImageFolder(_search.Source, _companions, Settings.AutoGalleryPercent),
            Settings.DefaultViewMode));
        SaveState();
    }

    /// <summary>
    /// The view on screen becomes the default for folders without a pin
    /// (<see cref="AppSettings.DefaultViewMode"/>). The open folder's own
    /// pin comes off with it: pinned, it would not follow the next default
    /// (decision B13).
    /// </summary>
    private void MakeDefaultView() {
        Settings.DefaultViewMode = ViewMode;
        if (_nav.Current is { Length: > 0 } here
            && _folders.SetView(here, null, null, DateOnly.FromDateTime(DateTime.Now))) {
            _foldersDirty = true;
        }
        ViewReason = ViewReason.Default;
        _log.Info($"Default view: {ViewMode}");
        SaveState();
    }


    private void SetGalleryBackground(string? name) {
        if (Enum.TryParse<GalleryBackground>(name, out var background)) {
            Settings.GalleryBackground = background;
        }
    }


    /// <summary>
    /// The view for a folder just arrived in - <see cref="ViewChoice"/> over
    /// the folder's pin, the settings and the rows. Before the rule runs,
    /// the book learns of the arrival: the record is touched (visit day,
    /// creation time), and a folder with no record may adopt the record of
    /// its former name - <paramref name="vacated"/> are the candidates the
    /// pool found gone from disk (<see cref="FolderSettingsBook.Adopt"/>).
    /// </summary>
    /// <param name="picturesHint">The folder's desktop.ini says it is one of pictures (H1).</param>
    private void ChooseView(
        IReadOnlyList<FileSystemEntry> items, string path, DateTime? createdUtc,
        IReadOnlyList<string>? vacated, bool inRecycleBin, bool picturesHint = false) {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _currentCreatedUtc = createdUtc;
        if (createdUtc is { } birth && vacated is { Count: > 0 }
            && _folders.Adopt(path, birth, vacated) is { } former) {
            _foldersDirty = true;
            _log.Info($"Folder record follows a rename made outside: {former} -> {path}");
        }
        _foldersDirty |= _folders.Touch(path, createdUtc, today);
        if (_foldersDirty) {
            SaveState();
        }

        ApplyViewDecision(ViewChoice.Decide(
            _folders.Find(path)?.View, Settings.AutoGallery, inRecycleBin,
            () => ImageFolderProbe.IsImageFolder(items, _companions, Settings.AutoGalleryPercent),
            Settings.DefaultViewMode, picturesHint));
    }

    /// <summary>
    /// Whether the folder's desktop.ini says it is one of pictures. On the
    /// pool, with the listing; a file that cannot be read says nothing.
    /// </summary>
    private bool ReadsAsPictures(string folder) {
        try {
            return DesktopIni.SaysPictures(_fs.ReadAllBytes(Path.Combine(folder, DesktopIni.FileName)));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return false;
        }
    }


    private void ApplyViewDecision(ViewDecision decision) {
        ViewMode = decision.Mode;
        ViewReason = decision.Reason;
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

        if (e.PropertyName == nameof(SettingsViewModel.UseSystemTemp)) {
            // Takes effect for the next scratch copy. Copies already made
            // stay where they are and go with the next startup sweep, which
            // looks in both places.
            AppPaths.UseSystemTemp = Settings.UseSystemTemp;
        }

        if (e.PropertyName is nameof(SettingsViewModel.LogActions) or nameof(SettingsViewModel.LogPaths)) {
            // From the next line on; what is written stays as it was.
            ApplyLogSettings();
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
            PushPalette();

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
            // File-list filter is one half; the panels keep the levels they
            // have read, and read them again to drop or surface hidden and
            // system folders - the hidden ones through the model's own
            // setting, which also takes a cursor off a hidden row (P-19).
            if (e.PropertyName == nameof(SettingsViewModel.ShowHidden)) {
                PostWorkspaceOptions();
            } else {
                Workspace.Post(new PanelsRefreshRequested());
            }
        }

        if (e.PropertyName == nameof(SettingsViewModel.TreeKeyboardNavigates)) {
            PostWorkspaceOptions();
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
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDesktop) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkMusic) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkVideos) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkRecycleBin)) {
            PostBookmarks();
        }

        if (e.PropertyName == nameof(SettingsViewModel.AutoRefresh)) {
            // Switching it on has to start watching the folder already on
            // screen, not only the next one navigated to.
            UpdateFolderWatch();
        }

        if (e.PropertyName == nameof(SettingsViewModel.PictureMemoryMb) ||
            e.PropertyName == nameof(SettingsViewModel.ThumbnailDiskCacheEnabled) ||
            e.PropertyName == nameof(SettingsViewModel.ThumbnailDiskCacheMb)) {
            // A lowered limit bites now, not at the next start.
            ApplyThumbnailCacheSettings();
        }

        SaveState();
    }


    /// <summary>
    /// Pushes the user's cache limits into the icon provider and the picture
    /// caches. Called on every relevant settings change and once at startup -
    /// the provider deliberately knows nothing about <see cref="AppSettings"/>.
    /// </summary>
    private void ApplyThumbnailCacheSettings() {
        // The pictures' budget: the megabytes set, or a sixteenth of the
        // machine - its memory, not what is free this moment (PictureMemory).
        // A quarter for the decoded thumbnails, the rest for the frames of
        // every preview together.
        long budget = PictureMemory.Budget(Settings.PictureMemoryMb, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        IconImageCache.SetLimit(PictureMemory.Thumbnails(budget));
        PictureCache.SetLimit(PictureMemory.Frames(budget));
        ServiceLocator.Get<IIconProvider>().ConfigureCache(new ThumbnailCacheOptions(
            ThumbnailCacheOptions.Default.MemoryEntries,
            Settings.ThumbnailDiskCacheEnabled,
            Settings.ThumbnailDiskCacheMb * 1024L * 1024L,
            // The system scale: a visual in no window reports it, and the
            // window's own may not exist yet at startup.
            ThumbnailCacheOptions.SideFor(System.Windows.Media.VisualTreeHelper.GetDpi(new System.Windows.Media.DrawingVisual()).DpiScaleX)));
    }

    /// <summary>
    /// What the session log may say - the two debug switches, copied where
    /// the logger reads them: Core has no settings to ask, the same as for
    /// the cache limits above.
    /// </summary>
    private void ApplyLogSettings() {
        Log.Details = Settings.LogActions;
        Log.RevealPaths = Settings.LogPaths;
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
    /// Delete pressed on a built-in bookmark (Downloads, Documents, the
    /// Recycle Bin...). Those folders are never deleted from the panel, with
    /// Shift or without: the row is switched off in the settings, which is
    /// where it comes back from. False when the node is not such a row.
    /// </summary>
    public bool HideSpecialBookmark(TreeNodeViewModel bookmark) {
        // Read before the switch: the panel is rebuilt inside the call.
        string name = bookmark.Name;
        string path = bookmark.FullPath;
        if (!bookmark.IsBuiltInBookmark || !Bookmarks.HideSpecial(path)) {
            return false;
        }

        _log.Info($"Delete on a built-in bookmark: switched off in the settings - {path}");
        Status = string.Format(Strings.BookmarkSwitchedOff, name);

        return true;
    }

    /// <summary>
    /// Delete pressed on a bookmark. The key could mean the row or the
    /// folder behind it, and the two are far apart - one is a line in a
    /// panel, the other is the user's files - so it asks, with both answers
    /// named on the buttons. A bookmark whose folder is gone has only the
    /// first answer.
    /// </summary>
    public void DeleteFromBookmark(TreeNodeViewModel bookmark, bool permanent) {
        var choices = new List<string> { Strings.BookmarkDeleteRemove };
        if (!bookmark.IsMissing) {
            choices.Add(permanent ? Strings.BookmarkDeleteFolderForever : Strings.BookmarkDeleteFolder);
        }

        int choice = _dialogs.Choose(new ChoiceRequest(
            DialogKind.BookmarkOrFolder,
            Strings.BookmarkDeleteTitle,
            string.Format(Strings.BookmarkDeleteMessage, bookmark.Name, bookmark.FullPath),
            choices));
        switch (choice) {
            case 0:
                _log.Info($"Delete on a bookmark: bookmark removed - {bookmark.FullPath}");
                RemoveBookmarkCommand.Execute(bookmark);
                break;

            case 1:
                _ = DeleteFolderAsync(bookmark.FullPath, permanent);
                break;

            default:
                _log.Info($"Delete on a bookmark cancelled - {bookmark.FullPath}");
                break;
        }
    }


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
    /// Moves a bookmark up or down. The panel's cursor stays on it - the
    /// model keeps the cursor by path (P-16), so a second Ctrl+Up has the
    /// same row under it. False when nothing moved.
    /// </summary>
    public bool MoveBookmark(string path, int delta) {
        return Bookmarks.Move(path, delta);
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
        // to consume the intent - the rows on screen land again for it.
        if (IsSamePath(folder, _nav.Current)) {
            PostLanding(PathsOf(Entries), ListingReason.Relist, _session.DecideArrival(_nav.Current, Entries));

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


    /// <param name="page">The page to open on; the one left last time when null.</param>
    private void OpenSettingsDialog(SettingsCategoryViewModel? page = null) {
        if (page is not null) {
            Settings.SelectedCategory = page;
        }

        // Lazy import: the View type lives in Wander.App.Views and is
        // referenced via its full namespace to keep MainViewModel free
        // of view-layer using directives at the top of the file.
        var dlg = new Wander.App.Views.SettingsWindow {
            DataContext = Settings,
            Owner = Application.Current?.MainWindow,
        };
        ShowModal(dlg);
        // A tool may have been installed or pointed at, a row added.
        _ = LocateToolsAsync();
    }


    // --- Context-menu verbs ---------------------------------------------
    // These exist because the context menu needs them; the toolbar and the
    // hotkey table are unchanged. What each acts on is TargetRules' answer
    // for the target (ResolveTarget) - the rows, a panel row's folder, or
    // the open folder when there is nothing - so the same command backs the
    // item menu, the background menu and the key. The call to the system
    // itself is ShellCommandsController's.

    private void ShowProperties(object? parameter) {
        if (TargetRules.PropertiesOf(ResolveTarget(parameter).Target, _nav.Current) is { } path) {
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

    private void OpenWith(object? parameter) {
        if (OpenWithTarget(parameter) is { } entry) {
            Shell.OpenWith(entry.FullPath);
        }
    }

    /// <summary>The one file "Open with" is about: the list's current row, in a place its programs can reach.</summary>
    private FileSystemEntry? OpenWithTarget(object? parameter) {
        var (target, place) = ResolveTarget(parameter);

        return target.Kind == TargetKind.ListRows && !place.IsReadOnly ? target.Primary : null;
    }

    private void OpenInTerminal(object? parameter) {
        if (TerminalFolder(parameter) is { } folder) {
            Shell.OpenInTerminal(folder);
        }
    }

    /// <summary>Folder a terminal should start in (TargetRules), unless the target's place is not a folder on disk.</summary>
    private string? TerminalFolder(object? parameter) {
        var (target, place) = ResolveTarget(parameter);

        return place.IsReadOnly ? null : TargetRules.TerminalFolder(target, _nav.Current);
    }


    /// <summary>Shortcuts to the list's rows, in the open folder. Not offered for a panel row (decision B8).</summary>
    private void CreateShortcutsForSelection(object? parameter) {
        if (ListRowsOf(parameter) is not { } rows || _nav.Current is null) {
            return;
        }
        CreateShortcuts(rows.Select(e => e.FullPath).ToList(), _nav.Current);
    }


    // --- Destructive / clipboard ops (always confirm, Cancel-default) --

    /// <summary>Delete and Shift+Delete: the target's items, where the place allows it.</summary>
    private bool CanDelete(object? parameter) {
        var (target, place) = ResolveTarget(parameter);

        return TargetRules.Items(target).Count > 0 && !place.IsReadOnly;
    }

    private async Task DeleteTargetAsync(object? parameter, bool permanent) {
        if (!CanDelete(parameter)) {
            return;
        }

        var items = await ItemsToActOnAsync(ResolveTarget(parameter).Target);
        await DeleteAsync(items, permanent);
    }

    /// <summary>
    /// The folder of a bookmark, asked about and confirmed already (the
    /// bookmark question) - deleted as if it were the target.
    /// </summary>
    private async Task DeleteFolderAsync(string path, bool permanent) {
        var items = await ItemsToActOnAsync(Target.OfPanelRow(Pane.Bookmarks, path));
        await DeleteAsync(items, permanent, confirmed: true);
    }

    /// <summary>
    /// The target's items as the operations need them. A panel row's folder
    /// is read from the disk here, off the UI thread and only now that it is
    /// acted on: its attributes decide the read-only question, and none of
    /// that is worth a stat on every step of the panel's cursor.
    /// </summary>
    private async Task<IReadOnlyList<FileSystemEntry>> ItemsToActOnAsync(Target target) {
        if (target.Kind != TargetKind.PanelRow) {
            return TargetRules.Items(target);
        }

        string folder = target.Folder!;
        var entry = await Task.Run(() => _fs.GetEntry(folder));
        if (entry is null) {
            _log.Info($"Panel row is gone: {folder}");
        }

        return entry is null ? Array.Empty<FileSystemEntry>() : new[] { entry };
    }

    /// <param name="snapshot">What to delete.</param>
    /// <param name="permanent">Past the bin.</param>
    /// <param name="confirmed">
    /// The user has already said "to the bin" in so many words (the bookmark
    /// question); the recycle confirmation would only ask it again. A
    /// permanent delete is confirmed regardless.
    /// </param>
    private async Task DeleteAsync(IReadOnlyList<FileSystemEntry> snapshot, bool permanent, bool confirmed = false) {
        if (snapshot.Count == 0) {
            return;
        }

        var paths = WithCompanions(snapshot);
        int extras = paths.Count - snapshot.Count;

        // Permanent (Shift+Delete) always asks. Recycle asks only when the
        // user kept the "confirm" preference on — Ctrl+Z still restores from
        // the bin so skipping the prompt is safe by default.
        bool needsConfirm = permanent || (Settings.ConfirmRecycle && !confirmed);
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

        await RunDeleteAsync(paths, snapshot, permanent);
    }

    /// <summary>
    /// The delete itself, once everything there was to ask has been asked:
    /// the operation, the listing and panels after it, the outcome. What
    /// failed because another program holds it is put to the user by name,
    /// with the offer to try again once they have closed it there - the
    /// status line alone is too easy to miss for a delete that did not
    /// happen.
    /// </summary>
    /// <param name="snapshot">The rows the delete started from, for where the keyboard lands.</param>
    private async Task RunDeleteAsync(IReadOnlyList<string> paths, IReadOnlyList<FileSystemEntry> snapshot, bool permanent) {
        IReadOnlyList<DeleteResult> results;
        ReleasePreview(paths);
        try {
            results = await RunWithProgressDialogAsync(
                permanent ? Strings.ProgressDeleting : Strings.ProgressRecycling,
                ct => _ops.DeleteManyAsync(paths, permanent, ct));
        } catch (OperationCanceledException) {
            Status = Strings.StatusCancelled;
            return;
        } catch (Exception ex) {
            _log.Error($"Delete batch failed", ex);
            Fail(string.Format(Strings.StatusDeleteFailed, ex.Message));
            return;
        } finally {
            RestorePreview();
        }

        var gone = results.Where(r => r.Status == DeleteStatus.Ok).Select(r => r.Path).ToList();
        int ok = gone.Count;
        int failed = results.Count(r => r.Status == DeleteStatus.Failed);

        // The panels show folders too, and a folder deleted from a panel is
        // usually not in the listing at all: its rows go at once - a cursor
        // on one goes to its neighbour (P-14) - and the folders above are
        // read again. Before the listing moves: going to the nearest
        // survivor is a navigation, and it puts the cursor there (P-13).
        Workspace.Post(new Removed(gone));
        RefreshTreesAbove(gone);
        if (NavigationFallback.AfterDelete(gone, _nav.Current) is { } fallback) {
            // The folder on screen went, or one above it: the listing goes
            // to the nearest survivor, the way Up would, rather than showing
            // "folder is gone" over what was removed on purpose. History is
            // left alone - Back into the deleted folder shows that panel.
            NavigateTo(fallback, _nav.CurrentSource ?? NavigationSource.External);
        } else {
            // Point the keyboard at what will be under it once the rows are
            // gone, and take it back from the progress dialog. Only when
            // something actually went: a delete that failed outright leaves
            // the rows on screen, and moving off them would hide what went
            // wrong.
            if (ok > 0 && _nav.Current is { } folder) {
                var next = NextAfterRemoval(snapshot);
                _session.SetArrivalHere(ArrivalIntent.Rows(folder, next, takeFocus: next.Length > 0));
            }
            Refresh();
        }

        string waited = BusyNote(results.Select(r => r.Busy));
        var failures = results.Where(r => r.Status == DeleteStatus.Failed).Select(r => r.Error).ToList();
        var severity = OutcomeSeverity(ok, failures, waited);
        if (failed == 0) {
            Say(string.Format(permanent ? Strings.StatusDeleted : Strings.StatusRecycled, ok) + waited, severity);

            return;
        }

        // Naming the holder asks Restart Manager, which takes a tenth of a
        // second or more (a folder: its first few hundred files) - off the
        // UI thread, or the question goes up behind a stall.
        var firstFail = results.First(r => r.Status == DeleteStatus.Failed);
        var firstError = firstFail.Error;
        string reason = firstError is null
            ? ""
            : await Task.Run(() => DescribeError(firstError, firstFail.Path));
        Say(string.Format(
            permanent ? Strings.StatusDeletedPartly : Strings.StatusRecycledPartly,
            ok, failed, reason.Length > 0 ? ": " + reason : "") + waited, severity);
        _log.Info($"Delete: {ok} done, {failed} failed - {reason}");

        // Refused by the bin and still where they were. Asked about once,
        // after the attempt: a folder with an over-long path inside is only
        // found out by the operation itself, so no question beforehand could
        // catch every case. The answer is the confirmation - the permanent
        // delete's own question is not asked again; everything else it does
        // (the guard, the Warn line, the cleared undo stack) it does.
        var unrecyclable = results.Where(r => r.Error is RecycleUnavailableException).ToList();
        if (unrecyclable.Count > 0 && AskDeleteForGood(unrecyclable, ok)) {
            var forGood = unrecyclable.Select(r => r.Path).ToList();
            var chosen = new HashSet<string>(forGood, StringComparer.OrdinalIgnoreCase);
            await RunDeleteAsync(forGood, snapshot.Where(e => chosen.Contains(e.FullPath)).ToList(), permanent: true);
        }

        var busy = results
            .Where(r => r.Status == DeleteStatus.Failed && FileInUse.Is(r.Error))
            .ToList();
        if (busy.Count == 0) {
            return;
        }

        string busyReason = ReferenceEquals(busy[0], firstFail)
            ? reason
            : await Task.Run(() => DescribeError(busy[0].Error!, busy[0].Path));
        var again = busy.Select(r => r.Path).ToList();
        if (!AskRetryInUse(again, busyReason)) {
            return;
        }

        var retry = new HashSet<string>(again, StringComparer.OrdinalIgnoreCase);
        await RunDeleteAsync(again, snapshot.Where(e => retry.Contains(e.FullPath)).ToList(), permanent);
    }

    /// <summary>
    /// "Could not delete it: it is open in ..." - with a button to try
    /// again. Cancel is the default and the answer to Esc, like every
    /// question here.
    /// </summary>
    private bool AskRetryInUse(IReadOnlyList<string> busy, string reason) {
        string name = Path.GetFileName(busy[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string message = busy.Count == 1
            ? string.Format(Strings.DeleteInUseOne, name, reason)
            : string.Format(Strings.DeleteInUseMany, busy.Count, name, reason);

        return _dialogs.Choose(new ChoiceRequest(
            DialogKind.DeleteInUse, Strings.DeleteInUseTitle, message, new[] { Strings.ActionRetry })) == 0;
    }

    /// <summary>
    /// Both halves of the preview let go of <paramref name="paths"/>, and of
    /// anything inside them, before an operation takes them away - see
    /// <see cref="PreviewController.Release"/>. Every call is paired with
    /// <see cref="RestorePreview"/> once the operation is over.
    /// </summary>
    private void ReleasePreview(IEnumerable<string> paths) {
        var list = paths.ToList();
        Preview.Release(list);
        PreviewSecond.Release(list);
    }

    /// <summary>What <see cref="ReleasePreview"/> let go of and the operation left in place is shown again.</summary>
    private void RestorePreview() {
        Preview.Restore();
        PreviewSecond.Restore();
    }

    /// <summary>
    /// "The bin will not take these: delete them for good?" Every refused
    /// item is named - up to ten, then a count - with why, what the same
    /// delete already put in the bin, and what a delete for good costs.
    /// Both answers are named; "stop here" is the default, Enter and Esc
    /// alike, and "delete for good" arms only after
    /// <see cref="_deleteForGoodArmDelay"/>.
    /// </summary>
    /// <param name="refused">What the bin refused, each with its <see cref="RecycleUnavailableException"/>.</param>
    /// <param name="recycled">What the same delete did put in the bin.</param>
    private bool AskDeleteForGood(IReadOnlyList<DeleteResult> refused, int recycled) {
        const int named = 10;
        var lines = new List<string> { Strings.RecycleUnavailableIntro };
        foreach (var item in refused.Take(named)) {
            string path = item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            lines.Add(string.Format(Strings.RecycleUnavailableItem, Path.GetFileName(path), Path.GetDirectoryName(path)));
        }
        if (refused.Count > named) {
            lines.Add(string.Format(Strings.AndMore, refused.Count - named));
        }

        lines.Add("");
        lines.AddRange(refused
            .Select(r => ((RecycleUnavailableException)r.Error!).Reason)
            .Distinct()
            .Select(reason => reason == RecycleUnavailableReason.PathTooLong
                ? Strings.RecycleUnavailableTooLong
                : Strings.RecycleUnavailableNoBin));
        if (recycled > 0) {
            lines.Add(string.Format(Strings.RecycleUnavailableRestRecycled, recycled));
        }

        lines.Add("");
        lines.Add(Strings.RecycleUnavailableIrreversible);
        if (_undo.Depth > 0) {
            lines.Add(Strings.RecycleUnavailableClearsUndo);
        }

        bool accepted = _dialogs.Choose(new ChoiceRequest(
            DialogKind.RecycleUnavailable, Strings.RecycleUnavailableTitle, string.Join("\n", lines),
            new[] { Strings.ActionDeleteForGood }, Strings.ActionAbort, _deleteForGoodArmDelay)) == 0;
        _log.Info($"Recycle refused for {refused.Count} item(s): {(accepted ? "user chose to delete them for good" : "left in place")}");

        return accepted;
    }

    /// <summary>
    /// The row the selection should land on once <paramref name="removed"/>
    /// is gone: the first survivor after the last of them, or — when they
    /// were at the end of the folder — the last survivor before the first.
    /// Empty when the folder is being emptied outright, and there is nothing
    /// to land on.
    /// </summary>
    private string[] NextAfterRemoval(IReadOnlyList<FileSystemEntry> removed) {
        // The same rule that answers a file deleted by another program
        // (ListingArrival), asked ahead of the listing: the rows minus
        // what is about to go stand in for the rows to come.
        var gone = new HashSet<string>(removed.Select(e => e.FullPath), StringComparer.OrdinalIgnoreCase);
        var staying = Entries.Where(e => !gone.Contains(e.FullPath)).ToList();

        return CurrentRowFallback.After(Entries, gone, staying) is { } next
            ? new[] { next.FullPath }
            : Array.Empty<string>();
    }


    [SuppressMessage("ReSharper", "AsyncVoidMethod",
        Justification = "A command body, nothing awaits it; every exception is caught, logged and shown in the status bar.")]
    private async void UndoLast() {
        try {
            // An operation like any other - off this thread, in the tracker,
            // cancellable, its window held back the same way.
            var outcome = await RunWithProgressDialogAsync(
                Strings.ProgressUndoing, ct => _undo.UndoAsync(_tracker, ct, _claims));
            if (outcome is null) {
                return;
            }

            foreach (var failure in outcome.Failures) {
                _log.Error($"Undo failed: {failure.Step.Description}", failure.Error);
            }
            // The journal is the user's: the lines name files, and the reason
            // is said in their words (PLAN block 0, step 7).
            var (failedNames, failedReason) = outcome.Failures.Count > 0
                ? await DescribeUndoFailuresAsync(outcome.Failures)
                : ("", "");
            string left = outcome.Remaining is { } remaining ? NamesOf(remaining) : "";
            var action = outcome.Undone;
            if (action is null) {
                if (outcome.Failures.Count > 0) {
                    Fail(string.Format(Strings.StatusUndoFailed, failedNames, failedReason));
                } else {
                    Warn(string.Format(Strings.StatusUndoStopped, left));
                }

                return;
            }

            _log.Info(
                $"Undo: {action.Description}" +
                (outcome.Cancelled ? $" - stopped, {outcome.Remaining?.Steps.Count ?? 0} steps left on the stack" : "") +
                (outcome.Failures.Count > 0 ? $" - {outcome.Failures.Count} steps failed" : ""));
            if (outcome.Cancelled) {
                Warn(string.Format(Strings.StatusUndoStopped, left));
            } else if (outcome.Failures.Count > 0) {
                Warn(string.Format(Strings.StatusUndonePartly, NamesOf(action), failedNames, failedReason));
            } else {
                Status = string.Format(Strings.StatusUndone, action.Description);
            }

            // An undo that only put a rating back leaves the folder exactly
            // as it was — same files, same names, same order — so re-listing
            // it would be the same jump the write itself avoids. Only the
            // rows it touched are re-read.
            // An undo that took the folder on screen back to where it was
            // moved from has taken the listing with it - re-listing the path
            // it left would show "folder is gone".
            if (action.MetadataTargets.Count > 0) {
                _ = Ratings.RefreshRowsAsync(action.MetadataTargets);
            } else if (!FollowRelocated(action.MovesOnUndo, target: null)) {
                // Point the user at what came back, not at wherever the
                // selection happened to be.
                _session.SetArrivalHere(ArrivalIntent.Rows(_nav.Current!, action.PathsAfterUndo.ToArray()));
                Refresh();
                // Nothing moved, yet something came back - a folder out of
                // the bin reappears in its parent's branch too.
                if (action.MovesOnUndo.Count == 0) {
                    RefreshTreesAbove(action.PathsAfterUndo);
                }
            }

            // A rating undo rewrites a sidecar the footer is already
            // showing; neither path above would touch it.
            Preview.ReloadCompanions();
            PreviewSecond.ReloadCompanions();
        } catch (Exception ex) {
            _log.Error("Undo failed", ex);
            Fail(string.Format(Strings.StatusUndoError, ex.Message));
        }
    }

    /// <summary>
    /// The steps of an undo that did not come back, for the journal: the
    /// files by name - the first, then how many more - and why the first did
    /// not, in the user's words. Naming a holder asks Restart Manager, so
    /// off this thread.
    /// </summary>
    private async Task<(string Names, string Reason)> DescribeUndoFailuresAsync(IReadOnlyList<UndoFailure> failures) {
        var first = failures[0];
        string? path = StepPath(first);
        string reason = await Task.Run(() => DescribeError(first.Error, path ?? ""));
        string names = path is null ? first.Step.Description : Named(path, failures.Count);

        return (names, reason);
    }

    /// <summary>
    /// The file an undo step is about: where it would have come back to, or
    /// - for the undo of a create, which takes the item away - the path its
    /// failure names. Null when neither says.
    /// </summary>
    private static string? StepPath(UndoFailure failure) {
        if (failure.Step.PathsAfterUndo is [var back, ..]) {
            return back;
        }

        return failure.Error switch {
            RecycleUnavailableException refused => refused.ItemPath,
            ClaimedByOperationException claimed => claimed.ItemPath,
            FileNotFoundException missing => missing.FileName,
            _ => null,
        };
    }

    /// <summary>What an undo brings back, by name; its description when it brings nothing back to name (the undo of a create).</summary>
    private static string NamesOf(IUndoableAction action) {
        return action.PathsAfterUndo is [var first, ..] ? Named(first, action.PathsAfterUndo.Count) : action.Description;
    }

    /// <summary>The first file by name, the rest as a count - how a journal line names files (PLAN block 0, step 7).</summary>
    private static string Named(string first, int count) {
        string name = Path.GetFileName(first.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return count <= 1
            ? string.Format(Strings.StatusNamedOne, name)
            : string.Format(Strings.StatusNamedMany, name, count - 1);
    }

    [SuppressMessage("ReSharper", "AsyncVoidMethod",
        Justification = "A command body, nothing awaits it; every exception is caught, logged and shown in the status bar.")]
    /// <param name="takeFocus">Confirmed with Enter: the keyboard goes onto the renamed row once it lands (K-4).</param>
    private async void Rename(FileSystemEntry? entry, string? newName, bool takeFocus) {
        if (entry is null || string.IsNullOrWhiteSpace(newName)) {
            return;
        }
        if (newName == entry.Name) {
            return;
        }

        // The watcher's tick waits for this one (L-11): the editor is gone
        // already, and a re-listing while the rename is on its way saw the
        // old name gone and the new one not yet known - and selected the
        // neighbour for a moment.
        _renamesInFlight++;
        try {
            // Companions ride along under the matching new name, as one
            // undo step — renaming Sprite.png and leaving Sprite.png.meta
            // behind is precisely the breakage this feature exists to stop.
            IReadOnlyList<(string Path, string NewName)> plan = Settings.IntegrateCompanions
                ? _companions.RenamePlan(entry.FullPath, newName, entry.Companions)
                : new[] { (entry.FullPath, newName) };
            ReleasePreview(plan.Select(p => p.Path));
            // Off this thread: a file somebody holds is waited for, up to
            // two seconds (BusyGate), and the window must not stand still
            // for them.
            await Task.Run(() => _ops.RenameMany(plan));
            // The rename rides with the next landing: a selection on the row
            // follows the new name (decision B7), whatever else happened to
            // the selection meanwhile. A folder's pair is added by
            // FollowRelocated below, with everything else that follows it.
            string folder = Path.GetDirectoryName(entry.FullPath) ?? "";
            string renamed = Path.Combine(folder, newName);
            if (entry.Kind != EntryKind.Directory) {
                _landingRenames.Add((entry.FullPath, renamed));
            }
            if (takeFocus) {
                _session.SetArrivalHere(ArrivalIntent.Rows(folder, new[] { renamed }, takeFocus: true));
            }
            Refresh();
            // A folder has things pointing at it that a file has not: a
            // bookmark on it or inside it, "Back" into it, its rows in
            // the panels. They follow the new name the way they follow a
            // move. Not awaited: the listing above must not wait for the
            // panels to re-read.
            if (entry.Kind == EntryKind.Directory) {
                FollowRelocated(new[] { (entry.FullPath, renamed) }, target: null);
            }
            if (plan.Count > 1) {
                Status = string.Format(Strings.StatusRenamedWithCompanions, plan.Count - 1);
            }
        } catch (Exception ex) {
            _log.Error($"Rename failed: {entry.FullPath} -> {Log.Path(newName)}", ex);
            string reason = await Task.Run(() => DescribeError(ex, entry.FullPath));
            Fail(string.Format(Strings.StatusRenameFailed, reason));
        } finally {
            _renamesInFlight--;
            RestorePreview();
        }
    }


    // --- Custom actions ----------------------------------------------------

    /// <summary>Where the programs are, as last looked up; what a preset is started with.</summary>
    private IReadOnlyDictionary<string, ToolLocation> _toolLocations = new Dictionary<string, ToolLocation>();


    /// <summary>
    /// Tools the catalog needs and the machine lacks - their actions are
    /// greyed in the header menu. Looked up at startup and after the
    /// settings dialog, never while a menu is opening.
    /// </summary>
    public IReadOnlySet<string> MissingTools { get; private set; } = new HashSet<string>();


    /// <summary>
    /// Runs the catalog action named by <paramref name="parameter"/> (its id,
    /// or a <see cref="MenuCall"/> carrying it) over the target's items, in
    /// the order the list shows them, in an operation window of its own.
    /// What applies is Core's call, asked again here: the menu that offered
    /// the row was built a moment ago. The outputs arrive through the folder
    /// watcher, so nothing is re-listed.
    /// </summary>
    /// <param name="pickFolder">Ask where the outputs go before running; beside their sources otherwise.</param>
    private async Task RunActionAsync(object? parameter, bool pickFolder = false) {
        var (target, place) = ResolveTarget(parameter);
        string? id = parameter is MenuCall call ? call.Argument as string : parameter as string;
        if (place.IsReadOnly || id is null) {
            return;
        }

        var action = Settings.Actions.FirstOrDefault(a => a.Id == id);
        if (action is null) {
            _log.Warn($"Action '{id}' is not in the catalog");

            return;
        }

        string? folder = _nav.Current;
        var items = TargetRules.Items(target);
        var state = ActionApplicability.For(action, items, folder is not null, tool => !MissingTools.Contains(tool));
        if (state != ActionState.Applicable) {
            _log.Info($"Action '{action.DisplayTitle}' not run: {state}");

            return;
        }

        // Nothing targeted and still applicable: an action for folders, on
        // the folder on screen.
        var paths = items.Count > 0 ? InListOrder(items) : new[] { folder! };

        string? outputFolder = null;
        if (pickFolder) {
            outputFolder = _dialogs.PickFolder(Strings.ActionsPickFolderTitle, folder);
            if (outputFolder is null) {
                return;
            }
        }

        await RunActionOnAsync(action, paths, outputFolder);
    }

    /// <summary>
    /// The right-button drop menu chose an action: it runs over what was
    /// dropped, not over the selection, and the outputs land in the folder
    /// dropped on. Not asked again whether it applies - the menu offered
    /// only what does, a moment ago, over these same items.
    /// </summary>
    public void RunActionOnDropped(string? id, IReadOnlyList<string> paths, string outputFolder) {
        var action = Settings.Actions.FirstOrDefault(a => a.Id == id);
        if (action is null) {
            _log.Warn($"Action '{id}' is not in the catalog");

            return;
        }
        if (paths.Count == 0) {
            return;
        }

        _ = RunActionOnAsync(action, paths, outputFolder);
    }

    /// <summary>One action over the given paths, in an operation window of its own, with the report at the end.</summary>
    private async Task RunActionOnAsync(CustomAction action, IReadOnlyList<string> paths, string? outputFolder) {
        var run = ActionCatalog.WithLocatedProgram(action, _toolLocations);
        string title = action.DisplayTitle;

        IReadOnlyList<ActionItemResult> results;
        try {
            results = await RunWithProgressDialogAsync(
                Strings.ProgressRunningAction,
                ct => Task.Run(() => _actions.RunAsync(run, paths, ct, outputFolder)));
        } catch (OperationCanceledException) {
            Status = Strings.StatusCancelled;

            return;
        } catch (Exception ex) {
            _log.Error($"Action '{title}' failed", ex);
            Fail(string.Format(Strings.StatusActionFailed, title, ex.Message));

            return;
        }

        int ok = results.Count(r => r.Status == BatchItemStatus.Ok);
        Say(string.Format(Strings.StatusActionDone, title, ok, results.Count),
            ok < results.Count ? StatusSeverity.Warning : StatusSeverity.Info);
        if (ActionReport.IsNeeded(results)) {
            string message = string.Format(Strings.ActionReportHeader, ok, results.Count)
                + "\n\n" + string.Join("\n", ActionReport.Lines(results, oneCommand: !run.RunPerFile));
            _dialogs.Ask(new DialogRequest(
                DialogKind.ActionReport, title, message, DialogButtons.Ok, DialogIcon.Warning));
        }
    }

    /// <summary>The selection in the order the list shows it, not the order it was clicked in.</summary>
    private string[] InListOrder(IReadOnlyList<FileSystemEntry> selection) {
        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Entries.Count; i++) {
            position.TryAdd(Entries[i].FullPath, i);
        }

        return selection
            .Select(e => e.FullPath)
            .OrderBy(p => position.GetValueOrDefault(p, int.MaxValue))
            .ToArray();
    }

    /// <summary>The walk over <c>PATH</c> is file probes, some of them maybe on a network share - on the pool.</summary>
    private async Task LocateToolsAsync() {
        var catalog = Settings.Actions;
        var given = Settings.ToolPaths;
        try {
            var tools = await Task.Run(() => ActionCatalog.LocateTools(catalog, given, _toolLocator.Find, _fs.FileExists));
            _toolLocations = tools;
            MissingTools = ActionCatalog.Missing(tools);
        } catch (Exception ex) {
            _log.Error("Could not look for the actions' tools", ex);
        }
    }


    // --- Batch rename ------------------------------------------------------

    /// <summary>
    /// Opens the batch-rename window on the selection and applies what it
    /// answers, as one undo step. A mixed selection is refused out loud:
    /// F2 comes here with any two rows without asking CanExecute, and a key
    /// that silently does nothing reads as broken.
    /// </summary>
    [SuppressMessage("ReSharper", "AsyncVoidMethod",
        Justification = "A command body, nothing awaits it; every exception is caught, logged and shown in the status bar.")]
    private async void BatchRename(object? parameter) {
        if (ResolveTarget(parameter).Place.IsReadOnly || _nav.Current is not { } folder
            || ListRowsOf(parameter) is not { } selection) {
            return;
        }

        var kind = BatchRenameGate.Classify(selection);
        if (kind == BatchRenameKind.Mixed) {
            Fail(Strings.StatusBatchRenameMixed);

            return;
        }
        if (kind == BatchRenameKind.TooFew) {
            return;
        }

        // The counter runs down the list as it is on screen, not in the
        // order the rows were clicked.
        var chosen = new HashSet<string>(selection.Select(e => e.FullPath), StringComparer.OrdinalIgnoreCase);
        var items = Entries.Where(e => chosen.Contains(e.FullPath)).Select(RenameItem.From).ToArray();
        var context = new RenameContext(
            path => _fs.FileExists(path) || _fs.DirectoryExists(path),
            Settings.IntegrateCompanions ? _companions : null);
        var saved = _stateStore.Load();
        var vm = new BatchRenameViewModel(
            items, context, ServiceLocator.TryGet<IImageMetadataReader>(),
            saved.RenameRules ?? RenameRules.Default, saved.RenameTemplates);

        var dlg = new Wander.App.Views.BatchRenameWindow(vm) {
            Owner = Application.Current?.MainWindow,
        };
        bool accepted = ShowModal(dlg) == true;
        if (!accepted) {
            return;
        }

        // Where the user left off: the same pass usually goes over the next
        // folder too.
        var rules = vm.Rules;
        var state = _stateStore.Load();
        _stateStore.Save(state with {
            RenameRules = rules,
            RenameTemplates = RenameTemplateHistory.Add(state.RenameTemplates, rules.Template),
        });

        var preview = vm.Preview;
        var renamed = preview.Rows.Where(r => r.Status == RenameRowStatus.Renamed).ToArray();
        var plan = preview.Plan();
        ReleasePreview(plan.Select(p => p.Path));
        try {
            // Off this thread, like the single rename: held files are waited for.
            await Task.Run(() => _ops.RenameMany(plan, $"Rename {renamed.Length} items"));
        } catch (Exception ex) {
            _log.Error($"Batch rename failed: {renamed.Length} items in {folder}", ex);
            string reason = await Task.Run(() => DescribeError(ex, renamed[0].Path));
            Fail(string.Format(Strings.StatusRenameFailed, reason));

            return;
        } finally {
            RestorePreview();
        }

        _log.Info($"Batch rename: {renamed.Length} items in {folder}");
        var arrived = renamed
            .Select(r => Path.Combine(Path.GetDirectoryName(r.Path) ?? "", r.NewName))
            .ToArray();
        // The renamed rows are new rows: the keyboard went with the old ones.
        _session.SetArrivalHere(ArrivalIntent.Rows(folder, arrived, takeFocus: true));
        Refresh();
        Status = string.Format(Strings.StatusBatchRenamed, renamed.Length);
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
    /// <paramref name="takeFocus"/>: committed with Enter - the user is still
    /// on the row, and the keyboard goes onto it under its new name (K-4);
    /// committed by a click elsewhere, the keyboard is where the click put it.
    /// </summary>
    public void CommitRename(string? newName, bool takeFocus) {
        string? path = RenamingPath;
        RenamingPath = null;
        if (path is null) {
            return;
        }

        var entry = Entries.FirstOrDefault(
            e => string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase));
        Rename(entry, newName, takeFocus);
    }

    public void CancelRename() {
        RenamingPath = null;
    }

    /// <summary>
    /// <c>F2</c> on a row of a folder panel. The operation is the list's
    /// (<see cref="Rename"/>: guard, log, undo, the wait for a holder), on
    /// a folder that need not be a row of the listing - the open folder or
    /// one above it, a bookmark, a branch three levels down. What
    /// remembers the path follows the new name the way it follows a move
    /// (<see cref="FollowRelocated"/>); the listing is re-read only
    /// when the folder is one of its rows, or is it or above it.
    /// </summary>
    /// <returns>The folder's new path, or null when nothing was renamed.</returns>
    public async Task<string?> RenameFolderAsync(string path, string newName) {
        string folder = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(folder);
        if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrEmpty(parent)
            || string.Equals(newName, Path.GetFileName(folder), StringComparison.Ordinal)) {
            return null;
        }

        string renamed = Path.Combine(parent, newName);
        try {
            ReleasePreview(new[] { folder });
            await Task.Run(() => _ops.RenameMany(new[] { (folder, newName) }));
        } catch (Exception ex) {
            _log.Error($"Rename failed: {folder} -> {Log.Path(newName)}", ex);
            string reason = await Task.Run(() => DescribeError(ex, folder));
            Fail(string.Format(Strings.StatusRenameFailed, reason));

            return null;
        } finally {
            RestorePreview();
        }

        if (!FollowRelocated(new[] { (folder, renamed) }, target: null)
            && IsSamePath(parent, _nav.Current)) {
            // A row of the open folder: the listing shows it under its new
            // name. The list keeps its selection while the keyboard is in a
            // panel (decision B2); where it held the folder, it follows the
            // new name - the rename rides with the landing - rather than
            // taking it for a row that left.
            Refresh();
        }

        return renamed;
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
                // ModifiedUtc carries the deletion time for bin entries, and
                // FullPath the $R file the bin keeps the item in - that is
                // what the enumerator puts there, and what Restore finds it by.
                bin.Restore(new RecycleHandle(
                    Path.Combine(entry.OriginalLocation, entry.Name), entry.ModifiedUtc,
                    BinFilePath: entry.FullPath));
                restored++;
            } catch (Exception ex) {
                _log.Error($"Restore failed: {Log.Path(entry.Name)}", ex);
                failures.Add(entry.Name);
            }
        }

        Refresh();
        if (failures.Count == 0) {
            Status = string.Format(Strings.StatusRestored, restored);
        } else {
            Warn(string.Format(Strings.StatusRestoredPartly, restored, failures.Count, failures[0]));
        }
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
            Fail(Strings.StatusClipboardVirtualFiles);
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


    /// <summary>Copy: the target's items, where the place allows it - an archive included, the bin not.</summary>
    private bool CanCopy(object? parameter) {
        var (target, place) = ResolveTarget(parameter);

        return TargetRules.Items(target).Count > 0 && (!place.IsReadOnly || place.IsArchive);
    }

    /// <summary>Cut: the target's items, where the place can be written to.</summary>
    private bool CanCut(object? parameter) {
        var (target, place) = ResolveTarget(parameter);

        return TargetRules.Items(target).Count > 0 && !place.IsReadOnly;
    }

    private void Copy(object? parameter) {
        if (!CanCopy(parameter)) {
            return;
        }

        var (target, place) = ResolveTarget(parameter);
        var items = TargetRules.Items(target);
        var paths = WithCompanions(items).ToList();

        // From inside an archive the paths name no file another program
        // could open, so what goes out to the system is the shell's own
        // data object over the same entries - the one Explorer puts there
        // when you copy out of a zip. Wander's own paste keeps working off
        // the paths either way.
        _clipboard.Copy(paths, ArchiveDataObject(paths));

        if (ClipboardWriteIssue() is { } issue) {
            Warn(issue);
        } else {
            Status = string.Format(place.IsArchive ? Strings.StatusArchiveCopied : Strings.StatusCopied, items.Count);
        }
    }

    /// <summary>
    /// The shell's data object for <paramref name="paths"/> when they lead
    /// into an archive, and null for ordinary files - those travel as the
    /// file list every application understands.
    /// </summary>
    private object? ArchiveDataObject(IReadOnlyList<string> paths) {
        return paths.Any(Archives.Inside) && TryGetShellNamespace() is { } ns
            ? ns.CreateDataObject(paths)
            : null;
    }

    private void Cut(object? parameter) {
        if (!CanCut(parameter)) {
            return;
        }

        var items = TargetRules.Items(ResolveTarget(parameter).Target);
        _clipboard.Cut(WithCompanions(items));
        if (ClipboardWriteIssue() is { } issue) {
            Warn(issue);
        } else {
            Status = string.Format(Strings.StatusCut, items.Count);
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

    /// <summary>
    /// Paste, where there is something to paste and a folder to take it
    /// that can be written to (<see cref="TargetRules.PasteFolder"/>).
    /// </summary>
    private bool CanPaste(object? parameter) {
        var (target, place) = ResolveTarget(parameter);

        return _clipboard.CanPaste && !place.IsReadOnly
            && TargetRules.PasteFolder(target, _nav.Current, fromMenu: parameter is MenuCall) is not null;
    }

    /// <summary>
    /// Pastes into the folder <see cref="TargetRules.PasteFolder"/> names:
    /// a panel row under the cursor or right-clicked, the one folder a row
    /// menu was opened on, the open folder otherwise - Ctrl+V in the list
    /// pastes into the open folder whatever row is selected, as in Explorer.
    /// </summary>
    private async Task PasteAsync(object? parameter) {
        if (!CanPaste(parameter)) {
            return;
        }

        // Where it goes, and why - the why is in the log, because "it went
        // into the wrong folder" has to be readable there (2026-09-22).
        var subject = ResolveTarget(parameter).Target;
        string target = TargetRules.PasteFolder(subject, _nav.Current, fromMenu: parameter is MenuCall)!;
        string how = subject.Kind switch {
            TargetKind.PanelRow => "panel row",
            TargetKind.Background => "folder background",
            TargetKind.ListRows when !IsSamePath(target, _nav.Current) => "selected row",
            _ => "open folder",
        };

        // What is on the clipboard now, not at the last activation: text
        // copied inside Wander while it stayed in front is the paste's too
        // (PLAN X). One kind per paste - files, else text, else a picture.
        _clipboard.SyncFromSystem();
        var choice = _clipboard.Choice;
        if (choice.Kind != PasteKind.Files) {
            await PasteContentAsync(choice, target, how);

            return;
        }
        NoteLeftOnClipboard(choice);

        var sources = _clipboard.Paths.ToList();

        var reason = PathSafety.DetectSelfDrop(sources, target, out string? offender);
        if (reason == SelfDropReason.IntoOwnDescendant || reason == SelfDropReason.Same) {
            string text = PathSafety.FormatReason(reason, offender, target);
            _dialogs.Ask(new DialogRequest(
                DialogKind.CannotPaste, Strings.CannotPasteTitle, text, DialogButtons.Ok, DialogIcon.Warning));
            Fail(text);
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
            _log.Info($"Paste: cut into its own folder, cut dropped ({sources.Count} item(s) in {target}, {how})");
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

        _log.Info($"Paste: {(wasCut ? "move" : "copy")} {groups.Count} item(s) into {target} ({how})");
        var resolver = _dialogs.CreateConflictResolver(Settings.SkipIdenticalOnConflict);
        IReadOnlyList<BatchItemResult> results;
        if (wasCut) {
            ReleasePreview(groups.SelectMany(g => g.All));
        }
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
            Fail(string.Format(Strings.StatusPasteFailed, ex.Message));
            return;
        } finally {
            RestorePreview();
        }

        if (wasCut) {
            _clipboard.Clear();
        }
        // Select what just arrived — the whole point of the operation is now
        // on screen, and the keyboard should already be on it. Only in the
        // folder on screen (N8): pasted into a subfolder, the rows are not
        // in this listing, and an intent left waiting for them took the
        // selection and the keyboard whenever anyone walked in there later.
        var arrived = results
            .Where(r => r.Status is BatchItemStatus.Ok or BatchItemStatus.Replaced or BatchItemStatus.Renamed or BatchItemStatus.Merged)
            .Select(r => r.FinalDestination)
            .ToArray();
        _session.SetArrivalHere(ArrivalIntent.Rows(target, arrived, takeFocus: arrived.Length > 0));
        if (!FollowMoved(results, target, moved: wasCut)) {
            Refresh();
        }
        ReportBatchResults(results, wasCut ? Strings.VerbMoved : Strings.VerbCopied, target);
    }

    /// <summary>
    /// Ctrl+V with no files on the clipboard (PLAN X): its text as a new
    /// .txt file - UTF-8, no byte-order mark - or its picture as a new
    /// .png, named by the resources (PasteTextFileName, PasteImageFileName),
    /// in the folder the paste is for; "(N)" when the name is taken, and
    /// selected with its name editor open, as a new folder is. Nothing is
    /// replaced, so nothing is asked; Ctrl+Z puts the file in the recycle
    /// bin. Nothing Wander can take: the status bar, and so the journal,
    /// names what was there.
    /// </summary>
    private async Task PasteContentAsync(PasteChoice choice, string target, string how) {
        if (choice.Kind == PasteKind.None) {
            if (_clipboard.LastSystemIssue == ClipboardController.SystemIssue.VirtualFiles) {
                Fail(Strings.StatusClipboardVirtualFiles);

                return;
            }

            string formats = string.Join(", ", _clipboard.FormatNames());
            _log.Info($"Paste: nothing to take, the clipboard holds {formats}");
            Warn(string.Format(Strings.StatusPasteNothing, formats));

            return;
        }

        bool text = choice.Kind == PasteKind.Text;
        byte[]? content = text
            ? _clipboard.ReadText() is { } read ? new System.Text.UTF8Encoding(false).GetBytes(read) : null
            : await Task.Run(_clipboard.ReadImagePng);
        if (content is null) {
            Fail(Strings.StatusPasteUnreadable);

            return;
        }

        string made;
        try {
            made = _ops.CreateFile(target, text ? Strings.PasteTextFileName : Strings.PasteImageFileName, content);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            _log.Error($"Paste: cannot write into {target}", ex);
            Fail(string.Format(Strings.StatusPasteFailed, ex.Message));

            return;
        }

        _log.Info($"Paste: {(text ? "text" : "a picture")} as a file into {target} ({how})");
        NoteLeftOnClipboard(choice);
        Status = string.Format(text ? Strings.StatusPastedText : Strings.StatusPastedImage, Path.GetFileName(made));
        // Only in the folder on screen (N8): pasted into a panel row, the
        // file is not in this listing.
        if (IsSamePath(target, _nav.Current)) {
            _session.SetArrivalHere(ArrivalIntent.Rows(target, new[] { made }, takeFocus: true, renameTarget: made));
            Refresh();
        }
    }

    /// <summary>
    /// What else was on the clipboard, left there by the paste - a line in
    /// the journal and the log rather than the status bar, which says what
    /// was pasted (PLAN X, decision of 2026-09-24).
    /// </summary>
    private void NoteLeftOnClipboard(PasteChoice choice) {
        foreach (var left in choice.Left) {
            string line = (choice.Kind, left) switch {
                (PasteKind.Text, PasteKind.Image) => Strings.PasteLeftImageForText,
                (PasteKind.Files, PasteKind.Text) => Strings.PasteLeftTextForFiles,
                (PasteKind.Files, PasteKind.Image) => Strings.PasteLeftImageForFiles,
                _ => "",
            };
            if (line.Length > 0) {
                Journal.Note(line, DateTime.Now);
                _log.Info($"Paste: {left} left on the clipboard, {choice.Kind} taken");
            }
        }
    }

    // --- Archives: extraction and the temporary copy --------------------

    /// <summary>
    /// True when "Извлечь..." has something to work on: rows of the list inside
    /// an archive, or archives selected in an ordinary folder.
    /// </summary>
    private bool CanExtractSelection(object? parameter) {
        return ListRowsOf(parameter) is not null
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
    /// "Извлечь..." — asks where, then extracts. Inside an archive the
    /// selection is what comes out; on an archive standing in an ordinary
    /// folder it is everything the archive holds, which is what the shell's
    /// own "Извлечь все..." does one row above.
    /// </summary>
    private async Task ExtractSelectionAsync(object? parameter) {
        if (!CanExtractSelection(parameter) || TryGetShellNamespace() is not { } ns
            || ExtractionSources(ns, _selectedEntries) is not { } sources) {
            return;
        }

        string? target = _dialogs.PickFolder(Strings.ExtractPickFolderTitle);
        if (string.IsNullOrEmpty(target)) {
            return;
        }

        await ExtractAsync(sources, target);
    }

    /// <summary>
    /// "Извлечь рядом" (2026-09-23): what "Извлечь..." takes out, into the
    /// folder the archive sits in, with no question at all - a name already
    /// there is kept and the newcomer gets "(1)" (pillar 2: asking nothing,
    /// it may not replace anything). Archives picked in search results can
    /// sit in several folders; each comes out beside its own.
    /// </summary>
    private async Task ExtractHereAsync(object? parameter) {
        if (!CanExtractSelection(parameter) || TryGetShellNamespace() is not { } ns) {
            return;
        }

        var batches = CurrentArchive is { } archive
            ? new[] { (Folder: Path.GetDirectoryName(archive.Archive), Rows: _selectedEntries) }
            : _selectedEntries
                .GroupBy(e => Path.GetDirectoryName(e.FullPath), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Folder: g.Key, Rows: (IReadOnlyList<FileSystemEntry>)g.ToList()))
                .ToArray();
        foreach (var (folder, rows) in batches) {
            if (string.IsNullOrEmpty(folder) || ExtractionSources(ns, rows) is not { } sources) {
                continue;
            }

            await ExtractAsync(sources, folder, new FixedConflictResolver(ConflictResolution.Rename));
        }
    }

    /// <summary>
    /// What an extraction takes out of <paramref name="rows"/>: inside an
    /// archive the rows themselves; archives in an ordinary folder give
    /// everything they hold, which is what the shell's own "Извлечь все..."
    /// does one row above. Null, said in the status bar, when that is nothing.
    /// </summary>
    private IReadOnlyList<string>? ExtractionSources(IShellNamespace ns, IReadOnlyList<FileSystemEntry> rows) {
        var sources = CurrentArchive is not null
            ? rows.Select(e => e.FullPath).ToList()
            : rows.SelectMany(e => TopLevelOf(ns, e.FullPath)).ToList();
        if (sources.Count == 0) {
            Warn(string.Format(Strings.StatusArchiveEmptyOrLocked, rows[0].Name));

            return null;
        }

        return sources;
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
    /// sources into a real folder and both "Извлечь" rows end here. A name
    /// already taken is asked about, unless <paramref name="resolver"/>
    /// answers instead.
    /// </summary>
    private async Task ExtractAsync(IReadOnlyList<string> sources, string target, IConflictResolver? resolver = null) {
        if (TryGetShellNamespace() is not { } ns) {
            return;
        }

        var service = new ExtractionService(ns, _fs, ServiceLocator.Get<IRecycleBin>(), _undo, _tracker, _log, _claims);
        resolver ??= _dialogs.CreateConflictResolver(Settings.SkipIdenticalOnConflict);
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
            Fail(string.Format(Strings.StatusExtractFailed, ex.Message));
            return;
        }

        // Select what arrived, but only when the folder it arrived in is the
        // one on screen: an extraction started from inside the archive lands
        // somewhere the user is not standing.
        var arrived = results
            .Where(r => r.Status is BatchItemStatus.Ok or BatchItemStatus.Replaced or BatchItemStatus.Renamed or BatchItemStatus.Merged)
            .Select(r => r.FinalDestination)
            .ToArray();
        _session.SetArrivalHere(ArrivalIntent.Rows(target, arrived, takeFocus: arrived.Length > 0));

        Refresh();
        ReportBatchResults(results, Strings.VerbExtracted, target);

        // Nothing came out, and the shell named no cause: it walked the whole
        // batch and wrote no bytes, which is what a password does. A cause
        // it did name - a full disk, a read-only medium - is already said
        // above, in its own words.
        if (arrived.Length == 0 && results.All(r => r.Status is BatchItemStatus.Failed)
            && results.Any(r => r.Error is ArchiveLockedException)) {
            Fail(Strings.StatusArchiveLocked);
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
            Fail(string.Format(Strings.StatusOpenFailed, ex.Message));
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
        string waited = BusyNote(results.Select(r => r.Busy));
        var failures = results.Where(r => r.Status == BatchItemStatus.Failed).Select(r => r.Error).ToList();
        Say(string.Join(", ", parts) + waited, OutcomeSeverity(ok, failures, waited));
    }

    /// <summary>
    /// "; 'a.txt' was held (Word (PID 812)) - let go after 0.4 s" for what
    /// an operation waited out (PLAN AF), or empty when it waited for
    /// nothing. The first by name, the rest as a count. A hold the wait did
    /// not outlast is a failure, and is told as one.
    /// </summary>
    private static string BusyNote(IEnumerable<BusyReport?> reports) {
        var released = reports.OfType<BusyReport>().Where(r => r.Released).ToList();
        if (released.Count == 0) {
            return "";
        }

        var first = released[0];
        string name = Path.GetFileName(first.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string holder = first.Holder is null ? "" : $" ({first.Holder})";
        double seconds = released.Max(r => r.Waited).TotalSeconds;

        return "; " + (released.Count == 1
            ? string.Format(Strings.StatusWasBusyOne, name, holder, seconds)
            : string.Format(Strings.StatusWasBusyMany, name, released.Count - 1, holder, seconds));
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
            _log.Error($"CreateFolder failed in {_nav.Current}: {Log.Path(name)}", ex);
            Fail(string.Format(Strings.StatusCreateFailed, ex.Message));

            return;
        }

        // "New folder" is never the name anyone wanted, so the next thing
        // the user does is type over it. Selecting it and opening the editor
        // are both for the listing Refresh is about to start — the row does
        // not exist to select, let alone edit, until it lands.
        string created = Path.Combine(_nav.Current, name);
        _session.SetArrivalHere(ArrivalIntent.Rows(
            _nav.Current, new[] { created }, takeFocus: true, renameTarget: created));
        Refresh();

        // The panels beside the list show folders too, and the folder they
        // are standing on just gained one. Leaving it to the watcher would
        // not do: its tick is held for as long as the editor we just opened
        // is up.
        Workspace.Post(new FolderChanged(_nav.Current));
    }

    private string DescribeError(Exception ex, string path) {
        if (ex is RecycleUnavailableException) {
            // The journal is the user's: it names the file, not only the reason.
            return string.Format(Strings.ErrorRecycleUnavailable, Path.GetFileName(path));
        }
        if (ex is ClaimedByOperationException claimed) {
            return string.Format(Strings.ErrorClaimedByOperation, Path.GetFileName(path), Strings.Get(claimed.Verb));
        }
        if (ex is IOException && _lockInspector is not null) {
            var lockers = _lockInspector.WhoIsLocking(path);
            if (lockers.Count > 0) {
                return string.Format(Strings.ErrorFileInUse, FileLockInfo.Describe(lockers));
            }
            // In use with nobody to name: the lock was a moment's, or held
            // by something Restart Manager does not see.
            if (FileInUse.Is(ex)) {
                return Strings.ErrorInUse;
            }
        }
        return ex.Message;
    }

    // --- Operation progress (status bar) -------------------------------

    /// <summary>
    /// Cancels every operation, the way each window's own Cancel would -
    /// what an exit with operations running does first. Returns how many
    /// were told.
    /// </summary>
    public int CancelAllOperations() {
        var windows = _operationWindows.ToList();
        foreach (var window in windows) {
            window.RequestCancel();
        }

        return windows.Count;
    }


    private void OnTrackerChanged(object? sender, EventArgs e) {
        if (_dispatcher.CheckAccess()) {
            RebuildOperations();
        } else {
            _dispatcher.BeginInvoke(RebuildOperations);
        }
    }

    /// <summary>
    /// Brings the rows in step with the tracker, in place: an operation that
    /// is still running keeps its row, and with it the running speed average
    /// and any button the pointer is on. Only ones that appeared or finished
    /// change the collection.
    /// </summary>
    private void RebuildOperations() {
        var snapshots = _tracker.Snapshot();
        var now = DateTime.UtcNow;

        for (int i = Operations.Count - 1; i >= 0; i--) {
            if (!snapshots.Any(s => s.Id == Operations[i].Id)) {
                Operations.RemoveAt(i);
            }
        }

        long doneWeighted = 0;
        long totalWeighted = 0;
        for (int i = 0; i < snapshots.Count; i++) {
            var snapshot = snapshots[i];
            var row = Operations.FirstOrDefault(o => o.Id == snapshot.Id);
            if (row is null) {
                row = new OperationViewModel(snapshot.Id, ShowOperationWindow, CancelOperation);
                Operations.Insert(Math.Min(i, Operations.Count), row);
            }
            row.Update(snapshot, now);

            // Bytes where there are bytes, items where there are not: the
            // two are added on one scale so a big copy is not outvoted by a
            // two-file delete.
            if (snapshot.HasBytes) {
                doneWeighted += snapshot.BytesDone;
                totalWeighted += snapshot.BytesTotal;
            } else if (snapshot.Total > 0) {
                doneWeighted += snapshot.Completed;
                totalWeighted += snapshot.Total;
            }
        }

        AggregateProgress = totalWeighted > 0
            ? Math.Clamp((double)doneWeighted * 100.0 / totalWeighted, 0.0, 100.0)
            : 0.0;
        OperationsSummary = Operations.Count switch {
            0 => "",
            1 => Operations[0].Summary,
            _ => string.Format(Strings.OperationMany, Operations.Count, (int)Math.Round(AggregateProgress)),
        };
        Raise(nameof(HasActiveOperations));
    }

    /// <summary>Brings an operation's window back from the status bar.</summary>
    private void ShowOperationWindow(long operationId) {
        WindowOf(operationId)?.Restore();
    }

    /// <summary>The panel's Cancel, routed to the window that owns the token.</summary>
    private void CancelOperation(long operationId) {
        WindowOf(operationId)?.RequestCancel();
    }

    private Wander.App.Views.ProgressDialog? WindowOf(long operationId) {
        return _operationWindows.FirstOrDefault(w => w.OperationId == operationId);
    }


    /// <summary>
    /// Run an async batch op with its own <see cref="Wander.App.Views.ProgressDialog"/>.
    /// The window comes up once the work has run for
    /// <see cref="_operationWindowDelay"/> - never, when it is over sooner -
    /// and without taking the focus: it appears in the middle of whatever
    /// the user went on to do. It waits out a modal question (the conflict
    /// window is asked from inside the work) rather than come up over it.
    /// It follows the operation the work
    /// registers in <see cref="_tracker"/>, and is closed here, before this
    /// returns, when <paramref name="work"/> finishes (success, failure, or
    /// user cancel). Returns whatever the work returned; rethrows
    /// <see cref="OperationCanceledException"/> when the user cancels, so
    /// callers can show a uniform message.
    ///
    /// <para>
    /// Not modal (PLAN, block 2): the list stays live while a long copy
    /// runs, the way it does in Explorer. What used to be ShowDialog
    /// blocking this continuation is now the plain await below - the window
    /// is a display, not a gate.
    /// </para>
    ///
    /// <para>
    /// Closed synchronously in the finally, not by the window from a
    /// continuation of the task: the caller may put a question about the
    /// outcome to the user the moment this returns, and that question must
    /// not find this window still open and active - it would take it as its
    /// owner and be destroyed with it a moment later (2026-09-17, the
    /// "file is in use" question after a delete).
    /// </para>
    /// </summary>
    private async Task<TResult> RunWithProgressDialogAsync<TResult>(string headline, Func<CancellationToken, Task<TResult>> work) {
        // The window finds its operation by the token it hands the work:
        // the operation registers under that token several layers down.
        var dlg = new Wander.App.Views.ProgressDialog(headline, _tracker) {
            Owner = Application.Current?.MainWindow,
            ShowActivated = false,
        };
        _operationWindows.Add(dlg);
        dlg.Closed += (_, _) => _operationWindows.Remove(dlg);

        try {
            var task = work(dlg.Token);
            await Task.WhenAny(task, Task.Delay(_operationWindowDelay)).ConfigureAwait(true);
            while (!task.IsCompleted && System.Windows.Interop.ComponentDispatcher.IsThreadModal) {
                await Task.WhenAny(task, Task.Delay(_operationWindowRecheck)).ConfigureAwait(true);
            }
            if (!task.IsCompleted) {
                dlg.Show();
            }

            return await task.ConfigureAwait(true);
        } finally {
            dlg.Finish();
        }
    }

    /// <summary>
    /// The debug menu's "Операция" (PLAN AI1). The parameter is a
    /// <see cref="Diagnostics.DebugOperationScenario"/> by name, or
    /// <see cref="ThreeAtOnce"/> - three plain runs side by side, which is
    /// about the status bar rather than about any one of them.
    /// </summary>
    private async Task RunDebugOperationAsync(string? scenarioName) {
        if (scenarioName == ThreeAtOnce) {
            await Task.WhenAll(Enumerable.Range(0, 3)
                .Select(_ => RunDebugScenarioAsync(Diagnostics.DebugOperationScenario.Plain)));

            return;
        }

        if (!Enum.TryParse(scenarioName, out Diagnostics.DebugOperationScenario scenario)) {
            _log.Warn($"Debug operation: no scenario named '{scenarioName}'");

            return;
        }

        await RunDebugScenarioAsync(scenario);
    }

    /// <summary>One debug run, in a window of its own, ending in the status line like a real one.</summary>
    private async Task RunDebugScenarioAsync(Diagnostics.DebugOperationScenario scenario) {
        Diagnostics.DebugOperationOutcome outcome;
        try {
            outcome = await RunWithProgressDialogAsync(
                Strings.ProgressDebug,
                ct => Diagnostics.DebugOperation.RunAsync(scenario, _tracker, _log, ct));
        } catch (OperationCanceledException) {
            Status = Strings.StatusCancelled;

            return;
        }

        Say(string.Format(Strings.StatusDebugDone, outcome.Ok, outcome.Total),
            outcome.Ok < outcome.Total ? StatusSeverity.Warning : StatusSeverity.Info);
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
