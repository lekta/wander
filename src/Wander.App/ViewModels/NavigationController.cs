using System.IO;
using Wander.Core.Navigation;

namespace Wander.App.ViewModels;

/// <summary>
/// View-model shell around <see cref="NavigationService"/>: owns the address
/// bar text, the Back/Forward/Up/Navigate commands, and the
/// <see cref="WindowTitle"/> derivation. Path validation and the side-effects
/// of a successful navigation (folder refresh, preview, save state, tree
/// expansion) stay on the host — the controller fires
/// <see cref="CurrentChanged"/> for that.
///
/// <para>
/// Two callbacks are injected at construction time:
/// </para>
/// <list type="bullet">
///   <item><description><c>canNavigate</c> — pre-flight check; if it returns
///   false, the controller calls <c>onInvalidPath</c> and skips the navigation.</description></item>
///   <item><description><c>resolveDisplayName</c> — supplies the user-facing
///   label for shell sentinels (e.g. <c>shell:RecycleBinFolder</c> → "Корзина")
///   when <see cref="Path.GetFileName"/> would return empty.</description></item>
/// </list>
/// </summary>
public sealed class NavigationController : ObservableObject {
    private readonly NavigationService _nav;
    private readonly Func<string, bool> _canNavigate;
    private readonly Action<string> _onInvalidPath;
    private readonly Func<string, string?> _resolveDisplayName;
    private readonly RecentPaths _recent = new();
    private string _addressText = "";
    private IReadOnlyList<string> _recentPaths = Array.Empty<string>();
    private IReadOnlyList<PathCrumb> _breadcrumbs = Array.Empty<PathCrumb>();
    private bool _isEditingAddress;


    public NavigationController(
        NavigationService nav,
        Func<string, bool> canNavigate,
        Action<string> onInvalidPath,
        Func<string, string?> resolveDisplayName) {
        _nav = nav;
        _canNavigate = canNavigate;
        _onInvalidPath = onInvalidPath;
        _resolveDisplayName = resolveDisplayName;

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
            if (_resolveDisplayName(_nav.Current) is { } shellName) {
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
    /// Runs the host's navigability check first; on failure delegates to
    /// the host's "invalid path" handler and returns without pushing
    /// history.
    /// </summary>
    public void NavigateTo(string path, NavigationSource source = NavigationSource.External) {
        if (!_canNavigate(path)) {
            _onInvalidPath(path);
            return;
        }
        _nav.NavigateTo(path, source);
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

        Breadcrumbs = _resolveDisplayName(current) is { } shellName
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
