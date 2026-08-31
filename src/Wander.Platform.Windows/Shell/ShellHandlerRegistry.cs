using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Wander.Core.Logging;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Reads the installed context-menu extensions straight out of the registry.
///
/// <para>
/// This is the half <c>IContextMenu</c> refuses to tell us — which handler
/// a menu row came from, which application owns it, what it is registered
/// for. The shell hands over a merged menu and no provenance, so the only
/// way to fill the settings table's "Приложение" and "Типы" columns is to
/// go and look.
/// </para>
///
/// <para>
/// <b>Nothing here needs permission and nothing here writes.</b>
/// <c>HKLM\SOFTWARE\Classes</c> and <c>HKCU\SOFTWARE\Classes</c> are
/// readable by <c>BUILTIN\Users</c>; no elevation, no UAC, no audit unless
/// an administrator turned auditing on deliberately. It is what every
/// shell-extension manager does — ShellExView, Autoruns, CCleaner all
/// enumerate exactly these keys. What gets an application into trouble is
/// <em>writing</em> under <c>shellex</c>, which Wander never does.
/// </para>
///
/// <para>
/// <b>Why the two halves and not HKCR.</b> <c>HKEY_CLASSES_ROOT</c> is a
/// merged view of the two, and enumerating it is pathologically slow —
/// measured at over two minutes for the same walk that takes under a
/// quarter of a second across <c>HKLM</c> and <c>HKCU</c> separately.
/// Merging the results ourselves is both faster and clearer about which
/// hive an entry came from.
/// </para>
/// </summary>
public sealed class ShellHandlerRegistry : IShellHandlerRegistry {
    /// <summary>
    /// The two sub-keys a scope can carry handlers in: COM objects and
    /// plain verbs. <c>SystemFileAssociations</c> is the third place
    /// Windows lets an extension hang things off, and it is searched under
    /// the same two names.
    /// </summary>
    private static readonly string[] _handlerKeys = {
        @"shellex\ContextMenuHandlers",
        "shell",
    };

    private readonly ILogger _log;

    /// <summary>
    /// CLSID → the DLL behind it. Resolving one means a registry lookup and
    /// then reading the file's version resource, which is the only file I/O
    /// in the whole scan; the same handler is registered on half a dozen
    /// scopes, so caching turns dozens of reads into a handful.
    /// </summary>
    private readonly Dictionary<string, string> _servers = new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string>? _searchPath;


    public ShellHandlerRegistry(ILogger log) {
        _log = log;
    }


    public IReadOnlyList<ShellHandler> Scan(IReadOnlyList<string> scopes) {
        var found = new List<ShellHandler>();
        var watch = Stopwatch.StartNew();

        foreach (var (hive, path) in Hives()) {
            using var root = OpenRead(hive, path);
            if (root is null) {
                continue;
            }

            foreach (string scope in scopes) {
                Collect(root, scope, scope, found);
                Collect(root, $@"SystemFileAssociations\{scope}", scope, found);
            }
        }

        _log.Info($"Shell handlers: {found.Count} entries over {scopes.Count} scopes in {watch.ElapsedMilliseconds} ms");

        return found;
    }


