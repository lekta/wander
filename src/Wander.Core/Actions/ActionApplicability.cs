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
///
/// <para>
/// A missing tool is asked about last: it is the one state a menu shows
/// greyed, as a hint to install the tool, and that hint is only worth
/// giving for an action that would otherwise run on this selection.
/// </para>
/// </summary>
public static class ActionApplicability {
    /// <summary>Why a row is greyed; takes the tool's name as <c>{0}</c>.</summary>
    public const string ToolMissingKey = "MenuReasonToolMissing";


    public static ActionState For(
        CustomAction action, IReadOnlyList<FileSystemEntry> selection,
        bool hasFolder, Func<string, bool> toolAvailable) {

        if (selection.Count == 0) {
            bool forFolders = !action.Types.HasMask && action.Types.Group == FileTypeGroup.Folders;
            if (!forFolders || !hasFolder) {
                return ActionState.NeedsSelection;
            }
        }

        foreach (var entry in selection) {
            if (!action.Types.Matches(entry)) {
                return ActionState.NotForSelection;
            }
        }

        if (action.RequiredTool.Length > 0 && !toolAvailable(action.RequiredTool)) {
            return ActionState.ToolMissing;
        }

        return ActionState.Applicable;
    }
}
