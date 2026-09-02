using System.IO;
using Wander.App;
using Wander.Core.Persistence;
using Wander.Harness.Host;
using Wander.Harness.Sandbox;

namespace Wander.Harness;

/// <summary>
/// Entry point. Three commands:
/// <list type="bullet">
///   <item><c>sandbox &lt;dir&gt; [--profiles a,b] [--photos N] [--big N] [--raw-mb N]</c> - generate test data.</item>
///   <item><c>run &lt;scenario.json&gt; [--sandbox dir] [--out dir]</c> - drive the app through a scenario.</item>
///   <item><c>selfcheck [--dir dir]</c> - generate a tiny sandbox and verify the generators against Core's readers.</item>
/// </list>
/// Exit codes: 0 ok, 2 scenario failed, 64 usage, 70 crashed.
/// </summary>
public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        if (args.Length == 0) {
            return Usage();
        }

        try {
            return args[0].ToLowerInvariant() switch {
                "sandbox" => SandboxCommand(args),
                "run" => RunCommand(args),
                "selfcheck" => SelfCheck.Run(new Options(args)),
                _ => Usage(),
            };
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);

            return 70;
        }
    }


    private static int SandboxCommand(string[] args) {
        var options = new Options(args);
        string? dir = options.Positional(1);
        if (dir is null) {
            return Usage();
        }

        var profiles = (options.Value("profiles") ?? "photos,raw,big,deep,names").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var built = SandboxBuilder.Build(Path.GetFullPath(dir), profiles, SandboxOptions.From(options));
        Console.WriteLine($"sandbox: {built.Root}");
        foreach (var line in built.Summary) {
            Console.WriteLine("  " + line);
        }

        return 0;
    }

    private static int RunCommand(string[] args) {
        var options = new Options(args);
        string? scenarioPath = options.Positional(1);
        if (scenarioPath is null) {
            return Usage();
        }

        var scenario = Scenario.Load(Path.GetFullPath(scenarioPath));
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string outDir = Path.GetFullPath(options.Value("out") ?? Path.Combine("artifacts", $"{scenario.Name}-{stamp}"));
        Directory.CreateDirectory(outDir);

        string sandboxRoot = Path.GetFullPath(options.Value("sandbox")
            ?? Path.Combine(Path.GetTempPath(), "wander-sandbox", scenario.Sandbox));
        if (options.Has("rebuild")) {
            // Emptied first, or --rebuild only writes over what is there:
            // a fixture that has since been dropped, or a folder a failed
            // run left behind, would live in every sandbox for ever and
            // quietly turn up in the next screenshot.
            SandboxBuilder.Remove(sandboxRoot);
        }
        if (!Directory.Exists(sandboxRoot)) {
            var built = SandboxBuilder.Build(sandboxRoot, scenario.Profiles, SandboxOptions.From(options));
            Console.WriteLine($"sandbox built: {built.Root}");
            foreach (string line in built.Summary) {
                Console.WriteLine("  " + line);
            }
        }

        // Everything the app writes for itself lands inside the run folder:
        // AppPaths honours the variable, the process is fresh, nothing of the
        // machine's own state.json or caches is touched.
        string dataDir = Path.Combine(outDir, "data");
        Environment.SetEnvironmentVariable(AppPaths.EnvironmentVariable, dataDir);
        SeedState(scenario, dataDir);
        Wander.App.App.Headless = true;

        var context = new RunContext(scenario, sandboxRoot, outDir, dataDir);
        var app = new HarnessApp(context);
        int code = app.Run();
        Console.WriteLine($"exit {code}: {Path.Combine(outDir, "report.md")}");

        return code;
    }

    /// <summary>
    /// Puts a saved state.json from an earlier version into the run's data
    /// directory, so the app comes up on it. Missing is a hard stop rather
    /// than a warning: a scenario that asked to start on 0.2.1 and silently
    /// started on nothing would report a pass about a test that never ran.
    /// </summary>
    private static void SeedState(Scenario scenario, string dataDir) {
        if (string.IsNullOrEmpty(scenario.State)) {
            return;
        }

        string? fixtures = FixtureLibrary.Discover().Root;
        string source = Path.Combine(fixtures ?? "", "state", scenario.State + ".json");
        if (fixtures is null || !File.Exists(source)) {
            throw new FileNotFoundException($"scenario wants state '{scenario.State}', no such file", source);
        }

        Directory.CreateDirectory(dataDir);
        File.Copy(source, Path.Combine(dataDir, "state.json"), overwrite: true);
        Console.WriteLine($"state: {source}");
    }

    private static int Usage() {
        Console.Error.WriteLine(
            "usage:\n" +
            "  Wander.Harness sandbox <dir> [--profiles photos,raw,big,deep,names] [--photos N] [--big N] [--raw-mb N] [--raw N]\n" +
            "  Wander.Harness run <scenario.json> [--sandbox <dir>] [--out <dir>] [--rebuild]\n" +
            "  Wander.Harness selfcheck [--dir <dir>]");

        return 64;
    }
}


/// <summary>Positional arguments plus <c>--key value</c> / <c>--flag</c>.</summary>
public sealed class Options {
    private readonly List<string> _positional = new();
    private readonly Dictionary<string, string?> _named = new(StringComparer.OrdinalIgnoreCase);


    public Options(string[] args) {
        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                _positional.Add(arg);
                continue;
            }

            string key = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                _named[key] = args[++i];
            } else {
                _named[key] = null;
            }
        }
    }


    public string? Positional(int index) {
        return index < _positional.Count ? _positional[index] : null;
    }

    public string? Value(string key) {
        return _named.TryGetValue(key, out string? value) ? value : null;
    }

    public bool Has(string key) {
        return _named.ContainsKey(key);
    }

    public int Int(string key, int fallback) {
        return int.TryParse(Value(key), out int value) ? value : fallback;
    }
}
