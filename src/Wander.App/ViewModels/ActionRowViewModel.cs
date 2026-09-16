using Wander.App.Resources;
using Wander.Core.Actions;

namespace Wander.App.ViewModels;

/// <summary>A value and what a combo box calls it.</summary>
public sealed record ChoiceItem<T>(T Value, string Title);


/// <summary>
/// One line of the actions table in settings. Holds the catalog row itself
/// and edits it with <c>with</c>, so a preset nobody touched stays equal to
/// the one in the code and is not written to <c>state.json</c>
/// (<see cref="ActionCatalog.ToStored"/>).
///
/// <para>
/// A preset is read-only here except for its checkbox and its program:
/// switching one off and pointing it at ffmpeg is all a shipped row needs,
/// anything more is a copy.
/// </para>
/// </summary>
public sealed class ActionRowViewModel : ObservableObject {
    private readonly Action _onChanged;
    private CustomAction _action;
    private IReadOnlyDictionary<string, ToolLocation> _tools = new Dictionary<string, ToolLocation>();


    public ActionRowViewModel(CustomAction action, Action onChanged) {
        _action = action;
        _onChanged = onChanged;
    }


    public static IReadOnlyList<ChoiceItem<FileTypeGroup>> GroupChoices { get; } = Enum.GetValues<FileTypeGroup>()
        .Select(g => new ChoiceItem<FileTypeGroup>(g, Capitalize(Strings.Get(FileTypeGroups.NameKey(g)))))
        .ToArray();

    public static IReadOnlyList<ChoiceItem<bool>> ModeChoices { get; } = new[] {
        new ChoiceItem<bool>(true, Strings.ActionsModePerFile),
        new ChoiceItem<bool>(false, Strings.ActionsModeOnce),
    };

    public static IReadOnlyList<ChoiceItem<ActionPlacement>> PlacementChoices { get; } = new[] {
        new ChoiceItem<ActionPlacement>(ActionPlacement.Both, Strings.ActionsPlacementBoth),
        new ChoiceItem<ActionPlacement>(ActionPlacement.ContextMenu, Strings.ActionsPlacementContext),
        new ChoiceItem<ActionPlacement>(ActionPlacement.Header, Strings.ActionsPlacementHeader),
    };

    /// <summary>The row as the catalog has it now.</summary>
    public CustomAction Action => _action;

    public bool IsPreset => _action.IsPreset;

    /// <summary>Everything but the checkbox and the program is the user's to change only on their own rows.</summary>
    public bool IsEditable => !_action.IsPreset;

    public bool Enabled {
        get => _action.Enabled;
        set => Update(_action with { Enabled = value });
    }

    public string Title {
        get => _action.DisplayTitle;
        set => Update(_action with { Title = value });
    }

    public FileTypeGroup Group {
        get => _action.Types.Group;
        set => Update(_action with { Types = _action.Types with { Group = value } });
    }

    /// <summary>A mask such as <c>*.psd;*.ai</c>; wins over the group when not empty.</summary>
    public string Mask {
        get => _action.Types.Mask;
        set => Update(_action with { Types = _action.Types with { Mask = value } });
    }

    public string Program {
        get => _action.Program;
        set => Update(_action with { Program = value });
    }

    public string Arguments {
        get => _action.Arguments;
        set => Update(_action with { Arguments = value });
    }

    public bool RunPerFile {
        get => _action.RunPerFile;
        set => Update(_action with { RunPerFile = value });
    }

    public ActionPlacement Placement {
        get => _action.Placement;
        set => Update(_action with { Placement = value });
    }

    public bool InSubmenu {
        get => _action.InSubmenu;
        set => Update(_action with { InSubmenu = value });
    }

    public bool HideConsole {
        get => _action.HideConsole;
        set => Update(_action with { HideConsole = value });
    }

    public string Output {
        get => _action.Output;
        set => Update(_action with { Output = value });
    }

    /// <summary>Why the command line cannot work in the chosen mode; empty when it can. Saving is not blocked.</summary>
    public string ValidationText => _action.Kind == ActionKind.Command
        && CommandLine.ValidationKey(_action.Arguments, _action.RunPerFile) is { } key
            ? Strings.Get(key)
            : string.Empty;

    /// <summary>The program the row needs is not available - its cell is marked, the fix is on the "Программы" page.</summary>
    public bool NeedsTool => ActionCatalog.NeedsTool(_action, _tools);

    /// <summary>What the marked program cell says on hover.</summary>
    public string? ProgramHint => NeedsTool
        ? string.Format(Strings.ActionsToolMissingHint, ActionPresets.KnownTool(_action.RequiredTool)?.Title ?? _action.RequiredTool)
        : null;


    /// <summary>Where the programs are; the program cell follows.</summary>
    public void SetTools(IReadOnlyDictionary<string, ToolLocation> tools) {
        _tools = tools;
        Raise(nameof(NeedsTool));
        Raise(nameof(ProgramHint));
    }


    private void Update(CustomAction next) {
        if (next == _action) {
            return;
        }

        _action = next;
        // Every column is a projection of the one record.
        Raise(string.Empty);
        _onChanged();
    }

    private static string Capitalize(string text) {
        return text.Length == 0 ? text : char.ToUpper(text[0]) + text[1..];
    }
}
