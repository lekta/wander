using System.IO;
using Wander.App.Resources;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Navigation;
using Wander.Core.Shell;


namespace Wander.App.Controllers;

/// <summary>
/// View-model shell around <see cref="NavigationService"/>: owns the address
/// bar text, the Back/Forward/Up/Navigate commands, and the
/// <see cref="WindowTitle"/> derivation. The side-effects of a successful
/// navigation (folder refresh, preview, save state, tree expansion) stay on
/// the host — the controller fires <see cref="CurrentChanged"/> for that,
/// and <see cref="StatusReported"/> when a typed path turns out not to
/// exist. What it can answer itself it answers from its own services:
/// whether a path is navigable, and the user-facing label for shell
/// sentinels (e.g. <c>shell:RecycleBinFolder</c> → "Корзина") where
/// <see cref="Path.GetFileName"/> would return empty.
/// </summary>
public sealed class NavigationController : ObservableObject {
    private readonly NavigationService _nav;
    private readonly IFileSystem _fs;
    private readonly IShellNamespace? _shellNamespace;
    private readonly ILogger _log;
    private readonly RecentPaths _recent = new();
    private string _addressText = "";
    private IReadOnlyList<string> _recentPaths = Array.Empty<string>();
    private IReadOnlyList<PathCrumb> _breadcrumbs = Array.Empty<PathCrumb>();
    private bool _isEditingAddress;


    public NavigationController(
        NavigationService nav,
        IFileSystem fs,
        IShellNamespace? shellNamespace,
        ILogger log) {
        _nav = nav;
        _fs = fs;
        _shellNamespace = shellNamespace;
        _log = log;

        BackCommand = new RelayCommand(_ => _nav.GoBack(), _ => _nav.CanGoBack);
        ForwardCommand = new RelayCommand(_ => _nav.GoForward(), _ => _nav.CanGoForward);
        UpCommand = new RelayCommand(_ => _nav.GoUp(), _ => _nav.CanGoUp);
        NavigateCommand = new RelayCommand(_ => NavigateToAddress());
        NavigateToCrumbCommand = new RelayCommand(p => NavigateToCrumb(p as string));

        _nav.CurrentChanged += (_, _) => {
            AddressText = _nav.Current ?? "";
            if (_nav.Current is { } current) {
                _recent.Add(current);
                PublishRecentPaths();
            }
            RebuildBreadcrumbs();
            // A completed navigation always hands the strip back to its
            // breadcrumb face — the typed path has served its purpose.
            IsEditingAddress = false;
            Raise(nameof(Current));
            Raise(nameof(CurrentSource));
            Raise(nameof(WindowTitle));
            BackCommand.RaiseCanExecuteChanged();
            ForwardCommand.RaiseCanExecuteChanged();
            UpCommand.RaiseCanExecuteChanged();
            CurrentChanged?.Invoke(this, _nav.Current);
        };
    }


    /// <summary>Fired after <see cref="NavigationService.Current"/> changes.</summary>
    public event EventHandler<string?>? CurrentChanged;

    /// <summary>Something to tell the user — already localised.</summary>
    public event EventHandler<string>? StatusReported;


    public string? Current => _nav.Current;
    public NavigationSource? CurrentSource => _nav.CurrentSource;
    public bool CanGoBack => _nav.CanGoBack;
    public bool CanGoForward => _nav.CanGoForward;
    public bool CanGoUp => _nav.CanGoUp;

    /// <summary>Address-bar text. Two-way bound by XAML.</summary>
    public string AddressText {
        get => _addressText;
        set => SetField(ref _addressText, value);
    }

    /// <summary>
    /// Current path cut into clickable segments, root first. Shell
    /// sentinels stay a single crumb under their localised display name —
    /// "Корзина" has no parent folder to walk up to.
    /// </summary>
    public IReadOnlyList<PathCrumb> Breadcrumbs {
        get => _breadcrumbs;
        private set => SetField(ref _breadcrumbs, value);
    }

    /// <summary>
    /// Folders visited most recently, newest first. A fresh snapshot on
    /// every change, never the live list from <see cref="RecentPaths"/>:
    /// an ItemsControl bound to a plain list that mutates underneath it
    /// desyncs its container generator and then throws
    /// "ItemsControl is inconsistent with its items source" on the next
    /// layout pass. Handing out a new instance makes WPF rebind cleanly.
    /// </summary>
    public IReadOnlyList<string> RecentPaths {
        get => _recentPaths;
        private set => SetField(ref _recentPaths, value);
    }

    /// <summary>
    /// Which face of the address bar is showing: false = breadcrumb
    /// buttons (default), true = the raw path in an editable TextBox.
    /// The view flips it on Ctrl+L / a click on the strip's empty space,
    /// and back on Esc / focus loss.
    /// </summary>
    public bool IsEditingAddress {
        get => _isEditingAddress;
        set => SetField(ref _isEditingAddress, value);
    }

