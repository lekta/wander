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
/// on its own is a name; "TortoiseGit - TortoiseGit - все файлы, папки,
/// фон папки" is a decision.
/// </para>
/// </summary>
public sealed class ShellExtensionRowViewModel : ObservableObject {
    private readonly ShellExtensionRow _row;
    private readonly Action<ShellExtensionRowViewModel> _onToggled;
    private bool _isBlocked;


    public ShellExtensionRowViewModel(ShellExtensionRow row, Action<ShellExtensionRowViewModel> onToggled) {
        _row = row;
        Key = row.Key;
        Keys = row.AllKeys.ToArray();
        Title = row.Title;
        // A dash, not a blank: an empty cell reads as "loading", and the
        // scope column next to it shows one for the same reason.
        AppName = row.AppName.Length > 0 ? row.AppName : Strings.SettingsShellScopeUnknown;
        IsSystem = row.IsSystem;
        _isBlocked = row.IsBlocked;
        _onToggled = onToggled;

        ScopesText = row.Scopes.Count > 0
            ? string.Join(", ", row.Scopes.Select(ShellScopes.Title))
            : Strings.SettingsShellScopeUnknown;

        // A row whose caption is the application's own name is the whole
        // section that application hangs in the menu — 7-Zip's popup, not
        // one command inside it. Reading "7-Zip / 7-Zip / все файлы" and
        // wondering what it means is exactly what the note answers; what is
        // inside the section cannot be listed, because a COM handler
        // decides that at popup time.
        string note = row.AppName.Length > 0
            && string.Equals(row.Title, row.AppName, StringComparison.CurrentCultureIgnoreCase)
            ? Strings.SettingsShellAppSection
            : "";
        // No tip at all rather than one that repeats a column (2026-09-28):
        // the scopes used to stand in for a missing description, and a tip
        // saying "все файлы" over a cell saying "все файлы" is noise.
        string text = string.Join("\n", new[] { note, row.Help }.Where(part => part.Length > 0));
        Description = text.Length > 0 ? text : null;
    }


    /// <summary>What the blocklist stores — see <see cref="ShellEntryKey"/>.</summary>
    public string Key { get; }

    /// <summary>
    /// Every key the checkbox switches — the row's own plus the aliases it
    /// folded in. One row on screen can stand for two registry entries that
    /// look identical; switching off only one of them would leave the item
    /// in the menu.
    /// </summary>
    public IReadOnlyList<string> Keys { get; }

    public string Title { get; }

    /// <summary>The owning application; a dash when the registry could not name one.</summary>
    public string AppName { get; }

    /// <summary>Scopes joined for display: "все файлы, папки".</summary>
    public string ScopesText { get; }

    /// <summary>
    /// Row tooltip: what the handler says the entry does, and whether the
    /// row is an application's whole section; null when there is neither.
    /// Not a column — most handlers publish nothing, and a column that is
    /// empty two thirds of the time is a column of nothing.
    /// </summary>
    public string? Description { get; }

    public bool IsSystem { get; }

    /// <summary>
    /// Ticked = the row appears in menus - the table's "Вкл", the same way
    /// round as every checkbox of the settings (2026-09-28). Stored the
    /// other way round, as the blocklist.
    /// </summary>
    public bool IsShown {
        get => !_isBlocked;
        set {
            if (SetField(ref _isBlocked, !value)) {
                _onToggled(this);
            }
        }
    }

    public bool IsBlocked => _isBlocked;


    /// <summary>Whether the filter above the table lets the row through - see <see cref="ShellExtensionFilter"/>.</summary>
    public bool Matches(string query) {
        return ShellExtensionFilter.Matches(_row, query, ShellScopes.Title);
    }
}
