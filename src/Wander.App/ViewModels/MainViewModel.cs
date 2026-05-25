using System.Collections.ObjectModel;
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
    private ViewMode _viewMode = ViewMode.Details;

    private string? _clipboardSource;
    private bool _clipboardIsCut;


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
        DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => _selectedEntry is not null);
        RenameCommand = new RelayCommand(p => Rename(p as string), _ => _selectedEntry is not null);
        CopyCommand = new RelayCommand(_ => Copy(), _ => _selectedEntry is not null);
        CutCommand = new RelayCommand(_ => Cut(), _ => _selectedEntry is not null);
        PasteCommand = new RelayCommand(_ => Paste(), _ => _clipboardSource is not null && _nav.Current is not null);
        NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => _nav.Current is not null);
        RefreshCommand = new RelayCommand(_ => Refresh());
        ToggleViewModeCommand = new RelayCommand(_ => ToggleViewMode());

        _nav.CurrentChanged += (_, _) => OnNavigationChanged();

        LoadRoots();
        RestoreState();
    }


    public ObservableCollection<FileSystemEntry> Entries { get; }
    public ObservableCollection<TreeNodeViewModel> Roots { get; }

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
    public RelayCommand ToggleViewModeCommand { get; }


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


    // --- Startup state -------------------------------------------------

    private void RestoreState() {
        var state = _stateStore.Load();

        if (!string.IsNullOrEmpty(state.ViewMode) && Enum.TryParse<ViewMode>(state.ViewMode, out var mode)) {
            _viewMode = mode;
            Raise(nameof(ViewMode));
        }

        if (!string.IsNullOrEmpty(state.LastPath) && _fs.DirectoryExists(state.LastPath)) {
            _nav.NavigateTo(state.LastPath);
            return;
        }

        string? first = Roots.FirstOrDefault()?.FullPath;
        if (first is not null) {
            _nav.NavigateTo(first);
        }
    }

    private void SaveState() {
        _stateStore.Save(new AppState {
            LastPath = _nav.Current,
            ViewMode = _viewMode.ToString(),
        });
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
        Refresh();
        ExpandTreeToCurrent();
        SaveState();
    }

    private void ExpandTreeToCurrent() {
        if (_nav.Current is null) {
            return;
        }

        foreach (var root in Roots) {
            if (root.TryExpandToPath(_nav.Current)) {
                return;
            }
        }
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
            Roots.Add(new TreeNodeViewModel(root.Name, root.FullPath, EntryKind.Drive, _fs));
        }
    }


    // --- View modes ----------------------------------------------------

    private void ToggleViewMode() {
        ViewMode = _viewMode == ViewMode.Details ? ViewMode.LargeIcons : ViewMode.Details;
    }


    // --- Destructive operations (always confirm, Cancel-default) -------

    private void DeleteSelected() {
        if (_selectedEntry is null) {
            return;
        }

        var entry = _selectedEntry;
        string kind = entry.Kind == EntryKind.Directory ? "folder" : "file";
        string message = $"Delete {kind} '{entry.Name}'?\n\n{entry.FullPath}";

        var result = MessageBox.Show(
            message,
            "Confirm deletion",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (result != MessageBoxResult.OK) {
            return;
        }

        try {
            _ops.Delete(entry.FullPath);
            Refresh();
        } catch (Exception ex) {
            Status = $"Delete failed: {ex.Message}";
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
            Status = $"Rename failed: {ex.Message}";
        }
    }

    private void Copy() {
        if (_selectedEntry is null) {
            return;
        }

        _clipboardSource = _selectedEntry.FullPath;
        _clipboardIsCut = false;
        Status = $"Copied: {_selectedEntry.Name}";
    }

    private void Cut() {
        if (_selectedEntry is null) {
            return;
        }

        _clipboardSource = _selectedEntry.FullPath;
        _clipboardIsCut = true;
        Status = $"Cut: {_selectedEntry.Name}";
    }

    private void Paste() {
        if (_clipboardSource is null || _nav.Current is null) {
            return;
        }

        string name = Path.GetFileName(_clipboardSource);
        string target = Path.Combine(_nav.Current, name);

        if (_clipboardIsCut && !ConfirmMove(_clipboardSource, target)) {
            return;
        }

        try {
            if (_clipboardIsCut) {
                _ops.Move(_clipboardSource, target);
                _clipboardSource = null;
            } else {
                _ops.Copy(_clipboardSource, target);
            }
            Refresh();
        } catch (Exception ex) {
            Status = $"Paste failed: {ex.Message}";
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

    private static bool ConfirmMove(string source, string target) {
        string message = $"Move this entry?\n\nFrom: {source}\nTo: {target}";

        var result = MessageBox.Show(
            message,
            "Confirm move",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        return result == MessageBoxResult.OK;
    }
}