    public IReadOnlyList<string> ListExtensions() {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, path) in Hives()) {
            using var root = OpenRead(hive, path);
            if (root is null) {
                continue;
            }

            try {
                foreach (string name in root.GetSubKeyNames()) {
                    // Names only — opening each of the ~800 keys to see what
                    // is inside is what makes a registry walk slow, and the
                    // picker does not need it.
                    if (name.Length > 1 && name[0] == '.') {
                        names.Add(name.ToLowerInvariant());
                    }
                }
            } catch (Exception ex) {
                _log.Warn($"Shell handlers: cannot list extensions under {path}: {ex.Message}");
            }
        }

        return names.ToArray();
    }


    // --- Walking ---------------------------------------------------------

    private static IEnumerable<(RegistryHive Hive, string Path)> Hives() {
        yield return (RegistryHive.LocalMachine, @"SOFTWARE\Classes");
        yield return (RegistryHive.CurrentUser, @"SOFTWARE\Classes");
    }

    private void Collect(RegistryKey root, string keyPath, string scope, List<ShellHandler> found) {
        foreach (string handlerKey in _handlerKeys) {
            using var container = OpenSub(root, $@"{keyPath}\{handlerKey}");
            if (container is null) {
                continue;
            }

            string[] names;
            try {
                names = container.GetSubKeyNames();
            } catch (Exception) {
                continue;
            }

            foreach (string name in names) {
                var handler = handlerKey == "shell"
                    ? ReadVerb(container, name, scope)
                    : ReadComHandler(root, container, name, scope);
                if (handler is not null) {
                    found.Add(handler);
                }
            }
        }
    }

    /// <summary>
    /// A <c>shell\&lt;verb&gt;</c> entry: everything about it is in the
    /// registry, including the label, so this is the accurate half of the
    /// table. The key name is the canonical verb the shell reports back,
    /// which makes the match against a menu row exact.
    /// </summary>
    private ShellHandler? ReadVerb(RegistryKey container, string verb, string scope) {
        using var key = OpenSub(container, verb);
        if (key is null) {
            return null;
        }

        string label = Indirect(key.GetValue(string.Empty) as string)
            ?? Indirect(key.GetValue("MUIVerb") as string)
            ?? verb;

        string executable = ExecutableFor(key);

        return new ShellHandler {
            Key = verb,
            Title = ShellEntryKey.Normalize(label),
            AppName = VersionName(executable),
            Scopes = new[] { scope },
            Kind = ShellHandlerKind.Verb,
            // No command line of its own means DelegateExecute or
            // ExplorerCommandHandler — Windows' modern verb plumbing, which
            // third parties essentially never use.
            IsSystem = executable.Length == 0 || IsSystemFile(executable),
        };
    }

    /// <summary>
    /// A <c>shellex\ContextMenuHandlers\&lt;name&gt;</c> entry. What it will
    /// draw is decided inside the handler's own DLL at popup time, so the
    /// registry key name is the closest thing to a label available without
    /// loading that DLL — and in practice handlers name it after themselves
    /// ("7-Zip", "TortoiseGit"), which is exactly what the menu shows.
    /// </summary>
    private ShellHandler? ReadComHandler(RegistryKey root, RegistryKey container, string name, string scope) {
        using var key = OpenSub(container, name);
        if (key is null) {
            return null;
        }

        string clsid = (key.GetValue(string.Empty) as string)?.Trim() ?? string.Empty;
        if (!IsClsid(clsid)) {
            // Some handlers put the CLSID in the key name and a caption in
            // the default value — the reverse of the usual layout.
            clsid = IsClsid(name) ? name : string.Empty;
        }

        string server = clsid.Length > 0 ? ServerFor(root, clsid) : string.Empty;
        string app = VersionName(server);

        // The key name is the identity where there is one — it is what the
        // handler will most likely draw. Where the key *is* the CLSID there
        // is nothing to match a menu row against, so the CLSID becomes the
        // key: unique, stable, and honest about being unmatchable.
        string entryKey = IsClsid(name) ? name : ShellEntryKey.Normalize(name);
        string title = IsClsid(name) && app.Length > 0 ? app : ShellEntryKey.Normalize(name);

        return new ShellHandler {
            Key = entryKey,
            Title = title,
            AppName = app,
            Scopes = new[] { scope },
            Kind = ShellHandlerKind.ContextMenuHandler,
            IsSystem = server.Length > 0 && IsSystemFile(server),
        };
    }


    // --- Naming the application -----------------------------------------

    /// <summary>Full path of the DLL or EXE implementing a CLSID, or empty.</summary>
    private string ServerFor(RegistryKey root, string clsid) {
        if (_servers.TryGetValue(clsid, out string? cached)) {
            return cached;
        }

        string result = string.Empty;
        foreach (string server in new[] { "InprocServer32", "LocalServer32" }) {
            using var key = OpenSub(root, $@"CLSID\{clsid}\{server}");
            if (key?.GetValue(string.Empty) is string path && path.Length > 0) {
                result = ResolveFile(path);
                break;
            }
        }

        _servers[clsid] = result;

        return result;
    }

    /// <summary>
    /// The executable a verb runs. Empty when the verb has no command line —
    /// which is itself the signal that Windows implements it internally.
    /// </summary>
    private static string ExecutableFor(RegistryKey verbKey) {
        using var command = verbKey.OpenSubKey("command");
        if (command?.GetValue(string.Empty) is string line && line.Length > 0) {
            return ResolveFile(line);
        }

        return string.Empty;
    }

    /// <summary>
    /// Digs the actual file out of whatever a registry value happens to
    /// hold: a bare DLL path, a quoted command line with arguments, an icon
    /// reference with a ",&lt;index&gt;" on the end, or just "cmd.exe".
    ///
    /// <para>
    /// The order matters. Splitting on the first space unconditionally is
    /// the obvious approach and it is wrong: every COM server under
    /// <c>Program Files</c> would come back as <c>C:\Program</c>. So an
    /// unquoted value that already names a real file is taken whole, and
    /// only one that does not gets taken apart.
    /// </para>
    /// </summary>
    private static string ResolveFile(string value) {
        string path = Expand(value);
        if (path.Length == 0) {
            return string.Empty;
        }

        if (path.StartsWith('"')) {
            int close = path.IndexOf('"', 1);
            path = close > 1 ? path[1..close] : path.Trim('"');
        } else if (!File.Exists(path)) {
            int space = path.IndexOf(' ');
            if (space > 0) {
                path = path[..space];
            }
            // An icon index, not a drive letter's colon-backslash.
            int comma = path.LastIndexOf(',');
            if (comma > 2) {
                path = path[..comma];
            }
        }

        // "cmd.exe", "powershell.exe" — Windows' own verbs name their
        // executable and leave the finding to the shell's search path.
        // Following it here is what makes them recognisable as system
        // entries rather than as unknown third-party ones.
        if (path.Length > 0 && !Path.IsPathRooted(path)) {
            foreach (string dir in SearchPath()) {
                string candidate = Path.Combine(dir, path);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
        }

        return path;
    }

    private static string VersionName(string path) {
        try {
            if (path.Length == 0 || !File.Exists(path)) {
                return string.Empty;
            }

            var info = FileVersionInfo.GetVersionInfo(path);

            // ProductName first: it is the name a user recognises ("7-Zip"),
            // where FileDescription is often the component ("7-Zip Shell
            // Extension"). Falls through to the file name for stripped
            // binaries, which is still better than nothing.
            return First(info.ProductName, info.FileDescription, info.CompanyName)
                ?? Path.GetFileNameWithoutExtension(path);
        } catch (Exception) {
            // A path we cannot read is a row without an owner, never a
            // failed scan: one broken handler must not cost the table.
            return string.Empty;
        }
    }

    /// <summary>
    /// Whether a binary belongs to Windows itself. The path test is the
    /// reliable one — a component can be signed by anyone but only Windows
    /// installs into <c>%SystemRoot%</c> — with the company name as backup
    /// for the handful that live under Program Files (Defender).
    /// </summary>
    private static bool IsSystemFile(string path) {
        try {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (windows.Length > 0 && path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            if (!File.Exists(path)) {
                return false;
            }

            string? company = FileVersionInfo.GetVersionInfo(path).CompanyName;

            return company is not null && company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>
    /// Where an unqualified executable name is looked for: System32 first
    /// (that is where the verbs in question live), then whatever PATH says.
    /// Read once — the environment does not change under a running process.
    /// </summary>
    private static IReadOnlyList<string> SearchPath() {
        if (_searchPath is not null) {
            return _searchPath;
        }

        var dirs = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.System) };
        try {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';')) {
                string trimmed = dir.Trim().Trim('"');
                if (trimmed.Length > 0) {
                    dirs.Add(trimmed);
                }
            }
        } catch (Exception) {
            // A malformed PATH costs the fallback, not the scan.
        }

        _searchPath = dirs;

        return dirs;
    }

    private static string Expand(string path) {
        try {
            return Environment.ExpandEnvironmentVariables(path.Trim());
        } catch (Exception) {
            return path.Trim();
        }
    }

    private static string? First(params string?[] candidates) {
        foreach (string? candidate in candidates) {
            if (!string.IsNullOrWhiteSpace(candidate)) {
                return candidate.Trim();
            }
        }

        return null;
    }


    // --- Registry plumbing ----------------------------------------------

    /// <summary>
    /// Registry labels come in two forms: plain text, and the
    /// "@shell32.dll,-8506" indirection Windows uses for its own localised
    /// strings. <c>SHLoadIndirectString</c> is the documented way to read
    /// the second — the same call the shell itself makes — and it keeps the
    /// system rows from all being listed under their key names.
    /// </summary>
    private static string? Indirect(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (!value.StartsWith('@')) {
            return value;
        }

        try {
            var buffer = new StringBuilder(512);
            if (SHLoadIndirectString(value, buffer, buffer.Capacity, IntPtr.Zero) == 0) {
                string text = buffer.ToString();

                return text.Length > 0 ? text : null;
            }
        } catch (Exception) {
            // A resource that will not load is a row named after its key.
        }

        return null;
    }


    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(
        string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    private static bool IsClsid(string value) {
        return value.Length > 2 && value[0] == '{' && value[^1] == '}';
    }

    private RegistryKey? OpenRead(RegistryHive hive, string path) {
        try {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);

            return baseKey.OpenSubKey(path, writable: false);
        } catch (Exception ex) {
            _log.Warn($"Shell handlers: cannot open {hive}\\{path}: {ex.Message}");

            return null;
        }
    }

    private static RegistryKey? OpenSub(RegistryKey parent, string path) {
        try {
            return parent.OpenSubKey(path, writable: false);
        } catch (Exception) {
            // A key an ACL keeps us out of is one row missing from a table,
            // not a reason to abandon the scan.
            return null;
        }
    }
}
