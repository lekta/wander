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
/// A preset is read-only here except for its checkbox: where its tool is,
/// is the "Программы" page's; anything more is a copy.
/// </para>
/// </summary>
public sealed class ActionRowViewModel : ObservableObject {
    private readonly Action _onChanged;
    private readonly Func<string, string?> _locate;
    private readonly Func<IReadOnlyList<ProgramChoice>> _programChoices;
    private CustomAction _action;
    private IReadOnlyDictionary<string, ToolLocation> _tools = new Dictionary<string, ToolLocation>();


    /// <param name="locate">Where a program is - see <see cref="ActionCatalog.ProgramValidationKey"/>.</param>
    /// <param name="programChoices">The owner's program list, the same for every row - see <see cref="ActionCatalog.ProgramChoices"/>.</param>
    public ActionRowViewModel(
        CustomAction action, Action onChanged, Func<string, string?> locate, Func<IReadOnlyList<ProgramChoice>> programChoices) {

        _action = action;
        _onChanged = onChanged;
        _locate = locate;
        _programChoices = programChoices;
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

    /// <summary>Everything but the checkbox is the user's to change only on their own rows.</summary>
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

    public string Program => _action.Program;

    /// <summary>
    /// The programs the form's list offers (2026-09-28): what the actions
    /// already use. A program new to them is chosen on the disk - see
    /// <see cref="ChooseProgramFile"/>.
    /// </summary>
    public IReadOnlyList<ProgramChoice> ProgramChoices => _programChoices();

    /// <summary>
    /// The list's line for the row's program. Picking one sets the program
    /// and the tool it is together (<see cref="ActionCatalog.WithProgram"/>).
    /// Null is what the box reports while its list is being replaced, not a
    /// choice: the program stays.
    /// </summary>
    public ProgramChoice? ProgramChoice {
        get => ActionCatalog.ChoiceOf(_action, _programChoices());
        set {
            if (value is not null) {
                Update(ActionCatalog.WithProgram(_action, value));
            }
        }
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

    /// <summary>
    /// The program's console window on screen while it runs. Ticked = shown,
    /// the same way round as every checkbox of the settings (2026-09-28);
    /// stored the other way round, as <see cref="CustomAction.HideConsole"/>.
    /// </summary>
    public bool ShowConsole {
        get => !_action.HideConsole;
        set => Update(_action with { HideConsole = !value });
    }

    public string Output {
        get => _action.Output;
        set => Update(_action with { Output = value });
    }

    /// <summary>
    /// Why the arguments cannot work: a command line in the chosen mode, or
    /// the built-in encoder's settings; empty when they can. Saving is not
    /// blocked.
    /// </summary>
    public string ValidationText => _action.Kind == ActionKind.Command
        ? CommandLine.ValidationKey(_action.Arguments, _action.RunPerFile) is { } key ? Strings.Get(key) : string.Empty
        : ActionCatalog.BuiltinProblem(_action) ?? string.Empty;

    /// <summary>What the arguments box takes, on its tooltip: a command line's placeholders, or the built-in encoder's settings.</summary>
    public string ArgumentsHint => _action.Kind == ActionKind.Builtin
        ? Strings.SettingsActionsBuiltinSettings
        : Strings.SettingsActionsPlaceholders;

    /// <summary>The program the row needs is not available - its cell is marked, the fix is on the "Программы" page.</summary>
    public bool NeedsTool => ActionCatalog.NeedsTool(_action, _tools);

    /// <summary>
    /// Why the row's program will not start, in red under the field: its tool
    /// is not found - the fix is on the "Программы" page - or its own program
    /// is not chosen or not there. Empty when it will. Saving is not blocked.
    /// </summary>
    public string ProgramText => NeedsTool
        ? string.Format(Strings.ActionsToolMissingHint, ActionPresets.KnownTool(_action.RequiredTool)?.Title ?? _action.RequiredTool)
        : ActionCatalog.ProgramValidationKey(_action, _locate) is { } key
            ? Strings.Get(key)
            : string.Empty;

    /// <summary>The program field is marked: the row's tool is missing, or its own program.</summary>
    public bool ProgramProblem => ProgramText.Length > 0;

    /// <summary>The table's "Для чего": the mask when there is one, else the group.</summary>
    public string TypesText => Mask.Length > 0
        ? Mask
        : GroupChoices.FirstOrDefault(c => c.Value == Group)?.Title ?? string.Empty;

    /// <summary>The table's "Где".</summary>
    public string PlacementText => PlacementChoices.FirstOrDefault(c => c.Value == Placement)?.Title ?? string.Empty;

    /// <summary>
    /// Something in the row's command will not work as it stands - the
    /// program is missing, the arguments do not suit the mode: its line in
    /// the table is marked, what is wrong is said in the form under it.
    /// </summary>
    public bool HasProblem => ProgramProblem || ValidationText.Length > 0;


    /// <summary>Where the programs are; the program cell follows.</summary>
    public void SetTools(IReadOnlyDictionary<string, ToolLocation> tools) {
        _tools = tools;
        Raise(nameof(NeedsTool));
        Raise(nameof(ProgramText));
        Raise(nameof(ProgramProblem));
        Raise(nameof(HasProblem));
    }

    /// <summary>A program chosen on the disk with the button beside the list: the row's own, no tool.</summary>
    public void ChooseProgramFile(string path) {
        Update(ActionCatalog.WithProgram(_action, new ProgramChoice(path, string.Empty, path)));
    }

    /// <summary>The owner's list changed: the box reads it again, and its line in it.</summary>
    public void RaiseProgramChoices() {
        Raise(nameof(ProgramChoices));
        Raise(nameof(ProgramChoice));
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
