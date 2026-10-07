using System.Diagnostics;

namespace Wander.Site;

/// <summary>
/// Builds the site: the landing from <c>docs/site/index.html</c>, a page per
/// guide section from <c>docs/GUIDE.md</c>, the list of versions from the
/// <c>v*</c> tags. <c>--out &lt;dir&gt;</c> is where it goes, artifacts/site by
/// default. Nothing is written unless every structure rule and every link
/// checks out - except in the debug build.
///
/// <para>
/// <c>--debug</c>: the build to look at before a push and a tag. Problems
/// are listed and do not stop it, and the places GUIDE.md marks for a
/// screenshot show on the pages. <c>--open</c>: the landing opens in the
/// default browser once written - for a person at the desk, never for a
/// check or a script. <c>--analytics</c>: the deployed build only - the
/// visit counter's script goes on every page (SiteBuilder).
/// </para>
///
/// Exit codes: 0 built, 1 problems found (listed on stderr; the debug build
/// is written all the same), 2 usage or no repository.
/// </summary>
public static class Program {
    public static int Main(string[] args) {
        string? root = FindRoot(AppContext.BaseDirectory) ?? FindRoot(Directory.GetCurrentDirectory());
        if (root is null) {
            Console.Error.WriteLine("site: Wander.slnx not found above " + AppContext.BaseDirectory);

            return 2;
        }

        string? output = null;
        bool debug = false;
        bool open = false;
        bool analytics = false;
        for (int i = 0; i < args.Length; i++) {
            if (args[i] == "--out" && i + 1 < args.Length) {
                output = Path.GetFullPath(args[++i]);
            } else if (args[i] == "--debug") {
                debug = true;
            } else if (args[i] == "--open") {
                open = true;
            } else if (args[i] == "--analytics") {
                analytics = true;
            } else {
                Console.Error.WriteLine("usage: Wander.Site [--out <dir>] [--debug] [--open] [--analytics]");

                return 2;
            }
        }
        output ??= Path.Combine(root, "artifacts", "site");

        // Caught rather than left to the runtime: a crashed .NET process exits
        // with a negative code, which `if errorlevel 1` in check.bat misses.
        try {
            var site = new SiteBuilder(root, debug, analytics);
            site.Build();
            // One template serves every page, so its mistake would repeat per page.
            var errors = site.Errors.Distinct().ToList();
            foreach (string error in errors) {
                Console.Error.WriteLine("  " + error);
            }
            if (errors.Count > 0 && !(debug && site.IsBuilt)) {
                Console.Error.WriteLine($"site: {errors.Count} problem(s), nothing written");

                return 1;
            }

            site.Write(output);
            // The page to open, not the folder: docs/site/index.html is the
            // template, placeholders and all, and looks broken opened as is.
            string landing = Path.Combine(output, "index.html");
            Console.WriteLine("site: " + landing);
            foreach (string line in site.Summary()) {
                Console.WriteLine("  " + line);
            }
            if (errors.Count > 0) {
                Console.Error.WriteLine($"site: {errors.Count} problem(s), written anyway (--debug)");
            }
            if (open) {
                Process.Start(new ProcessStartInfo(landing) { UseShellExecute = true })?.Dispose();
            }

            return errors.Count > 0 ? 1 : 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);

            return 1;
        }
    }


    private static string? FindRoot(string start) {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "Wander.slnx"))) {
                return dir.FullName;
            }
        }

        return null;
    }
}
