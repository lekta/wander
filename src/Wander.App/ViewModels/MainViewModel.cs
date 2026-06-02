using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Wander.App.Conflict;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Navigation;
using Wander.Core.Operations;
using Wander.Core.Persistence;
using Wander.Core.Shell;
using Wander.Core.Undo;

namespace Wander.App.ViewModels;

public sealed class MainViewModel : ObservableObject {
    private readonly IFileSystem _fs;
    private readonly IShellLauncher _shell;
    private readonly IAppStateStore _stateStore;
    private readonly IFileLockInspector? _lockInspector;
    private readonly NavigationService _nav = new();
    private readonly FileOperationService _ops;
    private readonly UndoService _undo;
    private readonly OperationTracker _tracker;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _log;

    private string _addressText = "";
    private string _status = "";
    private FileSystemEntry? _selectedEntry;
    private IReadOnlyList<FileSystemEntry> _selectedEntries = Array.Empty<FileSystemEntry>();
    private ViewMode _viewMode = ViewMode.Details;

    private bool _isPreviewVisible;
    private double _previewWidth = 280;

    private List<string> _clipboard = new();
    private bool _clipboardIsCut;

    private readonly List<string> _favorites = new();
    private bool _isBookmarksExpanded = true;
    private bool _buildingBookmarks;
    private IReadOnlyList<NavigationStop> _persistedExpandedPaths = Array.Empty<NavigationStop>();

    // Search filter state. _allEntries is the unfiltered post-hidden/system
    // snapshot of the current folder; Entries is the filtered projection
    // shown to the user. _filterCts cancels an in-flight filter when a new
    // keystroke arrives so rapid typing does not pile up stale results.
    private List<FileSystemEntry> _allEntries = new();
    private int _hiddenCount;
    private string _searchQuery = "";
    private CancellationTokenSource? _filterCts;

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

        Preview = new PreviewController(
            ServiceLocator.IsRegistered<IImageMetadataReader>()
                ? ServiceLocator.Get<IImageMetadataReader>()
                : null);

        Entries = new ObservableCollection<FileSystemEntry>();
        Roots = new ObservableCollection<TreeNodeViewModel>();
        Bookmarks = new ObservableCollection<TreeNodeViewModel>();
        Operations = new ObservableCollection<OperationViewModel>();

        // Settings VM is owned by MainVM and shared with the dialog when it
        // opens. Any mutation triggers a refresh-or-save side effect via
        // OnSettingsChanged.
        Settings = new SettingsViewModel();
        Settings.PropertyChanged += OnSettingsChanged;

        _tracker.Changed += OnTrackerChanged;

