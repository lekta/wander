using System.IO;

namespace Wander.Harness.Sandbox;

/// <summary>
/// The handful of files the generators cannot make: real encoders we do
/// not have (mp3, flac, m4a, video) and formats where hand-assembly buys
/// nothing (a Word 97 .doc). They live in <c>tests\Fixtures</c> and the
/// media / docs profiles copy in whatever is there.
///
/// <para>
/// Looked up by extension rather than by name: the point of a fixture is
/// its format, the file behind it is whatever somebody found, and a
/// generator that hard-codes <c>wilhelm_scream.mp3</c> breaks the day that
/// file is replaced with a better one. A format nobody has supplied yet is
/// a note in the summary, not a failure - a scenario that needs it says so
/// with its own assertion.
/// </para>
/// </summary>
public sealed class FixtureLibrary {
    private readonly Dictionary<string, string> _byExtension = new(StringComparer.OrdinalIgnoreCase);


    private FixtureLibrary(string? root) {
        Root = root;
        if (root is null) {
            return;
        }

        // First file wins, so the order is the order on disk; two mp3s in
        // the folder is a fixture set that needs tidying, not a decision
        // this class should be making.
        foreach (string path in Directory.EnumerateFiles(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
            string extension = Path.GetExtension(path);
            if (extension.Length > 0) {
                _byExtension.TryAdd(extension, path);
            }
        }
    }


    /// <summary>The fixtures folder, or null when this build is running from somewhere that has no repository above it.</summary>
    public string? Root { get; }


    /// <summary>
    /// Finds <c>tests\Fixtures</c> by walking up from the binaries and then
    /// from the working directory. Two starting points because the harness
    /// is run both ways: from the repository root by hand and by
    /// <c>check.bat</c>, and from its own output folder by a debugger.
    /// </summary>
    public static FixtureLibrary Discover() {
        return new FixtureLibrary(
            Search(AppContext.BaseDirectory) ?? Search(Directory.GetCurrentDirectory()));
    }


    /// <summary>The fixture for an extension (with its dot), or null when nobody has supplied one.</summary>
    public string? Find(string extension) {
        return _byExtension.TryGetValue(extension, out string? path) ? path : null;
    }

    /// <summary>
    /// Copies one fixture per extension into <paramref name="targetDir"/>,
    /// keeping its own name, and notes what was and was not there.
    /// </summary>
    public void CopyEach(SandboxContext context, string targetDir, params string[] extensions) {
        var missing = new List<string>();
        int copied = 0;
        foreach (string extension in extensions) {
            string? source = Find(extension);
            if (source is null) {
                missing.Add(extension);

                continue;
            }

            string target = Path.Combine(targetDir, Path.GetFileName(source));
            File.Copy(source, target, overwrite: true);
            context.NoteFixture(target);
            copied++;
        }

        context.Note(Root is null
            ? $"fixtures: tests/Fixtures not found, {extensions.Length} format(s) skipped"
            : $"fixtures: {copied} copied" + (missing.Count == 0 ? "" : $", missing {string.Join(" ", missing)}"));
    }


    /// <summary>
    /// Copies fixtures by name. The by-extension lookup cannot serve the
    /// archives profile: it needs a specific <c>locked.zip</c> and a
    /// specific <c>nested.7z</c>, and "the first .zip in the folder" is
    /// whichever one sorts first.
    /// </summary>
    public void CopyNamed(SandboxContext context, string targetDir, params string[] names) {
        var missing = new List<string>();
        int copied = 0;
        foreach (string name in names) {
            string? source = Root is null ? null : Path.Combine(Root, name);
            if (source is null || !File.Exists(source)) {
                missing.Add(name);

                continue;
            }

            string target = Path.Combine(targetDir, name);
            File.Copy(source, target, overwrite: true);
            context.NoteFixture(target);
            copied++;
        }

        context.Note($"fixtures by name: {copied} copied"
            + (missing.Count == 0 ? "" : $", missing {string.Join(" ", missing)}"));
    }


    private static string? Search(string start) {
        var dir = new DirectoryInfo(start);
        while (dir is not null) {
            string candidate = Path.Combine(dir.FullName, "tests", "Fixtures");
            if (Directory.Exists(candidate)) {
                return candidate;
            }
            dir = dir.Parent;
        }

        return null;
    }
}
