using Wander.Core.FileSystem;

namespace Wander.Core.Actions;

/// <summary>
/// Where a declared output goes: beside its source - or in the folder the
/// user picked - under the name the template gives (<c>{name}</c> the
/// source's name without extension, <c>{ext}</c> its extension without the
/// dot), and never over anything: a taken name gets the number after the
/// highest already there, the rule every invented name in Wander follows
/// (<see cref="UniqueNames"/>). The source itself counts as taken, so
/// <c>{name}.{ext}</c> yields "photo (1).jpg", not a program writing over
/// its own input.
/// </summary>
public static class OutputNames {
    /// <param name="exists">Whether a path is taken, by a file or a folder.</param>
    /// <param name="namesIn">The names in a folder; asked only when the plain name is taken.</param>
    /// <param name="folder">Where the output goes; beside the source when null.</param>
    public static string Resolve(
        string template, string sourcePath, Func<string, bool> exists, Func<string, IEnumerable<string>> namesIn,
        string? folder = null) {

        string dir = folder ?? Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string name = template
            .Replace("{name}", Path.GetFileNameWithoutExtension(sourcePath), StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", Path.GetExtension(sourcePath).TrimStart('.'), StringComparison.OrdinalIgnoreCase);

        return UniqueNames.Resolve(Path.Combine(dir, name), exists, namesIn);
    }
}