        BackCommand = new RelayCommand(_ => GoBack(), _ => _nav.CanGoBack);
        ForwardCommand = new RelayCommand(_ => GoForward(), _ => _nav.CanGoForward);
        UpCommand = new RelayCommand(_ => GoUp(), _ => _nav.CanGoUp);
        NavigateCommand = new RelayCommand(_ => NavigateToAddress());
        OpenCommand = new RelayCommand(p => OpenEntry(p as FileSystemEntry ?? _selectedEntry), _ => _selectedEntry is not null);
        // Destructive ops are blocked inside shell namespaces (Recycle Bin):
        // the entries' FullPaths point at $Recycle.Bin backing files, and
        // copying / deleting / renaming those would bypass the shell's
        // restore-tracking and corrupt the bin's state. Read-only browsing
        // only in this iteration.
        DeleteCommand = new RelayCommand(_ => _ = DeleteSelectedAsync(permanent: false), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        RenameCommand = new RelayCommand(p => Rename(p as string), _ => _selectedEntry is not null && !IsCurrentShellNamespace);
        CopyCommand = new RelayCommand(_ => Copy(), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        CutCommand = new RelayCommand(_ => Cut(), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        PasteCommand = new RelayCommand(_ => _ = PasteAsync(), _ => _clipboard.Count > 0 && _nav.Current is not null && !IsCurrentShellNamespace);
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => _nav.Current is not null && !IsCurrentShellNamespace);
        RefreshCommand = new RelayCommand(_ => Refresh());
        SetViewModeCommand = new RelayCommand(p => SetViewMode(p as string));
        ExitCommand = new RelayCommand(_ => Application.Current?.Shutdown());
        OptionsCommand = new RelayCommand(_ => OpenSettingsDialog());
        PropertiesCommand = new RelayCommand(_ => ShowProperties(), _ => _selectedEntry is not null);
        TogglePreviewCommand = new RelayCommand(_ => IsPreviewVisible = !IsPreviewVisible);
        UndoCommand = new RelayCommand(_ => UndoLast(), _ => _undo.CanUndo);
        PermanentDeleteCommand = new RelayCommand(_ => _ = DeleteSelectedAsync(permanent: true), _ => _selectedEntries.Count > 0 && !IsCurrentShellNamespace);
        OpenLogFileCommand = new RelayCommand(_ => OpenLogFile(), _ => ServiceLocator.IsRegistered<ILogFile>());
        ToggleBookmarksCommand = new RelayCommand(_ => IsBookmarksExpanded = !IsBookmarksExpanded);
        AddBookmarkCommand = new RelayCommand(p => AddBookmark(p as string));
        RemoveBookmarkCommand = new RelayCommand(p => RemoveBookmark(p as TreeNodeViewModel));

        _undo.Changed += (_, _) => {
            UndoCommand.RaiseCanExecuteChanged();
            Raise(nameof(UndoTooltip));
        };

        _nav.CurrentChanged += (_, _) => OnNavigationChanged();

        LoadRoots();
        RestoreState();
    }


    public ObservableCollection<FileSystemEntry> Entries { get; }
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

    public string? CurrentPath => _nav.Current;

    public string AddressText {
        get => _addressText;
        set => SetField(ref _addressText, value);
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
    /// Setter triggers an async pass over <see cref="_allEntries"/> on a
    /// background thread; the previous in-flight filter is cancelled.
    /// </summary>
    public string SearchQuery {
        get => _searchQuery;
        set {
            value ??= "";
            if (SetField(ref _searchQuery, value)) {
                Raise(nameof(HasSearchQuery));
                _ = ApplyFilterAsync();
            }
        }
    }

    public bool HasSearchQuery => !string.IsNullOrEmpty(_searchQuery);

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

    public RelayCommand BackCommand { get; }
    public RelayCommand ForwardCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand CutCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand NewFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SetViewModeCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand OptionsCommand { get; }
    public RelayCommand PropertiesCommand { get; }
    public RelayCommand TogglePreviewCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand PermanentDeleteCommand { get; }
    public RelayCommand OpenLogFileCommand { get; }
    public RelayCommand ToggleBookmarksCommand { get; }
    public RelayCommand AddBookmarkCommand { get; }
    public RelayCommand RemoveBookmarkCommand { get; }

    public bool IsBookmarksExpanded {
        get => _isBookmarksExpanded;
        set {
            if (SetField(ref _isBookmarksExpanded, value)) {
                SaveState();
            }
        }
    }

    public string UndoTooltip => _undo.NextDescription is { } next ? $"Undo: {next}" : "Nothing to undo";

    public string WindowTitle {
        get {
            if (string.IsNullOrEmpty(_nav.Current)) {
                return "Wander";
            }
            // Shell sentinels (e.g. shell:RecycleBinFolder) don't have a
            // sensible Path.GetFileName — ask the namespace for its
            // localised display label.
            if (TryGetShellNamespace() is { } ns && ns.IsShellPath(_nav.Current)) {
                return ns.GetDisplayName(_nav.Current) ?? _nav.Current;
            }
            string trimmed = _nav.Current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? _nav.Current : name;
        }
    }


    public void NavigateTo(string path, NavigationSource source = NavigationSource.External) {
        if (!PathIsNavigable(path)) {
            _log.Warn($"Navigate: path not found {path}");
            Status = $"Path not found: {path}";
            return;
        }
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
                Status = $"Open failed: {ex.Message}";
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
            Status = "No target folder for drop.";
            return;
        }

        if (effect == DropEffect.Link) {
            CreateShortcuts(sourcePaths, targetFolder);
            return;
        }

        if (effect == DropEffect.Move && !ConfirmMove(sourcePaths, targetFolder)) {
            return;
        }

        _log.Info($"Drop: {effect} {sourcePaths.Count} item(s) into {targetFolder}");
        var resolver = new DispatcherConflictResolver(new InteractiveConflictResolver());
        IReadOnlyList<BatchItemResult> results;
        try {
            results = await RunWithProgressDialogAsync(
                effect == DropEffect.Move ? "Перемещение" : "Копирование",
                ct => effect == DropEffect.Move
                    ? _ops.MoveManyAsync(sourcePaths, targetFolder, resolver, ct)
                    : _ops.CopyManyAsync(sourcePaths, targetFolder, resolver, ct));
        } catch (OperationCanceledException) {
            Status = "Operation cancelled.";
            return;
        } catch (Exception ex) {
            _log.Error($"Drop failed: {effect} -> {targetFolder}", ex);
            Status = $"Drop failed: {ex.Message}";
            return;
        }

        Refresh();
        ReportBatchResults(results, effect == DropEffect.Move ? "Moved" : "Copied", targetFolder);
    }

    private void CreateShortcuts(IReadOnlyList<string> sources, string targetFolder) {
        if (!ServiceLocator.IsRegistered<IShortcutService>()) {
            Status = "Shortcuts are not supported on this platform.";
            return;
        }

        var shortcuts = ServiceLocator.Get<IShortcutService>();
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
                ok++;
            } catch (Exception ex) {
                Status = $"Create shortcut failed for {srcName}: {ex.Message}";
            }
        }

