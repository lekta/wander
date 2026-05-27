using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wander.App.Conflict;
using Wander.App.Util;
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
// Disambiguate from System.Windows.Media.ImageMetadata.
using ImageMetadata = Wander.Core.Icons.ImageMetadata;

namespace Wander.App.ViewModels;

public sealed class MainViewModel : ObservableObject {
    private readonly IFileSystem _fs;
    private readonly IShellLauncher _shell;
    private readonly IAppStateStore _stateStore;
    private readonly IFileLockInspector? _lockInspector;
    private readonly IImageMetadataReader? _metadataReader;
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
    private PreviewKind _previewKind = PreviewKind.None;
    private bool _isPreviewLoading;
    private string? _previewText;
    private ImageSource? _previewImage;
    private string? _previewCodeText;
    private string? _previewCodeExtension;
    private Uri? _previewWebUri;
    private string? _previewWebHtml;
    private ImageMetadata? _previewImageMetadata;
    private string _previewSummary = "";

    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _summaryCts;

    private List<string> _clipboard = new();
    private bool _clipboardIsCut;

    private bool _restoring;


    public MainViewModel() {
        _fs = ServiceLocator.Get<IFileSystem>();
        _shell = ServiceLocator.Get<IShellLauncher>();
        _stateStore = ServiceLocator.Get<IAppStateStore>();
        _lockInspector = ServiceLocator.IsRegistered<IFileLockInspector>()
            ? ServiceLocator.Get<IFileLockInspector>()
            : null;
        _metadataReader = ServiceLocator.IsRegistered<IImageMetadataReader>()
            ? ServiceLocator.Get<IImageMetadataReader>()
            : null;
        _ops = ServiceLocator.Get<FileOperationService>();
        _undo = ServiceLocator.Get<UndoService>();
        _tracker = ServiceLocator.Get<OperationTracker>();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _log = ServiceLocator.IsRegistered<ILogger>() ? ServiceLocator.Get<ILogger>() : NullLogger.Instance;

        Entries = new ObservableCollection<FileSystemEntry>();
        Roots = new ObservableCollection<TreeNodeViewModel>();
        Operations = new ObservableCollection<OperationViewModel>();

        _tracker.Changed += OnTrackerChanged;

        BackCommand = new RelayCommand(_ => GoBack(), _ => _nav.CanGoBack);
        ForwardCommand = new RelayCommand(_ => GoForward(), _ => _nav.CanGoForward);
        UpCommand = new RelayCommand(_ => GoUp(), _ => _nav.CanGoUp);
        NavigateCommand = new RelayCommand(_ => NavigateToAddress());
        OpenCommand = new RelayCommand(p => OpenEntry(p as FileSystemEntry ?? _selectedEntry), _ => _selectedEntry is not null);
        DeleteCommand = new RelayCommand(_ => _ = DeleteSelectedAsync(permanent: false), _ => _selectedEntries.Count > 0);
        RenameCommand = new RelayCommand(p => Rename(p as string), _ => _selectedEntry is not null);
        CopyCommand = new RelayCommand(_ => Copy(), _ => _selectedEntries.Count > 0);
        CutCommand = new RelayCommand(_ => Cut(), _ => _selectedEntries.Count > 0);
        PasteCommand = new RelayCommand(_ => _ = PasteAsync(), _ => _clipboard.Count > 0 && _nav.Current is not null);
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => _nav.Current is not null);
        RefreshCommand = new RelayCommand(_ => Refresh());
        SetViewModeCommand = new RelayCommand(p => SetViewMode(p as string));
        ExitCommand = new RelayCommand(_ => Application.Current?.Shutdown());
        OptionsCommand = new RelayCommand(_ => Status = "Options dialog is not implemented yet.");
        PropertiesCommand = new RelayCommand(_ => ShowProperties(), _ => _selectedEntry is not null);
        TogglePreviewCommand = new RelayCommand(_ => IsPreviewVisible = !IsPreviewVisible);
        UndoCommand = new RelayCommand(_ => UndoLast(), _ => _undo.CanUndo);
        PermanentDeleteCommand = new RelayCommand(_ => _ = DeleteSelectedAsync(permanent: true), _ => _selectedEntries.Count > 0);
        OpenLogFileCommand = new RelayCommand(_ => OpenLogFile(), _ => ServiceLocator.IsRegistered<ILogFile>());

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
    public ObservableCollection<OperationViewModel> Operations { get; }

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
                SchedulePreviewUpdate();
                ScheduleSummaryUpdate();
            }
        }
    }

    public IReadOnlyList<FileSystemEntry> SelectedEntries {
        get => _selectedEntries;
        set {
            if (SetField(ref _selectedEntries, value)) {
                ScheduleSummaryUpdate();
            }
        }
    }

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
                SchedulePreviewUpdate();
                ScheduleSummaryUpdate();
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

    public PreviewKind PreviewKind {
        get => _previewKind;
        private set {
            if (SetField(ref _previewKind, value)) {
                Raise(nameof(IsPreviewPlaceholderVisible));
                Raise(nameof(PreviewPlaceholderText));
            }
        }
    }

    public bool IsPreviewLoading {
        get => _isPreviewLoading;
        private set => SetField(ref _isPreviewLoading, value);
    }

    public string? PreviewText {
        get => _previewText;
        private set => SetField(ref _previewText, value);
    }

    public ImageSource? PreviewImage {
        get => _previewImage;
        private set => SetField(ref _previewImage, value);
    }

    public string? PreviewCodeText {
        get => _previewCodeText;
        private set => SetField(ref _previewCodeText, value);
    }

    public string? PreviewCodeExtension {
        get => _previewCodeExtension;
        private set => SetField(ref _previewCodeExtension, value);
    }

    public Uri? PreviewWebUri {
        get => _previewWebUri;
        private set => SetField(ref _previewWebUri, value);
    }

    public string? PreviewWebHtml {
        get => _previewWebHtml;
        private set => SetField(ref _previewWebHtml, value);
    }

    public ImageMetadata? PreviewImageMetadata {
        get => _previewImageMetadata;
        private set => SetField(ref _previewImageMetadata, value);
    }

    public string PreviewSummary {
        get => _previewSummary;
        private set => SetField(ref _previewSummary, value);
    }

    public bool IsPreviewPlaceholderVisible =>
        _isPreviewVisible && (_previewKind == PreviewKind.None || _previewKind == PreviewKind.Unsupported);

    public string PreviewPlaceholderText =>
        _previewKind == PreviewKind.None ? "Select a file to preview" : "No preview available";

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

    public string UndoTooltip => _undo.NextDescription is { } next ? $"Undo: {next}" : "Nothing to undo";

    public string WindowTitle {
        get {
            if (string.IsNullOrEmpty(_nav.Current)) {
                return "Wander";
            }
            string trimmed = _nav.Current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? _nav.Current : name;
        }
    }


    public void NavigateTo(string path) {
        if (!_fs.DirectoryExists(path)) {
            _log.Warn($"Navigate: path not found {path}");
            Status = $"Path not found: {path}";
            return;
        }
        _log.Info($"Navigate: {path}");
        _nav.NavigateTo(path);
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

            try {
                _shell.Open(entry.FullPath);
            } catch (Exception ex) {
                Status = $"Open failed: {ex.Message}";
            }
            return;
        }

        NavigateTo(entry.FullPath);
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
        NavigateTo(target);
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
        IReadOnlyList<FileOperationService.BatchItemResult> results;
        try {
            results = effect == DropEffect.Move
                ? await _ops.MoveManyAsync(sourcePaths, targetFolder, resolver)
                : await _ops.CopyManyAsync(sourcePaths, targetFolder, resolver);
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
            if (!string.IsNullOrEmpty(state.ViewMode) && Enum.TryParse<ViewMode>(state.ViewMode, out var mode)) {
                _viewMode = mode;
                Raise(nameof(ViewMode));
            }

            _isPreviewVisible = state.IsPreviewVisible;
            Raise(nameof(IsPreviewVisible));
            if (state.PreviewWidth >= 120 && state.PreviewWidth <= 900) {
                _previewWidth = state.PreviewWidth;
                Raise(nameof(PreviewWidth));
            }

            foreach (string path in state.ExpandedPaths) {
                ExpandToPath(path, select: false);
            }

            if (!string.IsNullOrEmpty(state.LastPath) && _fs.DirectoryExists(state.LastPath)) {
                _nav.NavigateTo(state.LastPath);
            } else {
                string? first = Roots.FirstOrDefault()?.FullPath;
                if (first is not null) {
                    _nav.NavigateTo(first);
                }
            }
        } finally {
            _restoring = false;
        }
    }

    private void SaveState() {
        if (_restoring) {
            return;
        }

        // Read-modify-write: AppState now also carries window geometry, which
        // the View persists separately. If we replaced the whole record here
        // we'd silently wipe those fields on every navigation/preview toggle.
        var current = _stateStore.Load();
        _stateStore.Save(current with {
            LastPath = _nav.Current,
            ViewMode = _viewMode.ToString(),
            ExpandedPaths = CollectExpanded(),
            IsPreviewVisible = _isPreviewVisible,
            PreviewWidth = _previewWidth,
        });
    }

    private List<string> CollectExpanded() {
        var result = new List<string>();
        foreach (var root in Roots) {
            CollectExpandedRecursive(root, result);
        }
        return result;
    }

    private static void CollectExpandedRecursive(TreeNodeViewModel node, List<string> result) {
        if (node.IsExpanded && !string.IsNullOrEmpty(node.FullPath)) {
            result.Add(node.FullPath);
        }
        foreach (var child in node.Children) {
            CollectExpandedRecursive(child, result);
        }
    }

    private void ExpandToPath(string path, bool select) {
        foreach (var root in Roots) {
            if (root.TryExpandToPath(path, select)) {
                return;
            }
        }
    }


    // --- Navigation glue -----------------------------------------------

    private void NavigateToAddress() {
        if (string.IsNullOrWhiteSpace(AddressText)) {
            return;
        }
        NavigateTo(AddressText.Trim());
    }

    private void GoBack() => _nav.GoBack();
    private void GoForward() => _nav.GoForward();
    private void GoUp() => _nav.GoUp();

    private void OnNavigationChanged() {
        AddressText = _nav.Current ?? "";
        Raise(nameof(WindowTitle));
        Raise(nameof(CurrentPath));
        Refresh();
        ExpandTreeToCurrent();
        SaveState();
        ScheduleSummaryUpdate();
    }

    private void ExpandTreeToCurrent() {
        if (_nav.Current is null) {
            return;
        }
        ExpandToPath(_nav.Current, select: true);
    }

    private void Refresh() {
        Entries.Clear();
        if (_nav.Current is null) {
            return;
        }

        try {
            foreach (var e in _fs.Enumerate(_nav.Current)) {
                Entries.Add(e);
            }
            Status = $"{Entries.Count} items";
        } catch (Exception ex) {
            Status = $"Error: {ex.Message}";
        }
    }

    private void LoadRoots() {
        Roots.Clear();
        foreach (var root in _fs.GetRoots()) {
            bool hasChildren = _fs.HasSubdirectories(root.FullPath);
            var node = new TreeNodeViewModel(root.Name, root.FullPath, EntryKind.Drive, _fs, hasChildren);
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


    // --- Preview content -----------------------------------------------

    private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff",
        // RAW (may not render but we still try; metadata works regardless):
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
    };

    private static readonly HashSet<string> _textExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".txt", ".log", ".csv", ".tsv",
        ".ini", ".cfg", ".conf", ".toml", ".env", ".gitignore", ".gitattributes",
        ".editorconfig",
    };

    private static readonly HashSet<string> _codeExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".cs", ".csproj", ".props", ".targets", ".sln", ".slnx",
        ".js", ".ts", ".jsx", ".tsx", ".mjs", ".cjs",
        ".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift", ".php",
        ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".m", ".mm",
        ".css", ".scss", ".less",
        ".sh", ".ps1", ".bat", ".cmd",
        ".sql",
        ".xml", ".xaml", ".svg",
        ".json", ".yaml", ".yml",
    };

    private const long PreviewMaxFileSize = 1_048_576;     // 1 MB
    private const int PreviewMaxChars = 200_000;


    private void SchedulePreviewUpdate() {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        _ = UpdatePreviewAsync(_previewCts.Token);
    }

    private async Task UpdatePreviewAsync(CancellationToken ct) {
        ClearPreviewContent();

        if (!_isPreviewVisible || _selectedEntry is null || _selectedEntry.Kind != EntryKind.File) {
            PreviewKind = PreviewKind.None;
            IsPreviewLoading = false;
            return;
        }

        IsPreviewLoading = true;
        try {
            string path = _selectedEntry.FullPath;
            string ext = Path.GetExtension(path);

            if (_imageExtensions.Contains(ext)) {
                await LoadImageAsync(path, ct);
                return;
            }

            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".htm", StringComparison.OrdinalIgnoreCase)) {
                PreviewWebUri = new Uri(path);
                PreviewKind = PreviewKind.Web;
                return;
            }

            if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)) {
                await LoadMarkdownAsync(path, ct);
                return;
            }

            if (_codeExtensions.Contains(ext)) {
                await LoadCodeAsync(path, ext, ct);
                return;
            }

            if (_textExtensions.Contains(ext) || string.IsNullOrEmpty(ext)) {
                await LoadTextAsync(path, ct);
                return;
            }

            PreviewKind = PreviewKind.Unsupported;
        } catch (OperationCanceledException) {
            // newer selection won — ignore
        } finally {
            if (!ct.IsCancellationRequested) {
                IsPreviewLoading = false;
                ScheduleSummaryUpdate();  // metadata might have arrived
            }
        }
    }

    private async Task LoadImageAsync(string path, CancellationToken ct) {
        BitmapImage? image = null;
        ImageMetadata? meta = null;

        await Task.Run(() => {
            ct.ThrowIfCancellationRequested();
            try {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                image = bi;
            } catch {
                // RAW or unsupported codec — image stays null, metadata may still load.
            }

            if (_metadataReader is not null) {
                meta = _metadataReader.Read(path);
            }
        }, ct);

        if (ct.IsCancellationRequested) {
            return;
        }

        PreviewImageMetadata = meta;
        if (image is not null) {
            PreviewImage = image;
            PreviewKind = PreviewKind.Image;
        } else {
            PreviewKind = PreviewKind.Unsupported;
        }
    }

    private async Task LoadTextAsync(string path, CancellationToken ct) {
        if ((_selectedEntry?.Size ?? 0) > PreviewMaxFileSize) {
            PreviewKind = PreviewKind.Unsupported;
            return;
        }

        string text;
        try {
            text = await File.ReadAllTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        } catch {
            PreviewKind = PreviewKind.Unsupported;
            return;
        }

        if (ct.IsCancellationRequested) {
            return;
        }

        if (text.Length > PreviewMaxChars) {
            text = text.Substring(0, PreviewMaxChars) + "\n\n… (truncated)";
        }
        PreviewText = text;
        PreviewKind = PreviewKind.Text;
    }

    private async Task LoadCodeAsync(string path, string ext, CancellationToken ct) {
        if ((_selectedEntry?.Size ?? 0) > PreviewMaxFileSize) {
            PreviewKind = PreviewKind.Unsupported;
            return;
        }

        string text;
        try {
            text = await File.ReadAllTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        } catch {
            PreviewKind = PreviewKind.Unsupported;
            return;
        }

        if (ct.IsCancellationRequested) {
            return;
        }

        if (text.Length > PreviewMaxChars) {
            text = text.Substring(0, PreviewMaxChars) + "\n\n// … (truncated)";
        }
        PreviewCodeText = text;
        PreviewCodeExtension = ext;
        PreviewKind = PreviewKind.Code;
    }

    private async Task LoadMarkdownAsync(string path, CancellationToken ct) {
        if ((_selectedEntry?.Size ?? 0) > PreviewMaxFileSize) {
            PreviewKind = PreviewKind.Unsupported;
            return;
        }

        string md;
        try {
            md = await File.ReadAllTextAsync(path, ct);
        } catch (OperationCanceledException) {
            return;
        } catch {
            PreviewKind = PreviewKind.Unsupported;
            return;
        }

        if (ct.IsCancellationRequested) {
            return;
        }

        string html = await Task.Run(() => Markdig.Markdown.ToHtml(md), ct);
        string wrapped = WrapHtml(html);
        PreviewWebHtml = wrapped;
        PreviewKind = PreviewKind.Web;
    }

    private static string WrapHtml(string body) {
        return $@"<!doctype html><html><head><meta charset='utf-8'><style>
            body {{ font-family: 'Segoe UI', sans-serif; font-size: 13px; padding: 10px; color: #222; }}
            pre, code {{ font-family: Consolas, monospace; background: #f4f4f4; padding: 2px 4px; border-radius: 3px; }}
            pre {{ padding: 8px; overflow-x: auto; }}
            h1, h2, h3 {{ margin: 0.6em 0 0.3em; }}
            blockquote {{ border-left: 3px solid #ccc; margin: 0; padding-left: 10px; color: #555; }}
            table {{ border-collapse: collapse; }}
            th, td {{ border: 1px solid #ccc; padding: 4px 8px; }}
            img {{ max-width: 100%; }}
        </style></head><body>{body}</body></html>";
    }

    private void ClearPreviewContent() {
        PreviewText = null;
        PreviewImage = null;
        PreviewCodeText = null;
        PreviewCodeExtension = null;
        PreviewWebUri = null;
        PreviewWebHtml = null;
        PreviewImageMetadata = null;
    }


    // --- Preview footer summary ----------------------------------------

    private void ScheduleSummaryUpdate() {
        _summaryCts?.Cancel();
        _summaryCts = new CancellationTokenSource();
        _ = UpdateSummaryAsync(_summaryCts.Token);
    }

    private async Task UpdateSummaryAsync(CancellationToken ct) {
        if (!_isPreviewVisible) {
            PreviewSummary = "";
            return;
        }

        // 1. Single file selected — show file details + EXIF if image.
        if (_selectedEntries.Count == 1 && _selectedEntries[0].Kind == EntryKind.File) {
            var e = _selectedEntries[0];
            string summary = $"📄  {e.Name}\nSize: {SizeFormatter.Format(e.Size)}   •   Modified: {FormatModified(e.ModifiedUtc)}";
            if (_previewImageMetadata is { } m) {
                summary += "\n" + FormatExif(m);
            }
            PreviewSummary = summary;
            return;
        }

        // 2. Single folder selected — recursive count + size, async.
        if (_selectedEntries.Count == 1 && _selectedEntries[0].Kind == EntryKind.Directory) {
            var e = _selectedEntries[0];
            PreviewSummary = $"📁  {e.Name} — calculating…";
            var (count, size) = await Task.Run(() => CountAndSum(new[] { e.FullPath }, ct), ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            PreviewSummary = $"📁  {e.Name} — {count} files, {SizeFormatter.Format(size)}";
            return;
        }

        // 3. Multiple items selected.
        if (_selectedEntries.Count > 1) {
            PreviewSummary = $"{_selectedEntries.Count} items selected — calculating…";
            var paths = _selectedEntries.Select(en => en.FullPath).ToArray();
            var (count, size) = await Task.Run(() => CountAndSum(paths, ct), ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            PreviewSummary = $"{_selectedEntries.Count} items selected — {count} files inside, {SizeFormatter.Format(size)}";
            return;
        }

        // 4. Nothing selected — summary of current folder.
        if (!string.IsNullOrEmpty(_nav.Current)) {
            string name = WindowTitle;
            PreviewSummary = $"📁  {name} — calculating…";
            string cur = _nav.Current;
            var (count, size) = await Task.Run(() => CountAndSum(new[] { cur }, ct), ct);
            if (ct.IsCancellationRequested) {
                return;
            }
            PreviewSummary = $"📁  {name} — {count} files, {SizeFormatter.Format(size)}";
            return;
        }

        PreviewSummary = "";
    }

    private static (int Count, long Size) CountAndSum(string[] paths, CancellationToken ct) {
        int count = 0;
        long size = 0;
        foreach (var p in paths) {
            if (ct.IsCancellationRequested) {
                break;
            }
            try {
                if (Directory.Exists(p)) {
                    foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)) {
                        if (ct.IsCancellationRequested) {
                            break;
                        }
                        count++;
                        try {
                            size += new FileInfo(f).Length;
                        } catch {
                            // access denied per-file — ignore
                        }
                    }
                } else if (File.Exists(p)) {
                    count++;
                    try {
                        size += new FileInfo(p).Length;
                    } catch {
                        // ignore
                    }
                }
            } catch {
                // access denied on enumeration — skip this root
            }
        }
        return (count, size);
    }

    private static string FormatModified(DateTime utc) {
        return utc == DateTime.MinValue ? "—" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string FormatExif(ImageMetadata m) {
        var parts = new List<string>();
        string? camera = string.Join(" ", new[] { m.CameraMake, m.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(camera)) {
            parts.Add(camera);
        }
        var shot = new List<string>();
        if (!string.IsNullOrEmpty(m.IsoSpeed)) {
            shot.Add($"ISO {m.IsoSpeed}");
        }
        if (!string.IsNullOrEmpty(m.Aperture)) {
            shot.Add(m.Aperture);
        }
        if (!string.IsNullOrEmpty(m.ShutterSpeed)) {
            shot.Add(m.ShutterSpeed);
        }
        if (!string.IsNullOrEmpty(m.FocalLength)) {
            shot.Add(m.FocalLength);
        }
        if (shot.Count > 0) {
            parts.Add(string.Join(", ", shot));
        }
        if (m.PixelWidth is int w && m.PixelHeight is int h) {
            parts.Add($"{w} × {h}");
        }
        if (m.DateTaken is { } dt) {
            parts.Add(dt.ToString("yyyy-MM-dd HH:mm"));
        }
        return string.Join("   •   ", parts);
    }


    // --- Destructive / clipboard ops (always confirm, Cancel-default) --

    private async Task DeleteSelectedAsync(bool permanent) {
        if (_selectedEntries.Count == 0) {
            return;
        }

        var snapshot = _selectedEntries.ToList();
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
        IReadOnlyList<FileOperationService.DeleteResult> results;
        try {
            results = await _ops.DeleteManyAsync(paths, permanent);
        } catch (Exception ex) {
            _log.Error($"Delete batch failed", ex);
            Status = $"Delete failed: {ex.Message}";
            return;
        }

        Refresh();

        int ok = results.Count(r => r.Status == FileOperationService.DeleteStatus.Ok);
        int failed = results.Count(r => r.Status == FileOperationService.DeleteStatus.Failed);
        if (failed > 0) {
            var firstFail = results.First(r => r.Status == FileOperationService.DeleteStatus.Failed);
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
        IReadOnlyList<FileOperationService.BatchItemResult> results;
        try {
            results = wasCut
                ? await _ops.MoveManyAsync(sources, target, resolver)
                : await _ops.CopyManyAsync(sources, target, resolver);
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

    private void ReportBatchResults(IReadOnlyList<FileOperationService.BatchItemResult> results, string verb, string target) {
        int ok = results.Count(r =>
            r.Status == FileOperationService.BatchItemStatus.Ok ||
            r.Status == FileOperationService.BatchItemStatus.Replaced ||
            r.Status == FileOperationService.BatchItemStatus.Renamed);
        int skipped = results.Count(r => r.Status == FileOperationService.BatchItemStatus.Skipped);
        int failed = results.Count(r => r.Status == FileOperationService.BatchItemStatus.Failed);
        int cancelled = results.Count(r => r.Status == FileOperationService.BatchItemStatus.Cancelled);

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
            var firstFail = results.First(r => r.Status == FileOperationService.BatchItemStatus.Failed);
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
