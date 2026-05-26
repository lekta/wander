using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wander.App.Conflict;
using Wander.App.Util;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Navigation;
using Wander.Core.Persistence;
using Wander.Core.Shell;

namespace Wander.App.ViewModels;

public sealed class MainViewModel : ObservableObject {
    private readonly IFileSystem _fs;
    private readonly IShellLauncher _shell;
    private readonly IAppStateStore _stateStore;
    private readonly IFileLockInspector? _lockInspector;
    private readonly NavigationService _nav = new();
    private readonly FileOperationService _ops;

    private string _addressText = "";
    private string _status = "";
    private FileSystemEntry? _selectedEntry;
    private IReadOnlyList<FileSystemEntry> _selectedEntries = Array.Empty<FileSystemEntry>();
    private ViewMode _viewMode = ViewMode.Details;

    private bool _isPreviewVisible;
    private double _previewWidth = 280;
    private PreviewKind _previewKind = PreviewKind.None;
    private string? _previewText;
    private ImageSource? _previewImage;

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
        _ops = new FileOperationService(_fs);

        Entries = new ObservableCollection<FileSystemEntry>();
        Roots = new ObservableCollection<TreeNodeViewModel>();

        BackCommand = new RelayCommand(_ => GoBack(), _ => _nav.CanGoBack);
        ForwardCommand = new RelayCommand(_ => GoForward(), _ => _nav.CanGoForward);
        UpCommand = new RelayCommand(_ => GoUp(), _ => _nav.CanGoUp);
        NavigateCommand = new RelayCommand(_ => NavigateToAddress());
        OpenCommand = new RelayCommand(p => OpenEntry(p as FileSystemEntry ?? _selectedEntry), _ => _selectedEntry is not null);
        DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => _selectedEntries.Count > 0);
        RenameCommand = new RelayCommand(p => Rename(p as string), _ => _selectedEntry is not null);
        CopyCommand = new RelayCommand(_ => Copy(), _ => _selectedEntries.Count > 0);
        CutCommand = new RelayCommand(_ => Cut(), _ => _selectedEntries.Count > 0);
        PasteCommand = new RelayCommand(_ => Paste(), _ => _clipboard.Count > 0 && _nav.Current is not null);
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => _nav.Current is not null);
        RefreshCommand = new RelayCommand(_ => Refresh());
        SetViewModeCommand = new RelayCommand(p => SetViewMode(p as string));
        ExitCommand = new RelayCommand(_ => Application.Current?.Shutdown());
        OptionsCommand = new RelayCommand(_ => Status = "Options dialog is not implemented yet.");
        PropertiesCommand = new RelayCommand(_ => ShowProperties(), _ => _selectedEntry is not null);
        TogglePreviewCommand = new RelayCommand(_ => IsPreviewVisible = !IsPreviewVisible);

        _nav.CurrentChanged += (_, _) => OnNavigationChanged();

        LoadRoots();
        RestoreState();
    }


    public ObservableCollection<FileSystemEntry> Entries { get; }
    public ObservableCollection<TreeNodeViewModel> Roots { get; }

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
                UpdatePreview();
            }
        }
    }

    public IReadOnlyList<FileSystemEntry> SelectedEntries {
        get => _selectedEntries;
        set => SetField(ref _selectedEntries, value);
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
                UpdatePreview();
                SaveState();
            }
        }
    }

    public double PreviewWidth {
        get => _previewWidth;
        set {
            // Clamp to keep the pane usable.
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

    public string? PreviewText {
        get => _previewText;
        private set => SetField(ref _previewText, value);
    }

    public ImageSource? PreviewImage {
        get => _previewImage;
        private set => SetField(ref _previewImage, value);
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
            Status = $"Path not found: {path}";
            return;
        }

        _nav.NavigateTo(path);
    }

    public void OpenEntry(FileSystemEntry? entry) {
        if (entry is null) {
            return;
        }

        if (entry.Kind == EntryKind.File) {
            try {
                _shell.Open(entry.FullPath);
            } catch (Exception ex) {
                Status = $"Open failed: {ex.Message}";
            }
            return;
        }

        NavigateTo(entry.FullPath);
    }

    /// <summary>
    /// Called by the View when files are dropped into a list / tree / window.
    /// </summary>
    public void HandleDrop(IReadOnlyList<string> sourcePaths, string? targetFolder, DropEffect effect) {
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

        var resolver = new InteractiveConflictResolver();
        IReadOnlyList<FileOperationService.BatchItemResult> results;
        try {
            results = effect == DropEffect.Move
                ? _ops.MoveMany(sourcePaths, targetFolder, resolver)
                : _ops.CopyMany(sourcePaths, targetFolder, resolver);
        } catch (Exception ex) {
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

        _stateStore.Save(new AppState {
            LastPath = _nav.Current,
            ViewMode = _viewMode.ToString(),
            ExpandedPaths = CollectExpanded(),
            IsPreviewVisible = _isPreviewVisible,
            PreviewWidth = _previewWidth,
        });
    }


    // --- Preview -------------------------------------------------------

    private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff",
    };

    private static readonly HashSet<string> _textExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".txt", ".md", ".markdown", ".log", ".csv", ".tsv",
        ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf",
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".editorconfig",
        ".js", ".ts", ".jsx", ".tsx", ".html", ".htm", ".css", ".scss", ".less",
        ".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift", ".php",
        ".c", ".cpp", ".h", ".hpp", ".m", ".mm",
        ".sh", ".ps1", ".bat", ".cmd",
        ".gitignore", ".gitattributes",
    };

    // Read at most 1 MB from disk, render at most ~200 KB of text.
    private const long PreviewMaxFileSize = 1_048_576;
    private const int PreviewMaxChars = 200_000;


    private void UpdatePreview() {
        if (!_isPreviewVisible) {
            SetPreview(PreviewKind.None, null, null);
            return;
        }

        if (_selectedEntry is null || _selectedEntry.Kind != EntryKind.File) {
            SetPreview(PreviewKind.None, null, null);
            return;
        }

        string path = _selectedEntry.FullPath;
        string ext = Path.GetExtension(path);

        if (_imageExtensions.Contains(ext)) {
            TryLoadImage(path);
            return;
        }

        if (_textExtensions.Contains(ext) || string.IsNullOrEmpty(ext)) {
            if ((_selectedEntry.Size ?? 0) > PreviewMaxFileSize) {
                SetPreview(PreviewKind.Unsupported, null, null);
                return;
            }
            TryLoadText(path);
            return;
        }

        SetPreview(PreviewKind.Unsupported, null, null);
    }

    private void TryLoadImage(string path) {
        try {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            SetPreview(PreviewKind.Image, null, bitmap);
        } catch {
            SetPreview(PreviewKind.Unsupported, null, null);
        }
    }

    private void TryLoadText(string path) {
        try {
            string text = File.ReadAllText(path);
            if (text.Length > PreviewMaxChars) {
                text = text.Substring(0, PreviewMaxChars) + "\n\n… (truncated)";
            }
            SetPreview(PreviewKind.Text, text, null);
        } catch {
            SetPreview(PreviewKind.Unsupported, null, null);
        }
    }

    private void SetPreview(PreviewKind kind, string? text, ImageSource? image) {
        PreviewText = text;
        PreviewImage = image;
        PreviewKind = kind;
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

    private void GoBack() {
        _nav.GoBack();
    }

    private void GoForward() {
        _nav.GoForward();
    }

    private void GoUp() {
        _nav.GoUp();
    }

    private void OnNavigationChanged() {
        AddressText = _nav.Current ?? "";
        Raise(nameof(WindowTitle));
        Raise(nameof(CurrentPath));
        Refresh();
        ExpandTreeToCurrent();
        SaveState();
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

    private void DeleteSelected() {
        if (_selectedEntries.Count == 0) {
            return;
        }

        string message;
        if (_selectedEntries.Count == 1) {
            var e0 = _selectedEntries[0];
            string kind = e0.Kind == EntryKind.Directory ? "folder" : "file";
            message = $"Delete {kind} '{e0.Name}'?\n\n{e0.FullPath}";
        } else {
            message = $"Delete {_selectedEntries.Count} items?\n\n" +
                string.Join("\n", _selectedEntries.Take(5).Select(e => "• " + e.Name)) +
                (_selectedEntries.Count > 5 ? $"\n… and {_selectedEntries.Count - 5} more" : "");
        }

        var result = MessageBox.Show(
            message,
            "Confirm deletion",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (result != MessageBoxResult.OK) {
            return;
        }

        // Read-only second-stage confirmation: list affected items explicitly.
        var readOnlys = _selectedEntries.Where(en => en.IsReadOnly).ToList();
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
        }

        foreach (var entry in _selectedEntries.ToList()) {
            try {
                if (entry.IsReadOnly) {
                    _fs.ClearReadOnly(entry.FullPath);
                }
                _ops.Delete(entry.FullPath);
            } catch (Exception ex) {
                Status = $"Delete failed for {entry.Name}: {DescribeError(ex, entry.FullPath)}";
            }
        }
        Refresh();
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

    private void Paste() {
        if (_clipboard.Count == 0 || _nav.Current is null) {
            return;
        }

        // Self-drop protection (paste-into-self / into-own-descendant).
        var reason = PathSafety.DetectSelfDrop(_clipboard, _nav.Current, out string? offender);
        if (reason == SelfDropReason.IntoOwnDescendant || reason == SelfDropReason.Same) {
            string text = PathSafety.FormatReason(reason, offender, _nav.Current);
            MessageBox.Show(text, "Cannot paste", MessageBoxButton.OK, MessageBoxImage.Warning);
            Status = text;
            return;
        }

        if (_clipboardIsCut && !ConfirmMove(_clipboard, _nav.Current)) {
            return;
        }

        bool wasCut = _clipboardIsCut;
        var resolver = new InteractiveConflictResolver();
        IReadOnlyList<FileOperationService.BatchItemResult> results;
        try {
            results = wasCut
                ? _ops.MoveMany(_clipboard, _nav.Current, resolver)
                : _ops.CopyMany(_clipboard, _nav.Current, resolver);
        } catch (Exception ex) {
            Status = $"Paste failed: {ex.Message}";
            return;
        }

        if (wasCut) {
            _clipboard.Clear();
        }
        Refresh();
        ReportBatchResults(results, wasCut ? "Moved" : "Copied", _nav.Current);
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
            Status = $"Create failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Turn an IOException into a human-readable string that includes which
    /// process holds the file when we can determine it via RestartManager.
    /// For directory operations we only get the wrapping IOException; we still
    /// try the path itself in case it's actually a file.
    /// </summary>
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
