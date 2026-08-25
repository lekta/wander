namespace Wander.Core.Menu;

/// <summary>
/// One rendered row of a context menu — a command, a separator, or a
/// submenu header carrying <see cref="Children"/>.
///
/// <para>
/// Deliberately a single type rather than a hierarchy: the UI layer walks
/// this list top-down and needs to branch on shape anyway, and a
/// three-class hierarchy would buy nothing but ceremony. A row is a
/// built-in when <see cref="Id"/> is set, a third-party shell entry when
/// <see cref="ShellCommand"/> is non-negative, and never both.
/// </para>
/// </summary>
public sealed record MenuEntry {
    /// <summary>Shared instance for divider rows — they carry no state.</summary>
    public static readonly MenuEntry Divider = new() { IsSeparator = true };


    /// <summary>Built-in identity, or <see cref="MenuCommandId.None"/> for shell entries.</summary>
    public MenuCommandId Id { get; init; } = MenuCommandId.None;

    /// <summary>Text shown to the user. Empty for separators.</summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>Right-aligned hotkey hint ("Ctrl+C"), or null when the action has no binding.</summary>
    public string? Gesture { get; init; }

    public bool IsSeparator { get; init; }

    /// <summary>
    /// False when the entry does not apply to the current selection (Rename
    /// on a multi-selection, for example). The row is still drawn — greyed
    /// out — so the menu keeps a stable shape between right-clicks.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>The action a double-click would perform; rendered in bold.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Renders as a checkbox row (view mode, sort key, toggles).</summary>
    public bool IsCheckable { get; init; }

    public bool IsChecked { get; init; }

    /// <summary>
    /// Shell-extension command id, valid only inside the session that
    /// produced it. -1 means "not a shell entry".
    /// </summary>
    public int ShellCommand { get; init; } = -1;

    /// <summary>PNG bytes of the entry's icon, when the shell supplied one.</summary>
    public byte[]? IconPng { get; init; }

    public IReadOnlyList<MenuEntry> Children { get; init; } = Array.Empty<MenuEntry>();


    public bool IsShellCommand => ShellCommand >= 0;

    public bool HasChildren => Children.Count > 0;
}
