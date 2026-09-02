namespace Wander.Core.Persistence;

/// <summary>
/// The one root under which Wander keeps everything it writes for itself:
/// state.json, logs, the thumbnail cache, crash bundles, the WebView2
/// profile. Resolved once at startup and read everywhere else, so a test
/// harness, a portable install or a second instance on the same machine can
/// point the whole set somewhere else with one call instead of five.
/// </summary>
/// <remarks>
/// Precedence: <see cref="Override"/> (harness, tests), then
/// <c>--data-dir &lt;path&gt;</c>, then <c>--portable</c> (a <c>data</c>
/// folder next to the executable), then the <c>WANDER_DATA_DIR</c>
/// environment variable, then <c>%LOCALAPPDATA%\Wander</c>.
/// <c>LOCALAPPDATA</c> itself is deliberately not consulted: the runtime
/// answers <see cref="Environment.SpecialFolder.LocalApplicationData"/>
/// from the shell, not from the environment, so overriding the variable
/// would move nothing.
/// </remarks>
public static class AppPaths {
    public const string DataDirOption = "--data-dir";
    public const string PortableOption = "--portable";
    public const string EnvironmentVariable = "WANDER_DATA_DIR";
    public const string PortableFolderName = "data";

    private static string? _root;
    private static string _source = "default";


    /// <summary>Root folder; subfolders below are all relative to it.</summary>
    public static string DataRoot => _root ?? Default;

    /// <summary>Where the root came from ("arg", "portable", "env", "override", "default") - for the session log header.</summary>
    public static string Source => _source;

    public static string StateFile => Path.Combine(DataRoot, "state.json");

    public static string Logs => Path.Combine(DataRoot, "logs");

    public static string Thumbs => Path.Combine(DataRoot, "thumbs");

    public static string Crashes => Path.Combine(DataRoot, "crashes");

    public static string WebView2 => Path.Combine(DataRoot, "WebView2");

    /// <summary>
    /// Scratch copies Wander makes for the user to open - today, entries
    /// pulled out of an archive so an application can be pointed at them.
    /// Swept at startup; see <c>TempFiles</c>.
    /// </summary>
    public static string Tmp => Path.Combine(DataRoot, "tmp");


    /// <summary>
    /// Picks the root from the command line and the environment. Called
    /// once, before anything opens a file; calling it again re-resolves.
    /// </summary>
    public static void Resolve(IReadOnlyList<string> args) {
        for (int i = 0; i < args.Count; i++) {
            string arg = args[i];
            if (arg.Equals(DataDirOption, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) {
                Set(Path.GetFullPath(args[i + 1]), "arg");

                return;
            }
            if (arg.StartsWith(DataDirOption + "=", StringComparison.OrdinalIgnoreCase)) {
                Set(Path.GetFullPath(arg[(DataDirOption.Length + 1)..]), "arg");

                return;
            }
        }

        if (args.Any(a => a.Equals(PortableOption, StringComparison.OrdinalIgnoreCase))) {
            string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(exeDir)) {
                Set(Path.Combine(exeDir, PortableFolderName), "portable");

                return;
            }
        }

        string? env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env)) {
            Set(Path.GetFullPath(env), "env");

            return;
        }

        Set(Default, "default");
    }

    /// <summary>Programmatic root - the harness and tests, which never see a command line.</summary>
    public static void Override(string root) {
        Set(Path.GetFullPath(root), "override");
    }


    private static string Default =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wander");

    private static void Set(string root, string source) {
        _root = root;
        _source = source;
    }
}
