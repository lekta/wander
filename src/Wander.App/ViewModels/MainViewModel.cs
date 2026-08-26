using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Wander.App.Conflict;
using Wander.App.Resources;
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
using Wander.Core.Shell;
using Wander.Core.Undo;

namespace Wander.App.ViewModels;

public sealed class MainViewModel : ObservableObject {
    /// <summary>How long a folder may take to list before the spinner shows.</summary>
    private const int SpinnerDelayMs = 150;

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
    private ViewMode _viewMode = ViewMode.Details;

    private bool _isPreviewVisible;
    private double _previewWidth = 280;
    private double _bookmarksHeight = 200;

    private readonly ClipboardController _clipboard = new();

    private readonly List<string> _favorites = new();
    private bool _isBookmarksExpanded = true;
    private bool _buildingBookmarks;
    private IReadOnlyList<NavigationStop> _persistedExpandedPaths = Array.Empty<NavigationStop>();

    private CancellationTokenSource? _listLoadCts;
    private bool _isListLoading;

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

    // Search filter is owned by SearchController; we only keep the hidden-
    // count separately for the "X items (N hidden)" status-bar message.
    private readonly SearchController _search = new();
    private int _hiddenCount;

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

        Preview = new PreviewController(
            ServiceLocator.IsRegistered<IImageMetadataReader>()
                ? ServiceLocator.Get<IImageMetadataReader>()
                : null,
            ServiceLocator.IsRegistered<CompanionMetadataService>()
                ? ServiceLocator.Get<CompanionMetadataService>()
                : null);

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
        RefreshCommand = new RelayCommand(_ => Refresh());
        SetViewModeCommand = new RelayCommand(p => SetViewMode(p as string));
        SetSortKeyCommand = new RelayCommand(p => SetSortKey(p as string));
        ToggleSortAscendingCommand = new RelayCommand(_ => Settings.SortAscending = !Settings.SortAscending);
        ToggleGroupFoldersFirstCommand = new RelayCommand(_ => Settings.GroupFoldersFirst = !Settings.GroupFoldersFirst);
        ExitCommand = new RelayCommand(_ => Application.Current?.Shutdown());
        OptionsCommand = new RelayCommand(_ => OpenSettingsDialog());
        ReportIssueCommand = new RelayCommand(_ => ReportIssue());
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

