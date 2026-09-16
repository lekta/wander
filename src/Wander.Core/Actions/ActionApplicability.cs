using Wander.Core.FileSystem;

namespace Wander.Core.Actions;

public enum ActionState {
    Applicable,

    /// <summary>Nothing is selected and the action is not about folders.</summary>
    NeedsSelection,

    /// <summary>Not every selected item is of the action's type.</summary>
    NotForSelection,

    /// <summary>The tool the action needs is not on this machine.</summary>
    ToolMissing,
}


/// <summary>
/// Whether an action applies to what is selected. One rule, deliberately
/// simple: every selected item must match, or the action is not offered -
/// a mixed selection gets only the actions for "everything". Partial
/// matches are not counted or explained (BACKLOG); the user can see what
/// they selected.
///
/// <para>
/// With nothing selected, an action for folders applies to the folder on
/// screen - that is what the header menu and the background context menu
/// are about.
/// </para>
/// </summary>
public static class ActionApplicability {
    public const string SelectFilesKey = "MenuReasonSelectFiles";
    public const string NotForSelectionKey = "MenuReasonNotForSelection";
    public const string ToolMissingKey = "MenuReasonToolMissing";


    public static ActionState For(
        CustomAction action, IReadOnlyList<FileSystemEntry> selection,
        bool hasFolder, Func<string, bool> toolAvailable) {

        if (action.RequiredTool.Length > 0 && !toolAvailable(action.RequiredTool)) {
            return ActionState.ToolMissing;
        }

        if (selection.Count == 0) {
            bool forFolders = !action.Types.HasMask && action.Types.Group == FileTypeGroup.Folders;

            return forFolders && hasFolder ? ActionState.Applicable : ActionState.NeedsSelection;
        }

        foreach (var entry in selection) {
            if (!action.Types.Matches(entry)) {
                return ActionState.NotForSelection;
            }
        }

        return ActionState.Applicable;
    }


    /// <summary>
    /// Resource key of the reason a row is greyed; null when it is not.
    /// <see cref="ToolMissingKey"/> takes the tool's name as <c>{0}</c>.
    /// </summary>
    public static string? ReasonKey(ActionState state) {
        return state switch {
            ActionState.NeedsSelection => SelectFilesKey,
            ActionState.NotForSelection => NotForSelectionKey,
            ActionState.ToolMissing => ToolMissingKey,
            _ => null,
        };
    }
}
