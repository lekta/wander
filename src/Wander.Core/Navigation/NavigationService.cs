namespace Wander.Core.Navigation;

public sealed class NavigationService {
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private string? _current;


    public string? Current => _current;
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => _current is not null && Path.GetDirectoryName(_current) is { Length: > 0 };

    public event EventHandler<string?>? CurrentChanged;


    public void NavigateTo(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        if (_current == path) {
            return;
        }

        if (_current is not null) {
            _back.Push(_current);
        }

        _forward.Clear();
        SetCurrent(path);
    }

    public string? GoBack() {
        if (!CanGoBack) {
            return null;
        }

        if (_current is not null) {
            _forward.Push(_current);
        }

        SetCurrent(_back.Pop());
        return _current;
    }

    public string? GoForward() {
        if (!CanGoForward) {
            return null;
        }

        if (_current is not null) {
            _back.Push(_current);
        }

        SetCurrent(_forward.Pop());
        return _current;
    }

    public string? GoUp() {
        if (_current is null) {
            return null;
        }

        var parent = Path.GetDirectoryName(_current);
        if (string.IsNullOrEmpty(parent)) {
            return null;
        }

        NavigateTo(parent);
        return _current;
    }


    private void SetCurrent(string path) {
        _current = path;
        CurrentChanged?.Invoke(this, _current);
    }
}
