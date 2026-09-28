namespace Wander.Core.Actions;

/// <summary>
/// One line of the program list in an action's form (2026-09-28) - see
/// <see cref="ActionCatalog.ProgramChoices"/>.
/// </summary>
/// <param name="Program">What the row's program becomes: a path or a file name, or a built-in handler's name.</param>
/// <param name="Tool">
/// The tool it is, found or pointed at on the "Программы" page; empty for a
/// program a row names itself and for a built-in handler.
/// </param>
/// <param name="Title">What the line says: the tool's name, the handler's title, or the program as the row has it.</param>
/// <param name="Kind">An external program, or one of Wander's own handlers.</param>
public sealed record ProgramChoice(string Program, string Tool, string Title, ActionKind Kind = ActionKind.Command);
