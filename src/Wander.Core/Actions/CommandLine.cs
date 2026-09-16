using System.Text;
using System.Text.RegularExpressions;

namespace Wander.Core.Actions;

/// <summary>
/// Turns an action's argument template into the command line a process is
/// started with. Placeholders, case-insensitive:
/// <c>{path}</c> the file, <c>{name}</c> its name without extension,
/// <c>{ext}</c> the extension without the dot, <c>{dir}</c> the folder it
/// is in, <c>{paths}</c> every selected file (one command for the whole
/// selection), <c>{list}</c> a temporary file listing them one per line
/// (for tools that take <c>@listfile</c>, and for selections longer than
/// a command line), <c>{out}</c> the declared output. In the one-command
/// mode <c>{path}</c> and its three companions refer to the first file.
///
/// <para>
/// Quoting is the substitution's job, never the user's: a value goes in
/// wrapped in double quotes, always, because a path with a space and a
/// template written without quotes is the classic way a batch of files
/// becomes two batches. A quote cannot appear in a file name on Windows,
/// so the one character that would need escaping never does; the trailing
/// backslash of a root folder does, and is doubled.
/// </para>
/// </summary>
public static class CommandLine {
    /// <summary>Resource key: a per-file action cannot use the whole-selection placeholders.</summary>
    public const string PerFileWithListKey = "ActionsErrorPerFileWithList";

    /// <summary>Resource key: a one-command action has nowhere to put the selection.</summary>
    public const string GroupWithoutListKey = "ActionsErrorGroupWithoutList";

    private static readonly Regex _placeholder = new(
        @"\{(path|name|ext|dir|paths|list|out)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);


    public static string Expand(string template, IReadOnlyList<string> paths, string? listFile = null, string? output = null) {
        string first = paths.Count > 0 ? paths[0] : string.Empty;

        return _placeholder.Replace(template, match => match.Groups[1].Value.ToLowerInvariant() switch {
            "path" => Quote(first),
            "name" => Quote(Path.GetFileNameWithoutExtension(first)),
            "ext" => Quote(Path.GetExtension(first).TrimStart('.')),
            "dir" => Quote(Path.GetDirectoryName(first) ?? string.Empty),
            "paths" => string.Join(' ', paths.Select(Quote)),
            "list" => listFile is null ? string.Empty : Quote(listFile),
            "out" => output is null ? string.Empty : Quote(output),
            _ => match.Value,
        });
    }


    /// <summary>Whether the template mentions a placeholder, given with its braces.</summary>
    public static bool Uses(string template, string placeholder) {
        return template.Contains(placeholder, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The one shape error the settings table can catch before a run:
    /// per-file mode with <c>{paths}</c> / <c>{list}</c>, or one-command
    /// mode without either. Returns the reason's resource key, or null.
    /// </summary>
    public static string? ValidationKey(string template, bool runPerFile) {
        bool usesGroup = Uses(template, "{paths}") || Uses(template, "{list}");
        if (runPerFile && usesGroup) {
            return PerFileWithListKey;
        }
        if (!runPerFile && !usesGroup) {
            return GroupWithoutListKey;
        }

        return null;
    }


    /// <summary>
    /// The value in double quotes, as CreateProcess reads them. Only a
    /// trailing backslash needs care: <c>"D:\"</c> would escape its own
    /// closing quote, so backslashes before the closing quote are doubled.
    /// </summary>
    public static string Quote(string value) {
        int trailing = 0;
        while (trailing < value.Length && value[value.Length - 1 - trailing] == '\\') {
            trailing++;
        }

        var sb = new StringBuilder(value.Length + trailing + 2);
        sb.Append('"').Append(value).Append('\\', trailing).Append('"');

        return sb.ToString();
    }
}
