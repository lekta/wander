namespace Wander.Core.Navigation;

/// <summary>
/// The folder a session is asked to start in (2026-09-28):
/// <c>--folder &lt;path&gt;</c> on the command line. It wins over the last
/// folder and the working folder; a folder that is not there is not opened,
/// and the session starts where it would without the option.
///
/// <para>
/// What a test run is given - <c>check.bat run</c> and the harness - so that
/// it lists a folder of its own: with no state a session starts in the
/// working folder, and that is the user's Documents.
/// </para>
/// </summary>
public static class StartFolder {
    public const string Option = "--folder";


    /// <summary>The folder asked for, as a full path; null when none was.</summary>
    public static string? Asked { get; private set; }


    /// <summary>
    /// Reads the option off the command line, once, at startup. A command
    /// line without it leaves what <see cref="Override"/> set: the harness
    /// names its folder before the application starts.
    /// </summary>
    public static void Resolve(IReadOnlyList<string> args) {
        Asked = FromArguments(args) ?? Asked;
    }

    /// <summary>Programmatic - the harness, whose command line is not the application's; null forgets the folder.</summary>
    public static void Override(string? folder) {
        Asked = folder is null ? null : Path.GetFullPath(folder);
    }

    /// <summary>
    /// The folder <paramref name="args"/> name, as a full path:
    /// <c>--folder path</c> or <c>--folder=path</c>, the quotes a shell left
    /// on the path dropped. Null when the option is not there or names
    /// nothing - another option after it is not a path.
    /// </summary>
    public static string? FromArguments(IReadOnlyList<string> args) {
        for (int i = 0; i < args.Count; i++) {
            string arg = args[i];
            string? value;
            if (arg.Equals(Option, StringComparison.OrdinalIgnoreCase)) {
                value = i + 1 < args.Count ? args[i + 1] : null;
            } else if (arg.StartsWith(Option + "=", StringComparison.OrdinalIgnoreCase)) {
                value = arg[(Option.Length + 1)..];
            } else {
                continue;
            }

            value = value?.Trim().Trim('"');
            if (string.IsNullOrEmpty(value) || value.StartsWith("--", StringComparison.Ordinal)) {
                return null;
            }

            return Path.GetFullPath(value);
        }

        return null;
    }
}