        Refresh();
        if (ok > 0) {
            Status = $"Created {ok} shortcut(s) in {targetFolder}";
        }
    }


    // --- Startup state -------------------------------------------------

    private void RestoreState() {
        var state = _stateStore.Load();

        _restoring = true;
        try {
            // Settings before view mode / navigation: ShowHidden affects
            // what Refresh() displays, so load filters before the first
            // Refresh fires via the navigation change below.
            Settings.ApplyFrom(state.Settings);

            if (!string.IsNullOrEmpty(state.ViewMode) && Enum.TryParse<ViewMode>(state.ViewMode, out var mode)) {
                _viewMode = mode;
                Raise(nameof(ViewMode));
            }

            _isPreviewVisible = state.IsPreviewVisible;
            Raise(nameof(IsPreviewVisible));
            Preview.SetVisible(_isPreviewVisible);
            if (state.PreviewWidth >= 120 && state.PreviewWidth <= 900) {
                _previewWidth = state.PreviewWidth;
                Raise(nameof(PreviewWidth));
            }

            _favorites.Clear();
            _favorites.AddRange(state.Favorites);
            _isBookmarksExpanded = state.IsBookmarksExpanded;
            Raise(nameof(IsBookmarksExpanded));

            _persistedExpandedPaths = state.ExpandedPaths.ToArray();
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

            // Honour the RestoreLastFolder preference: when off, ignore
            // LastPath and start at the first drive.
            bool wantRestore = Settings.RestoreLastFolder
                && state.LastPath is not null
                && _fs.DirectoryExists(state.LastPath.Path);
            if (wantRestore) {
                _nav.NavigateTo(state.LastPath!.Path, state.LastPath.Source);
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

        // Read-modify-write: AppState now also carries window geometry, which
        // the View persists separately. If we replaced the whole record here
        // we'd silently wipe those fields on every navigation/preview toggle.
        var current = _stateStore.Load();
        _stateStore.Save(current with {
            LastPath = _nav.Current is not null
                ? new NavigationStop(_nav.Current, _nav.CurrentSource ?? NavigationSource.External)
                : null,
            ViewMode = _viewMode.ToString(),
            ExpandedPaths = CollectExpanded(),
            IsPreviewVisible = _isPreviewVisible,
            PreviewWidth = _previewWidth,
            Favorites = _favorites.ToArray(),
            IsBookmarksExpanded = _isBookmarksExpanded,
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

    private void NavigateToAddress() {
        if (string.IsNullOrWhiteSpace(AddressText)) {
            return;
        }
        NavigateTo(AddressText.Trim(), NavigationSource.Address);
    }

    private void GoBack() => _nav.GoBack();
    private void GoForward() => _nav.GoForward();
    private void GoUp() => _nav.GoUp();

    private void OnNavigationChanged() {
        AddressText = _nav.Current ?? "";
        Raise(nameof(WindowTitle));
        Raise(nameof(CurrentPath));
        // Drop any active filter when the user moves to a new folder — the
        // filter is scoped to "the folder I'm looking at right now". Bypass
        // the SearchQuery setter so we don't race a stale ApplyFilterAsync
        // against the Refresh below; Refresh will trigger one fresh pass.
        if (_searchQuery.Length > 0) {
            _searchQuery = "";
            Raise(nameof(SearchQuery));
            Raise(nameof(HasSearchQuery));
        }
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
        if (_nav.Current is null) {
            _allEntries = new List<FileSystemEntry>();
            _hiddenCount = 0;
            Entries.Clear();
            Status = "";
            return;
        }

        // Shell namespaces (Recycle Bin etc.) route through IShellNamespace
        // — IFileSystem.Enumerate would throw on shell:RecycleBinFolder.
        // No Hidden/System filtering: shell items don't carry those flags
        // and Wander's "what to hide" preference is filesystem-only.
        if (IsShellPath(_nav.Current) && TryGetShellNamespace() is { } ns) {
            try {
                _allEntries = ns.Enumerate(_nav.Current).ToList();
                _hiddenCount = 0;
                _ = ApplyFilterAsync();
            } catch (Exception ex) {
                _allEntries = new List<FileSystemEntry>();
                _hiddenCount = 0;
                Entries.Clear();
                Status = $"Error: {ex.Message}";
            }
            return;
        }

        bool showHidden = Settings.ShowHidden;
        bool showSystem = Settings.ShowSystem;

        try {
            var list = new List<FileSystemEntry>();
            int hidden = 0;
            foreach (var e in _fs.Enumerate(_nav.Current)) {
                if (!showHidden && e.IsHidden) { hidden++; continue; }
                if (!showSystem && e.IsSystem) { hidden++; continue; }
                list.Add(e);
            }
            _allEntries = list;
            _hiddenCount = hidden;
            _ = ApplyFilterAsync();
        } catch (Exception ex) {
            _allEntries = new List<FileSystemEntry>();
            _hiddenCount = 0;
            Entries.Clear();
            Status = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Re-projects <see cref="_allEntries"/> through the current
    /// <see cref="SearchQuery"/> into <see cref="Entries"/>. Filtering runs
    /// on a background thread so the UI stays responsive even when the
    /// folder is large; a fresh keystroke cancels the previous pass.
    /// </summary>
    private async Task ApplyFilterAsync() {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        // Snapshot the inputs so a Refresh / new keystroke mid-flight cannot
        // race us — we either complete with these inputs or get cancelled.
        string query = _searchQuery;
        var source = _allEntries;

        if (string.IsNullOrEmpty(query)) {
            ReplaceEntries(source);
            UpdateFilterStatus(source.Count, source.Count);
            return;
        }

        List<FileSystemEntry> filtered;
        try {
            filtered = await Task.Run(() => {
                var result = new List<FileSystemEntry>();
                foreach (var e in source) {
                    token.ThrowIfCancellationRequested();
                    if (e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) {
                        result.Add(e);
                    }
                }
                return result;
            }, token);
        } catch (OperationCanceledException) {
            return;
        }

        if (token.IsCancellationRequested) {
            return;
        }
        ReplaceEntries(filtered);
        UpdateFilterStatus(filtered.Count, source.Count);
    }

    private void ReplaceEntries(IReadOnlyList<FileSystemEntry> items) {
        Entries.Clear();
        foreach (var e in items) {
            Entries.Add(e);
        }
    }

    private void UpdateFilterStatus(int shown, int total) {
        if (!string.IsNullOrEmpty(_searchQuery)) {
            Status = total > 0
                ? $"{shown} of {total} items match \"{_searchQuery}\""
                : $"{shown} items";
        } else if (_hiddenCount > 0) {
            Status = $"{shown} items ({_hiddenCount} hidden)";
        } else {
            Status = $"{shown} items";
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

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        // Side effects: re-list when filters change, persist always.
        // Switching the Categories list-selection in the dialog should
        // NOT trigger a Save — that's a UI-only property, not a setting.
        if (e.PropertyName == nameof(SettingsViewModel.SelectedCategory)) {
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

        if (e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDownloads) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkDocuments) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkPictures) ||
            e.PropertyName == nameof(SettingsViewModel.ShowBookmarkRecycleBin)) {
            BuildBookmarks();
        }

        SaveState();
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
                AddSpecialFolderNode("Загрузки", ResolveDownloads());
            }
            if (Settings.ShowBookmarkDocuments) {
                AddSpecialFolderNode("Документы", ResolveDocuments());
            }
            if (Settings.ShowBookmarkPictures) {
                AddSpecialFolderNode("Изображения", ResolvePictures());
            }
            if (Settings.ShowBookmarkRecycleBin && TryGetShellNamespace() is not null) {
                AddSpecialFolderNode("Корзина", ShellPaths.RecycleBin);
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

    public void AddBookmark(string? path) {
        if (string.IsNullOrEmpty(path) || !_fs.DirectoryExists(path)) {
            return;
        }
        if (_favorites.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) {
            Status = "Папка уже в закладках.";
            return;
        }
        _favorites.Add(path);
        _log.Info($"Bookmark added: {path}");
        Status = $"Добавлено в закладки: {Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}";
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

    private void OpenLogFile() {
        if (!ServiceLocator.IsRegistered<ILogFile>()) {
            Status = "Logging is not configured.";
            return;
        }
        string path = ServiceLocator.Get<ILogFile>().FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            Status = "Log file not found.";
            return;
        }
        try {
            _shell.Open(path);
        } catch (Exception ex) {
            Status = $"Open log failed: {ex.Message}";
        }
    }

    private void ShowProperties() {
        if (_selectedEntry is null) {
            return;
        }
        try {
            _shell.ShowProperties(_selectedEntry.FullPath);
        } catch (Exception ex) {
            Status = $"Properties failed: {ex.Message}";
        }
    }


    // --- Destructive / clipboard ops (always confirm, Cancel-default) --

    private async Task DeleteSelectedAsync(bool permanent) {
        if (_selectedEntries.Count == 0) {
            return;
        }

        var snapshot = _selectedEntries.ToList();

        // Permanent (Shift+Delete) always asks. Recycle asks only when the
        // user kept the "confirm" preference on — Ctrl+Z still restores from
        // the bin so skipping the prompt is safe by default.
        bool needsConfirm = permanent || Settings.ConfirmRecycle;
        if (needsConfirm) {
            string verb = permanent ? "Permanently delete" : "Move to recycle bin";
            string title = permanent ? "Confirm permanent deletion" : "Confirm move to recycle bin";
            string message;
            if (snapshot.Count == 1) {
                var e0 = snapshot[0];
                string kind = e0.Kind == EntryKind.Directory ? "folder" : "file";
                message = $"{verb} {kind} '{e0.Name}'?\n\n{e0.FullPath}";
            } else {
                message = $"{verb} {snapshot.Count} items?\n\n" +
                    string.Join("\n", snapshot.Take(5).Select(e => "• " + e.Name)) +
                    (snapshot.Count > 5 ? $"\n… and {snapshot.Count - 5} more" : "");
            }
            if (permanent) {
                message += "\n\nThis cannot be undone.";
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
                (readOnlys.Count > 5 ? $"\n… and {readOnlys.Count - 5} more" : "");
            string roMsg = readOnlys.Count == 1
                ? $"The item is read-only:\n\n{list}\n\nDelete anyway?"
                : $"These items are read-only:\n\n{list}\n\nDelete all anyway?";

            var roResult = MessageBox.Show(
                roMsg,
                "Read-only",
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

        var paths = snapshot.Select(e => e.FullPath).ToList();
        IReadOnlyList<DeleteResult> results;
        try {
            results = await RunWithProgressDialogAsync(
                permanent ? "Удаление" : "В корзину",
                ct => _ops.DeleteManyAsync(paths, permanent, ct));
        } catch (OperationCanceledException) {
            Status = "Operation cancelled.";
            return;
        } catch (Exception ex) {
            _log.Error($"Delete batch failed", ex);
            Status = $"Delete failed: {ex.Message}";
            return;
        }

        Refresh();

        int ok = results.Count(r => r.Status == DeleteStatus.Ok);
        int failed = results.Count(r => r.Status == DeleteStatus.Failed);
        if (failed > 0) {
            var firstFail = results.First(r => r.Status == DeleteStatus.Failed);
            string detail = firstFail.Error is null ? "" : ": " + DescribeError(firstFail.Error, firstFail.Path);
            Status = $"{(permanent ? "Deleted" : "Recycled")} {ok}, {failed} failed{detail}";
        } else {
            Status = $"{(permanent ? "Deleted" : "Recycled")} {ok} item(s)";
        }
    }

    private void UndoLast() {
        try {
            var action = _undo.Undo();
            if (action is null) {
                return;
            }
            _log.Info($"Undo: {action.Description}");
            Status = $"Undone: {action.Description}";
            Refresh();
        } catch (Exception ex) {
            _log.Error("Undo failed", ex);
            Status = $"Undo failed: {ex.Message}";
        }
    }

    private void Rename(string? newName) {
        if (_selectedEntry is null || string.IsNullOrWhiteSpace(newName)) {
            return;
        }
        if (newName == _selectedEntry.Name) {
            return;
        }

        try {
            _ops.Rename(_selectedEntry.FullPath, newName);
            Refresh();
        } catch (Exception ex) {
            _log.Error($"Rename failed: {_selectedEntry.FullPath} -> {newName}", ex);
            Status = $"Rename failed: {DescribeError(ex, _selectedEntry.FullPath)}";
        }
    }

    private void Copy() {
        if (_selectedEntries.Count == 0) {
            return;
        }
        _clipboard = _selectedEntries.Select(e => e.FullPath).ToList();
        _clipboardIsCut = false;
        Status = $"Copied {_clipboard.Count} item(s)";
    }

    private void Cut() {
        if (_selectedEntries.Count == 0) {
            return;
        }
        _clipboard = _selectedEntries.Select(e => e.FullPath).ToList();
        _clipboardIsCut = true;
        Status = $"Cut {_clipboard.Count} item(s)";
    }

    private async Task PasteAsync() {
        if (_clipboard.Count == 0 || _nav.Current is null) {
            return;
        }

        string target = _nav.Current;
        var sources = _clipboard.ToList();

        var reason = PathSafety.DetectSelfDrop(sources, target, out string? offender);
        if (reason == SelfDropReason.IntoOwnDescendant || reason == SelfDropReason.Same) {
            string text = PathSafety.FormatReason(reason, offender, target);
            MessageBox.Show(text, "Cannot paste", MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = text;
            return;
        }

        bool wasCut = _clipboardIsCut;
        if (wasCut && !ConfirmMove(sources, target)) {
            return;
        }

        _log.Info($"Paste: {(wasCut ? "move" : "copy")} {sources.Count} item(s) into {target}");
        var resolver = new DispatcherConflictResolver(new InteractiveConflictResolver());
        IReadOnlyList<BatchItemResult> results;
        try {
            results = await RunWithProgressDialogAsync(
                wasCut ? "Перемещение" : "Копирование",
                ct => wasCut
                    ? _ops.MoveManyAsync(sources, target, resolver, ct)
                    : _ops.CopyManyAsync(sources, target, resolver, ct));
        } catch (OperationCanceledException) {
            Status = "Operation cancelled.";
            return;
        } catch (Exception ex) {
            _log.Error($"Paste failed into {target}", ex);
            Status = $"Paste failed: {ex.Message}";
            return;
        }

        if (wasCut) {
            _clipboard.Clear();
        }
        Refresh();
        ReportBatchResults(results, wasCut ? "Moved" : "Copied", target);
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
            Status = "Operation cancelled.";
            return;
        }

        var parts = new List<string> { $"{verb} {ok} item(s) to {target}" };
        if (skipped > 0) {
            parts.Add($"skipped {skipped}");
        }
        if (cancelled > 0) {
            parts.Add($"cancelled {cancelled}");
        }
        if (failed > 0) {
            var firstFail = results.First(r => r.Status == BatchItemStatus.Failed);
            string detail = firstFail.Error is null ? "" : ": " + DescribeError(firstFail.Error, firstFail.Source);
            parts.Add($"{failed} failed{detail}");
        }
        Status = string.Join(", ", parts);
    }

    private void NewFolder() {
        if (_nav.Current is null) {
            return;
        }

        string baseName = "New folder";
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
            Status = $"Create failed: {ex.Message}";
        }
    }

    private string DescribeError(Exception ex, string path) {
        if (ex is IOException && _lockInspector is not null) {
            var lockers = _lockInspector.WhoIsLocking(path);
            if (lockers.Count > 0) {
                string procs = string.Join(", ", lockers.Select(l => $"{l.ProcessName} (PID {l.ProcessId})"));
                return $"file is open in: {procs}";
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
            message = $"Move this entry?\n\nFrom: {sources[0]}\nTo:   {Path.Combine(target, Path.GetFileName(sources[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))}";
        } else {
            message = $"Move {sources.Count} items to:\n{target}?";
        }

        var result = MessageBox.Show(
            message,
            "Confirm move",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        return result == MessageBoxResult.OK;
    }
}
