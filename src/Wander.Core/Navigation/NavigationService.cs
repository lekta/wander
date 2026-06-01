namespace Wander.Core.Navigation;

/// <summary>
/// Browser-style back/forward navigation. Each entry carries a
/// <see cref="NavigationSource"/> so consumers (the tree expander, the
/// preview pane, …) can react differently based on how the user got there
/// — see <see cref="CurrentSource"/>.
/// </summary>
public sealed class NavigationService {
    private readonly List<NavigationEntry> _history = new();
    private int _cursor = -1;


    public string? Current => _cursor >= 0 ? _history[_cursor].Path : null;

    /// <summary>
    /// Source of the current entry. Null only when nothing has been
    /// navigated to yet. Updated on every <see cref="NavigateTo"/>,
    /// <see cref="GoBack"/>, <see cref="GoForward"/>, <see cref="GoUp"/>.
    /// </summary>
    public NavigationSource? CurrentSource => _cursor >= 0 ? _history[_cursor].Source : null;

    public bool CanGoBack => _cursor > 0;
    public bool CanGoForward => _cursor >= 0 && _cursor < _history.Count - 1;
    public bool CanGoUp => Current is not null && Path.GetDirectoryName(Current) is { Length: > 0 };

    public event EventHandler<string?>? CurrentChanged;


    public void NavigateTo(string path, NavigationSource source = NavigationSource.External) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        if (Current == path) {
            return;
        }

        // Replacing the forward tail: anything to the right of the cursor
        // disappears once a new navigation diverges from history.
        if (_cursor < _history.Count - 1) {
            _history.RemoveRange(_cursor + 1, _history.Count - _cursor - 1);
        }

        _history.Add(new NavigationEntry(path, source));
        _cursor = _history.Count - 1;
        RaiseChanged();
    }

    public string? GoBack() {
        if (!CanGoBack) {
            return null;
        }
        _cursor--;
        RaiseChanged();
        return Current;
    }

    public string? GoForward() {
        if (!CanGoForward) {
            return null;
        }
        _cursor++;
        RaiseChanged();
        return Current;
    }

    /// <summary>
    /// Navigate to the parent of the current folder. Inherits the current
    /// entry's source — going Up while browsing a bookmark stays "in
    /// bookmarks context" until the path leaves all known bookmark
    /// subtrees, at which point the expand-on-navigate fallback in the
    /// view model takes over.
    /// </summary>
    public string? GoUp() {
        if (Current is null) {
            return null;
        }

        var parent = Path.GetDirectoryName(Current);
        if (string.IsNullOrEmpty(parent)) {
            return null;
        }

        NavigateTo(parent, CurrentSource ?? NavigationSource.External);
        return Current;
    }


    private void RaiseChanged() {
        CurrentChanged?.Invoke(this, Current);
    }
}
