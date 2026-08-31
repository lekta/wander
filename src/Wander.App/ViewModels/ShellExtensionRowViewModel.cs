using Wander.App.Resources;
using Wander.Core.Shell;

namespace Wander.App.ViewModels;

/// <summary>
/// One line of the context-menu table in settings: a third-party entry, who
/// it belongs to, where it shows up, and whether the user has switched it
/// off.
///
/// <para>
/// A table rather than the pile of checkboxes this used to be, because two
/// of the four columns are the reason anyone can act on it. "TortoiseGit"
/// on its own is a name; "TortoiseGit — TortoiseGit — все файлы, папки,
/// фон папки" is a decision.
/// </para>
/// </summary>
public sealed class ShellExtensionRowViewModel : ObservableObject {
    private readonly Action<ShellExtensionRowViewModel> _onToggled;
    private bool _isBlocked;


    public ShellExtensionRowViewModel(ShellExtensionRow row, Action<ShellExtensionRowViewModel> onToggled) {
        Key = row.Key;
        Title = row.Title;
        // A dash, not a blank: an empty cell reads as "loading", and the
        // scope column next to it already says "—" for the same reason.
        AppName = row.AppName.Length > 0 ? row.AppName : Strings.SettingsShellScopeUnknown;
        IsSeen = row.IsSeen;
        IsSystem = row.IsSystem;
        _isBlocked = row.IsBlocked;
        _onToggled = onToggled;

        ScopesText = row.Scopes.Count > 0
            ? string.Join(", ", row.Scopes.Select(ShellScopes.Title))
            : Strings.SettingsShellScopeUnknown;
        Description = row.Help.Length > 0 ? row.Help : ScopesText;
    }


    /// <summary>What the blocklist stores — see <see cref="ShellEntryKey"/>.</summary>
    public string Key { get; }

    public string Title { get; }

    /// <summary>Empty when the registry could not name an owner; the column shows a dash.</summary>
    public string AppName { get; }

    /// <summary>Scopes joined for display: "все файлы, папки".</summary>
    public string ScopesText { get; }

    /// <summary>
    /// Row tooltip: what the handler says the entry does, falling back to
    /// where it shows up. Not a column — most handlers publish nothing, and
    /// a column that is empty two thirds of the time is a column of nothing.
    /// </summary>
    public string Description { get; }

    /// <summary>Wander has met this row in an actual menu, not only in the registry.</summary>
    public bool IsSeen { get; }

    public bool IsSystem { get; }

    /// <summary>Ticked = the row does not appear in menus.</summary>
    public bool IsBlocked {
        get => _isBlocked;
        set {
            if (SetField(ref _isBlocked, value)) {
                _onToggled(this);
            }
        }
    }
}