    /// <summary>
    /// Window-chrome title — leaf name of the current path, or the shell
    /// namespace's localised display name when the path is a shell sentinel.
    /// "Wander" on a fresh start.
    /// </summary>
    public string WindowTitle {
        get {
            if (string.IsNullOrEmpty(_nav.Current)) {
                return "Wander";
            }
            if (ResolveDisplayName(_nav.Current) is { } shellName) {
                return shellName;
            }
            string trimmed = _nav.Current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? _nav.Current : name;
        }
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand ForwardCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand NavigateCommand { get; }

    /// <summary>Click on a breadcrumb; parameter is the segment's full path.</summary>
    public RelayCommand NavigateToCrumbCommand { get; }


    /// <summary>
    /// Entry point used by every code path that wants to change folder
    /// (right-pane double-click, tree click, drop-target follow, restore).
    ///
    /// <para>
    /// No pre-flight disk check: probing the path here ran
    /// <c>DirectoryExists</c> on the UI thread, and on a sleeping drive or
    /// a pulled card reader that single call held the whole window for
    /// seconds — on every navigation, including ones whose paths came
    /// straight out of a listing that just succeeded. Navigation itself is
    /// bookkeeping and must be instant; whether the folder is actually
    /// there is answered by the async listing, whose failure path already
    /// shows the "folder is gone" panel. Back/Forward/Up have always
    /// worked exactly this way.
    /// </para>
    ///
    /// <para>
    /// The one entry that still gets validated is typed text — the address
    /// bar and its crumbs (<see cref="NavigationSource.Address"/>) — where
    /// a typo staying put with a status message is the better answer than
    /// walking into a "folder is gone" panel. That check runs off the UI
    /// thread; see <see cref="NavigateWhenValidAsync"/>.
    /// </para>
    /// </summary>
    public void NavigateTo(string path, NavigationSource source = NavigationSource.External) {
        if (source == NavigationSource.Address) {
            _ = NavigateWhenValidAsync(path, source);

            return;
        }

        _nav.NavigateTo(path, source);
    }


    /// <summary>
    /// The typed-path route: existence is checked on the pool, and the
    /// navigation happens only if the user has not gone somewhere else
    /// while the disk was answering — a slow "yes" from a spun-down drive
    /// must not yank them away from wherever they went meanwhile.
    /// </summary>
    private async Task NavigateWhenValidAsync(string path, NavigationSource source) {
        string? before = _nav.Current;
        bool ok = await Task.Run(() => PathIsNavigable(path));

        if (!string.Equals(_nav.Current, before, StringComparison.OrdinalIgnoreCase)) {
            return;
        }
        if (!ok) {
            _log.Warn($"Navigate: path not found {path}");
            StatusReported?.Invoke(this, string.Format(Strings.StatusPathNotFound, path));

            return;
        }

        _nav.NavigateTo(path, source);
    }


    // Runs on the pool (see NavigateWhenValidAsync), so it asks the cheap
    // IsShellPath and never GetDisplayName — no shell COM off the UI thread.
    private bool PathIsNavigable(string path) {
        return (_shellNamespace?.IsShellPath(path) ?? false) || _fs.DirectoryExists(path);
    }


    /// <summary>
    /// The shell namespace's localised label for a sentinel path, or null
    /// for an ordinary folder (and for every path when the host has no
    /// shell namespace registered).
    /// </summary>
    private string? ResolveDisplayName(string path) {
        if (_shellNamespace is { } ns && ns.IsShellPath(path)) {
            return ns.GetDisplayName(path) ?? path;
        }

        return null;
    }

    /// <summary>Restores the recent-folders list from <c>state.json</c>.</summary>
    public void LoadRecentPaths(IEnumerable<string> paths) {
        _recent.Load(paths);
        PublishRecentPaths();
    }

    private void PublishRecentPaths() {
        RecentPaths = _recent.Items.ToArray();
    }

    private void NavigateToCrumb(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }
        NavigateTo(path, NavigationSource.Address);
    }

    private void RebuildBreadcrumbs() {
        if (_nav.Current is not { } current) {
            Breadcrumbs = Array.Empty<PathCrumb>();
            return;
        }

        Breadcrumbs = ResolveDisplayName(current) is { } shellName
            ? new[] { new PathCrumb(shellName, current) }
            : PathCrumbs.Split(current);
    }

    private void NavigateToAddress() {
        if (string.IsNullOrWhiteSpace(AddressText)) {
            return;
        }

        string target = AddressText.Trim();
        NavigateTo(target, NavigationSource.Address);

        // Re-entering the folder we are already in produces no
        // CurrentChanged to ride back to breadcrumb mode on, so close the
        // edit here. A rejected path deliberately keeps the box open —
        // the user is one keystroke away from fixing the typo.
        if (string.Equals(target, Current, StringComparison.OrdinalIgnoreCase)) {
            IsEditingAddress = false;
        }
    }
}
