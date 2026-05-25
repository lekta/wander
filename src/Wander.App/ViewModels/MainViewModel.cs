using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Navigation;
using Wander.Core.Persistence;
using Wander.Core.Shell;

namespace Wander.App.ViewModels;

public sealed class MainViewModel : ObservableObject {
    private readonly IFileSystem _fs;
    private readonly IShellLauncher _shell;
    private readonly IAppStateStore _stateStore;
    private readonly NavigationService _nav = new();
    private readonly FileOperationService _ops;

    private string _addressText = "";
    private string _status = "";
    private FileSystemEntry? _selectedEntry;
    private IReadOnlyList<FileSystemEntry> _selectedEntries = Array.Empty<FileSystemEntry>();
    private ViewMode _viewMode = ViewMode.Details;

    private List<string> _clipboard = new();
    private bool _clipboardIsCut;

    private bool _restoring;


    public MainViewModel() {
        _fs = ServiceLocator.Get<IFileSystem>();
        _shell = ServiceLocator.Get<IShellLauncher>();
        _stateStore = ServiceLocator.Get<IAppStateStore>();
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
        set => SetField(ref _selectedEntry, value);
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

        if (effect == DropEffect.Move && !ConfirmMove(sourcePaths, targetFolder)) {
            return;
        }

        int ok = 0;
        foreach (string src in sourcePaths) {
            string name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) {
                continue;
            }

            string dest = Path.Combine(targetFolder, name);
            if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            try {
                if (effect == DropEffect.Move) {
                    _ops.Move(src, dest);
                } else {
                    _ops.Copy(src, dest);
                }
                ok++;
            } catch (Exception ex) {
                Status = $"{(effect == DropEffect.Move ? "Move" : "Copy")} failed for {name}: {ex.Message}";
            }
        }

        Refresh();
        if (ok > 0) {
            Status = $"{(effect == DropEffect.Move ? "Moved" : "Copied")} {ok} item(s) to {targetFolder}";
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

        foreach (var entry in _selectedEntries.ToList()) {
            try {
                _ops.Delete(entry.FullPath);
            } catch (Exception ex) {
                Status = $"Delete failed for {entry.Name}: {ex.Message}";
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
            Status = $"Rename failed: {ex.Message}";
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

        if (_clipboardIsCut && !ConfirmMove(_clipboard, _nav.Current)) {
            return;
        }

        int ok = 0;
        foreach (string src in _clipboard) {
            string name = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string target = Path.Combine(_nav.Current, name);
            try {
                if (_clipboardIsCut) {
                    _ops.Move(src, target);
                } else {
                    _ops.Copy(src, target);
                }
                ok++;
            } catch (Exception ex) {
                Status = $"Paste failed for {name}: {ex.Message}";
            }
        }

        if (_clipboardIsCut) {
            _clipboard.Clear();
        }
        Refresh();
        if (ok > 0) {
            Status = $"{(_clipboardIsCut ? "Moved" : "Pasted")} {ok} item(s)";
        }
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