        _search.PropertyChanged += (_, e) => {
            // SearchController owns the underlying _query; surface the
            // changes under the names XAML/binding consumers already expect.
            if (e.PropertyName == nameof(SearchController.Query)) {
                Raise(nameof(SearchQuery));
            } else if (e.PropertyName == nameof(SearchController.HasQuery)) {
                Raise(nameof(HasSearchQuery));
            }
        };
        _search.FilteredChanged += filtered => {
            SyncEntries(filtered);
            UpdateFilterStatus(filtered.Count, _search.Source.Count);
            ApplyRestoreSelection();
            ApplyPendingFolderSelection();
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
            if (SetField(ref _selectedEntry, value)) {
                Preview.SetPrimary(value);
            }
        }
    }

    public IReadOnlyList<FileSystemEntry> SelectedEntries {
        get => _selectedEntries;
        set {
            if (SetField(ref _selectedEntries, value)) {
                Preview.SetSelection(value);
            }
        }
    }

    /// <summary>
    /// Live filter over the current folder. Empty string = no filter.
    /// Delegates to <see cref="SearchController"/> which handles the
    /// async pass + cancellation; we just forward XAML bindings here so
    /// the existing TextBox binding (<c>{Binding SearchQuery, ...}</c>)
    /// keeps working.
    /// </summary>
    public string SearchQuery {
        get => _search.Query;
        set => _search.Query = value;
    }

    public bool HasSearchQuery => _search.HasQuery;

    public ViewMode ViewMode {
        get => _viewMode;
        set {
            if (SetField(ref _viewMode, value)) {
                SaveState();
            }
        }
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
    public RelayCommand RestoreFromRecycleBinCommand { get; }
    public RelayCommand SetViewModeCommand { get; }
    public RelayCommand SetSortKeyCommand { get; }
    public RelayCommand ToggleSortAscendingCommand { get; }
    public RelayCommand ToggleGroupFoldersFirstCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand OptionsCommand { get; }
    public RelayCommand ReportIssueCommand { get; }
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
                _viewMode = mode;
                Raise(nameof(ViewMode));
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
                ViewMode = _viewMode.ToString(),
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
        // Drop any active filter when the user moves to a new folder — the
        // filter is scoped to "the folder I'm looking at right now".
        // SearchController.Reset cancels any in-flight pass; the upcoming
        // Refresh → SetSource will reapply the (now empty) query.
        _search.Reset();
        Refresh();
        ExpandTreeToCurrent();
        Preview.SetCurrentFolder(_nav.Current, WindowTitle);
        SaveState();
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
        if (!IsSamePath(path, _listedPath)) {
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
            // A file operation may have reported its outcome ("Copied 3
            // items") while we were enumerating; the listing's own
            // "N items" must not eat that message.
            string reported = Status;
            _search.SetSource(items);
            if (reported != statusBeforeLoad) {
                Status = reported;
            }
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
            if (!SameRow(Entries[i], want)) {
                Entries[i] = want;
            }
        }
    }


    /// <summary>
    /// Whether two listings of the same file say the same thing. Record
    /// equality is not usable here: <see cref="FileSystemEntry.Companions"/>
    /// is a list, and two enumerations of the same folder produce two
    /// instances of it — so every row with a sidecar would count as changed
    /// and lose its container on every single refresh.
    /// </summary>
    private static bool SameRow(FileSystemEntry a, FileSystemEntry b) {
        return a.Name == b.Name
            && a.Kind == b.Kind
            && a.Size == b.Size
            && a.ModifiedUtc == b.ModifiedUtc
            && a.IsHidden == b.IsHidden
            && a.IsReadOnly == b.IsReadOnly
            && a.IsSystem == b.IsSystem
            && a.LinksToDirectory == b.LinksToDirectory
            && a.OriginalLocation == b.OriginalLocation
            && SameCompanions(a.Companions, b.Companions);
    }

    private static bool SameCompanions(IReadOnlyList<string>? a, IReadOnlyList<string>? b) {
        if (a is null || a.Count == 0) {
            return b is null || b.Count == 0;
        }
        if (b is null || a.Count != b.Count) {
            return false;
        }

        for (int i = 0; i < a.Count; i++) {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }
        return true;
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
        if (_restoreSelection.Length == 0) {
            return;
        }

        var wanted = new HashSet<string>(_restoreSelection, StringComparer.OrdinalIgnoreCase);
        _restoreSelection = Array.Empty<string>();

        var found = Entries.Where(e => wanted.Contains(e.FullPath)).ToList();
        if (found.Count == 0) {
            return;
        }

        SelectedEntry = found[0];
        SelectedEntries = found;
        SelectionRestoreRequested?.Invoke(found);
    }

    private void UpdateFilterStatus(int shown, int total) {
        if (_search.HasQuery) {
            Status = total > 0
                ? string.Format(Strings.StatusFilterMatches, shown, total, _search.Query)
                : string.Format(Strings.StatusItems, shown);
        } else if (_hiddenCount > 0) {
            Status = string.Format(Strings.StatusItemsWithHidden, shown, _hiddenCount);
        } else {
            Status = string.Format(Strings.StatusItems, shown);
        }
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

    private void SetViewMode(string? name) {
        if (Enum.TryParse<ViewMode>(name, out var mode)) {
            ViewMode = mode;
        }
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

        // Tile geometry is a projection of the four LargeIcon* settings, not
        // a setting of its own: the knob that moved has already come through
        // here and saved. Falling through would just save the same state a
        // second time on every keystroke in the settings dialog.
        if (e.PropertyName == nameof(SettingsViewModel.IconsMetrics)) {
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
            Refresh();
        }

        if (e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDownloads) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDocuments) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkPictures) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkRecycleBin)) {
            BuildBookmarks();
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

    private void ReportIssue() {
        // GitHub's template chooser lets the user pick "Bug report" or
        // "Feature request"; nothing is pre-filled, so no session data is
        // involved — unlike the crash path, which bundles diagnostics.
        try {
            _shell.Open(Wander.App.Diagnostics.CrashReporter.IssueChooserUrl);
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

        Refresh();

        int ok = results.Count(r => r.Status == DeleteStatus.Ok);
        int failed = results.Count(r => r.Status == DeleteStatus.Failed);
        if (failed > 0) {
            var firstFail = results.First(r => r.Status == DeleteStatus.Failed);
            string detail = firstFail.Error is null ? "" : ": " + DescribeError(firstFail.Error, firstFail.Path);
            Status = string.Format(
                permanent ? Strings.StatusDeletedPartly : Strings.StatusRecycledPartly, ok, failed, detail);
        } else {
            Status = string.Format(permanent ? Strings.StatusDeleted : Strings.StatusRecycled, ok);
        }
    }

    private void UndoLast() {
        try {
            var action = _undo.Undo();
            if (action is null) {
                return;
            }
            _log.Info($"Undo: {action.Description}");
            Status = string.Format(Strings.StatusUndone, action.Description);
            // Point the user at what came back, not at wherever the
            // selection happened to be.
            _restoreSelection = action.PathsAfterUndo.ToArray();
            Refresh();
            // A rating undo rewrites a sidecar the footer is already
            // showing; the listing refresh above wouldn't touch it.
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
        Status = string.Format(Strings.StatusCopied, _selectedEntries.Count);
    }

    private void Cut() {
        if (_selectedEntries.Count == 0) {
            return;
        }
        _clipboard.Cut(WithCompanions(_selectedEntries));
        Status = string.Format(Strings.StatusCut, _selectedEntries.Count);
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
