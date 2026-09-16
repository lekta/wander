using Wander.Core.Localization;

namespace Wander.Core.Actions;

public enum ActionKind {
    /// <summary>An external program with a command line.</summary>
    Command,

    /// <summary>One of Wander's own handlers (<see cref="IBuiltinAction"/>), named by <see cref="CustomAction.Program"/>.</summary>
    Builtin,
}


public enum ActionPlacement {
    Both,
    ContextMenu,
    Header,
}


/// <summary>Which submenu of the header the action lives in.</summary>
public enum ActionCategory {
    /// <summary>"Действия": what the user set up.</summary>
    Actions,

    /// <summary>"Конвертировать": the shipped presets and their copies.</summary>
    Convert,
}


/// <summary>
/// One row of the actions catalog - the single mechanism behind the
/// header's "Действия" and "Конвертировать", the context menu's copies of
/// them, and the settings table that edits them. A shipped preset and a
/// command the user typed in are the same record: the preset just comes
/// with a title key, a required tool and <see cref="IsPreset"/> set.
///
/// <para>
/// Persisted as-is in <c>state.json</c> (<c>AppSettings.CustomActions</c>),
/// so every property has a default and none of them is a delegate.
/// </para>
/// </summary>
public sealed record CustomAction {
    /// <summary>Stable identity: a GUID for the user's rows, <c>preset:...</c> for shipped ones.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>What the menu row says; empty for a preset, which has <see cref="TitleKey"/> instead.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Resource key of a preset's title, so the row is translated with the rest of the UI.</summary>
    public string TitleKey { get; init; } = string.Empty;

    public FileTypeSelector Types { get; init; } = new();

    public ActionKind Kind { get; init; } = ActionKind.Command;

    /// <summary>
    /// The program: a full path, or a name resolved through <c>PATH</c>.
    /// For a built-in action, the handler's name.
    /// </summary>
    public string Program { get; init; } = string.Empty;

    /// <summary>
    /// Command-line template - see <see cref="CommandLine"/> for the
    /// placeholders. For a built-in action, its own <c>key=value;...</c>
    /// settings.
    /// </summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>One process per selected file, in turn; otherwise one process for the whole selection.</summary>
    public bool RunPerFile { get; init; } = true;

    public ActionPlacement Placement { get; init; } = ActionPlacement.Both;

    /// <summary>Inside the "Действия" submenu, or as a row beside it.</summary>
    public bool InSubmenu { get; init; } = true;

    /// <summary>Run without a console window; also what makes stderr readable for the report.</summary>
    public bool HideConsole { get; init; } = true;

    /// <summary>
    /// Name of the file the action produces beside its input, as a template
    /// (<c>{name}.mp4</c>); empty when the action declares no output. A
    /// declared output is what the journal can name and what Ctrl+Z can
    /// send to the recycle bin.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    public ActionCategory Category { get; init; } = ActionCategory.Actions;

    /// <summary>Shipped with Wander: cannot be deleted, is edited by copying, is switched off with <see cref="Enabled"/>.</summary>
    public bool IsPreset { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The external tool the action cannot run without ("ffmpeg"), for the
    /// "not found" mark in settings and the greyed row in the header. Empty
    /// when the program itself is the whole requirement.
    /// </summary>
    public string RequiredTool { get; init; } = string.Empty;


    /// <summary>The title as shown: the preset's key resolved, or the user's own words.</summary>
    public string DisplayTitle => TitleKey.Length > 0 ? Text.Get(TitleKey) : Title;
}
