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

    /// <summary>A custom action finished with failures or cancellations: what did not work, per file.</summary>
    ActionReport,

    /// <summary>The window is closing with operations still running: stop them and exit?</summary>
    ExitWithOperations,

    /// <summary>Delete on a bookmark: take the bookmark away, or delete the folder behind it?</summary>
    BookmarkOrFolder,

    /// <summary>A delete failed because another program holds the file: try again?</summary>
    DeleteInUse,

    /// <summary>The recycle bin would not take these: delete them for good, or stop here?</summary>
    RecycleUnavailable,
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
/// A question whose answers are named, for when "OK" would not say which
/// of two things happens. Cancel is always there besides, and is the
/// default.
/// </summary>
/// <param name="Choices">The answers, as the buttons read, left to right.</param>
/// <param name="CancelLabel">What the Cancel button reads, when "Cancel" would not say what it does; null is "Cancel".</param>
/// <param name="ArmDelay">
/// How long the answers stay disabled once the question is up: Cancel
/// alone works from the first moment. For a question that comes up in the
/// middle of a run of keystrokes, where an Enter meant for something else
/// must not pick an answer that cannot be taken back.
/// </param>
public sealed record ChoiceRequest(
    DialogKind Kind,
    string Title,
    string Message,
    IReadOnlyList<string> Choices,
    string? CancelLabel = null,
    TimeSpan? ArmDelay = null);

/// <summary>
/// A window that closes on its own schedule - when the work it shows is
/// over, whichever window is up - and so must never own a modal question:
/// a modal window whose owner is destroyed goes with it, leaving the
/// application disabled behind a loop nobody can see. The operation
/// window is one; <see cref="WpfDialogs"/> skips these when it looks for
/// an owner.
/// </summary>
public interface ITransientWindow {
}

/// <summary>
/// Every modal question the app asks goes through here, so a headless run
/// can answer them by policy instead of hanging on a message box nobody
/// will click. Production is <see cref="WpfDialogs"/>; the harness
/// substitutes its own before the view model is built.
/// </summary>
public interface IDialogs {
    /// <summary>True when the user accepted (OK / Yes). A single-button request returns true once shown.</summary>
    bool Ask(DialogRequest request);

    /// <summary>The index of the answer picked, or -1 for Cancel.</summary>
    int Choose(ChoiceRequest request);

    /// <summary>Text entry; null when cancelled.</summary>
    string? Prompt(string title, string label, string initial, bool filenameMode);

    /// <summary>
    /// Folder picker; null when cancelled. <paramref name="startAt"/> is
    /// the folder to open on - the caller's best guess at where the user
    /// is going, not a default answer.
    /// </summary>
    string? PickFolder(string title, string? startAt = null);

    /// <summary>
    /// The resolver a batch copy / move consults about collisions.
    /// <paramref name="skipIdentical"/> is the user's setting of the same
    /// name, passed in rather than read here so the resolver stays a thing
    /// with no opinions of its own.
    /// </summary>
    IConflictResolver CreateConflictResolver(bool skipIdentical);
}
