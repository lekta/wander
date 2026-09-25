namespace Wander.Site;

/// <summary>
/// Builds the site: the landing from <c>docs/site/index.html</c>, a page per
/// guide section from <c>docs/GUIDE.md</c>, the list of versions from the
/// <c>v*</c> tags. <c>--out &lt;dir&gt;</c> is where it goes, artifacts/site by
/// default. Nothing is written unless every structure rule and every link
/// checks out.
/// Exit codes: 0 built, 1 problems found (listed on stderr), 2 usage or no
/// repository.
/// </summary>
public static class Program {
    public static int Main(string[] args) {
        string? root = FindRoot(AppContext.BaseDirectory) ?? FindRoot(Directory.GetCurrentDirectory());
        if (root is null) {
            Console.Error.WriteLine("site: Wander.slnx not found above " + AppContext.BaseDirectory);

            return 2;
        }

        string output;
        if (args.Length == 0) {
            output = Path.Combine(root, "artifacts", "site");
        } else if (args.Length == 2 && args[0] == "--out") {
            output = Path.GetFullPath(args[1]);
        } else {
            Console.Error.WriteLine("usage: Wander.Site [--out <dir>]");

            return 2;
        }

        // Caught rather than left to the runtime: a crashed .NET process exits
        // with a negative code, which `if errorlevel 1` in check.bat misses.
        try {
            var site = new SiteBuilder(root);
            site.Build();
            // One template serves every page, so its mistake would repeat per page.
            var errors = site.Errors.Distinct().ToList();
            if (errors.Count > 0) {
                foreach (string error in errors) {
                    Console.Error.WriteLine("  " + error);
                }
                Console.Error.WriteLine($"site: {errors.Count} problem(s), nothing written");

                return 1;
            }

            site.Write(output);
            // The page to open, not the folder: docs/site/index.html is the
            // template, placeholders and all, and looks broken opened as is.
            Console.WriteLine("site: " + Path.Combine(output, "index.html"));
            foreach (string line in site.Summary()) {
                Console.WriteLine("  " + line);
            }

            return 0;
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
