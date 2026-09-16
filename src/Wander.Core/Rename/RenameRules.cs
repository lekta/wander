namespace Wander.Core.Rename;

/// <summary>
/// How a set of names becomes another set of names: what the batch-rename
/// window edits, what <see cref="RenamePlanner"/> reads, and what
/// <c>state.json</c> remembers between sessions.
///
/// <para>
/// Three optional rules with a fixed order of application - find / replace,
/// then the template, then case - so the same settings always mean the same
/// thing and the preview can be recomputed on every keystroke. The
/// extension is not part of any of them: a group rename changes what a file
/// is called, not what it is. The one rule that touches it,
/// <see cref="ExtensionCase"/>, exists for the camera that writes
/// <c>.JPG</c>.
/// </para>
/// </summary>
public sealed record RenameRules {
    /// <summary>The template that changes nothing.</summary>
    public const string IdentityTemplate = "[N]";

    public static readonly RenameRules Default = new();


    // --- 1. Find / replace, on the name without its extension ------------

    /// <summary>Text (or a .NET regular expression) to look for; empty = rule off.</summary>
    public string Find { get; init; } = string.Empty;

    /// <summary>What it becomes. With <see cref="FindIsRegex"/>, <c>$1</c> refers to a group.</summary>
    public string Replace { get; init; } = string.Empty;

    public bool FindIgnoreCase { get; init; } = true;

    public bool FindIsRegex { get; init; }


    // --- 2. Template ------------------------------------------------------

    /// <summary>
    /// Tokens: <c>[N]</c> the name as step 1 left it, <c>[C]</c> the
    /// counter, <c>[D]</c> the modified date, <c>[X]</c> the shot date from
    /// EXIF (modified date when there is none), <c>[P]</c> the parent
    /// folder's name. <c>[D:yyyy-MM-dd HH-mm]</c> and <c>[X:...]</c> take a
    /// .NET date format. Anything else is literal.
    /// </summary>
    public string Template { get; init; } = IdentityTemplate;

    public int CounterStart { get; init; } = 1;

    public int CounterStep { get; init; } = 1;

    /// <summary>Minimum digits, zero-padded: 3 gives 001.</summary>
    public int CounterWidth { get; init; } = 1;


    // --- 3. Case -----------------------------------------------------------

    public NameCase NameCase { get; init; } = NameCase.Unchanged;

    public NameCase ExtensionCase { get; init; } = NameCase.Unchanged;


    /// <summary>True when applying these rules could not change any name.</summary>
    public bool IsIdentity =>
        Find.Length == 0
        && Template == IdentityTemplate
        && NameCase == NameCase.Unchanged
        && ExtensionCase == NameCase.Unchanged;
}


public enum NameCase {
    Unchanged,
    Lower,
    Upper,

    /// <summary>First letter upper, the rest lower.</summary>
    Sentence,
}
