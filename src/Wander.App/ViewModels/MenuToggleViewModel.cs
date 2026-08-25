namespace Wander.App.ViewModels;

/// <summary>
/// One "show this menu entry" checkbox in the settings dialog. Backs both
/// lists on the Context-menu page — Wander's own entries and the
/// third-party ones — because from the dialog's point of view they are the
/// same thing: a name, a flag, and a save when the flag moves.
/// </summary>
public sealed class MenuToggleViewModel : ObservableObject {
    private readonly Action _onChanged;
    private bool _isEnabled;


    public MenuToggleViewModel(string key, string title, bool isEnabled, Action onChanged) {
        Key = key;
        Title = title;
        _isEnabled = isEnabled;
        _onChanged = onChanged;
    }


    /// <summary>
    /// What gets persisted: a <c>MenuCommandId</c> name for built-ins, the
    /// normalised display name for shell extensions.
    /// </summary>
    public string Key { get; }

    /// <summary>Label shown next to the checkbox.</summary>
    public string Title { get; }

    /// <summary>Checked = the entry appears in the menu.</summary>
    public bool IsEnabled {
        get => _isEnabled;
        set {
            if (SetField(ref _isEnabled, value)) {
                _onChanged();
            }
        }
    }
}
