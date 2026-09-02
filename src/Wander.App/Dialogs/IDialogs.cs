using Wander.Core.FileSystem;

namespace Wander.App.Dialogs;

/// <summary>Which question is being asked - a harness answers by kind, not by reading the text.</summary>
public enum DialogKind {
    RecycleConfirm,
    PermanentDeleteConfirm,
    ReadOnlyConfirm,
    MoveConfirm,
    CreateSidecar,
    CannotPaste,
    ShellMenuReset,
    SettingsReset,
}

public enum DialogButtons {
    Ok,
    OkCancel,
    YesNo,
}

public enum DialogIcon {
    Information,
    Question,
    Warning,
    Error,
}

/// <summary>
/// A question the app puts to the user. The default button is always the
/// cancelling one (project rule: Enter must never destroy anything), so it
/// is not a field here.
/// </summary>
public sealed record DialogRequest(
    DialogKind Kind,
    string Title,
    string Message,
    DialogButtons Buttons,
    DialogIcon Icon);

/// <summary>
/// Every modal question the app asks goes through here, so a headless run
/// can answer them by policy instead of hanging on a message box nobody
/// will click. Production is <see cref="WpfDialogs"/>; the harness
/// substitutes its own before the view model is built.
/// </summary>
public interface IDialogs {
    /// <summary>True when the user accepted (OK / Yes). A single-button request returns true once shown.</summary>
    bool Ask(DialogRequest request);

    /// <summary>Text entry; null when cancelled.</summary>
    string? Prompt(string title, string label, string initial, bool filenameMode);

    /// <summary>Folder picker; null when cancelled.</summary>
    string? PickFolder(string title);

    /// <summary>The resolver a batch copy / move consults about collisions.</summary>
    IConflictResolver CreateConflictResolver();
}
