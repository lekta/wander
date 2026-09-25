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

    /// <summary>
    /// <c>--yield</c>: this instance shares the data folder with the copy
    /// the user actually runs, and gives way to it. What that means is the
    /// state store's business (it stops writing <c>state.json</c> while
    /// the other instance is alive); the flag is only parsed here, next to
    /// the other things the command line says about the data folder. The
    /// Rider launch profile passes it, so a Debug session on the real
    /// bookmarks and layout never overwrites what the installed copy
    /// saved a moment ago.
    /// </summary>
    public const string YieldOption = "--yield";

    private static string? _root;
    private static string _source = "default";
    private static string? _webView2;


    /// <summary>Root folder; subfolders below are all relative to it.</summary>
    public static string DataRoot => _root ?? Default;

    /// <summary>Where the root came from ("arg", "portable", "env", "override", "default") - for the session log header.</summary>
    public static string Source => _source;

    /// <summary>Started with <see cref="YieldOption"/>; see there.</summary>
    public static bool Yields { get; private set; }

    public static string StateFile => Path.Combine(DataRoot, "state.json");

    /// <summary>Per-folder records (<c>IFolderSettingsStore</c>): pinned views, later more.</summary>
    public static string FoldersFile => Path.Combine(DataRoot, "folders.json");

    public static string Logs => Path.Combine(DataRoot, "logs");

    public static string Thumbs => Path.Combine(DataRoot, "thumbs");

    public static string Crashes => Path.Combine(DataRoot, "crashes");

    /// <summary>
    /// The web view's profile (PLAN AD1): the components its browser keeps
    /// for itself - <see cref="SystemWebView2"/> with
    /// <see cref="UseSystemTemp"/>, where it goes with the temporary files,
    /// <see cref="DataWebView2"/> otherwise. Fixed the first time it is
    /// asked, after the settings are read: a browser keeps the folder it was
    /// started with, so a changed setting takes effect at the next start.
    /// </summary>
    public static string WebView2 => _webView2 ??= UseSystemTemp ? SystemWebView2 : DataWebView2;

    /// <summary>The web view's profile in the data folder.</summary>
    public static string DataWebView2 => Path.Combine(DataRoot, "WebView2");

    /// <summary>The web view's profile under the system's Temp folder - a profile nobody minds losing.</summary>
    public static string SystemWebView2 => Path.Combine(SystemTmp, "WebView2");

    /// <summary>
    /// Whether scratch copies go to the system's Temp folder instead of the
    /// data folder - <c>AppSettings.UseSystemTemp</c>, applied by the view
    /// model when the settings are loaded and whenever they change. Off
    /// until told: the sweep at startup runs before any setting is read,
    /// and looks in both places anyway.
    /// </summary>
    public static bool UseSystemTemp { get; set; }

    /// <summary>The data folder sits beside the exe (<c>--portable</c>).</summary>
    public static bool IsPortable => _source == "portable";

    /// <summary>
    /// Scratch copies Wander makes for the user to open or look at - entries
    /// pulled out of an archive so an application, or the preview pane, can
    /// be pointed at them. One of the two folders below, by
    /// <see cref="UseSystemTemp"/>; swept at startup, see <c>TempFiles</c>.
    /// </summary>
    public static string Tmp => UseSystemTemp ? SystemTmp : DataTmp;

    /// <summary>Scratch copies inside the data folder - beside the settings, or beside the exe when portable.</summary>
    public static string DataTmp => Path.Combine(DataRoot, "tmp");

    /// <summary>
    /// Scratch copies under the system's Temp folder - what Storage Sense
    /// and Disk Cleanup sweep, and off the stick when the data folder is on
    /// one.
    /// </summary>
    public static string SystemTmp => Path.Combine(Path.GetTempPath(), "Wander");


    /// <summary>
    /// Picks the root from the command line and the environment. Called
    /// once, before anything opens a file; calling it again re-resolves.
    /// </summary>
    public static void Resolve(IReadOnlyList<string> args) {
        Yields = args.Any(a => a.Equals(YieldOption, StringComparison.OrdinalIgnoreCase));

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
        _webView2 = null;
    }
}
