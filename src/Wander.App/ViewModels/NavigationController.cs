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
    private string _addressText = "";


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

        _nav.CurrentChanged += (_, _) => {
            AddressText = _nav.Current ?? "";
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

    private void NavigateToAddress() {
        if (string.IsNullOrWhiteSpace(AddressText)) {
            return;
        }
        NavigateTo(AddressText.Trim(), NavigationSource.Address);
    }
}
