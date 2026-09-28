using System.Collections.Concurrent;
using Wander.Core.Actions;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// Looks for <c>&lt;tool&gt;.exe</c> on <c>PATH</c> and in the folders the
/// usual installers put the presets' tools in without always touching
/// <c>PATH</c>. Answers are kept until <see cref="Refresh"/>.
///
/// <para>
/// <c>PATH</c> is read three ways - this process's, the user's and the
/// machine's from the registry - because the process copy is the one from
/// the moment Wander started: a tool installed with winget a minute ago is
/// on the stored <c>PATH</c> and not yet on ours.
/// </para>
/// </summary>
public sealed class WindowsToolLocator : IToolLocator {
    /// <summary>What CreateProcess starts by itself; anything else needs a program to open it.</summary>
    private static readonly HashSet<string> _runnable = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".com", ".bat", ".cmd" };

    private readonly ConcurrentDictionary<string, string?> _found = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _located = new(StringComparer.OrdinalIgnoreCase);


    public string? Find(string tool) {
        return _found.GetOrAdd(tool, Search);
    }

    public string? Locate(string program) {
        return _located.GetOrAdd(program.Trim().Trim('"'), Resolve);
    }

    public void Refresh() {
        _found.Clear();
        _located.Clear();
    }


    private static string? Search(string tool) {
        string file = tool + ".exe";
        foreach (string folder in Folders()) {
            try {
                string candidate = Path.Combine(folder, file);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            } catch (ArgumentException) {
                // A PATH entry with characters no path may have.
            }
        }

        return null;
    }

    /// <summary>
    /// What the runner's CreateProcess makes of a program: a path is the file
    /// itself; a bare name is looked for in System32, in Windows and on
    /// <c>PATH</c> as this process has it. Not in the folders
    /// <see cref="Folders"/> adds, nor on <c>PATH</c> as stored since Wander
    /// started - a program only those have is one the action would not
    /// start, and the field says so rather than the run.
    /// </summary>
    private static string? Resolve(string program) {
        if (program.Length == 0) {
            return null;
        }

        string file = Path.HasExtension(program) ? program : program + ".exe";
        if (!_runnable.Contains(Path.GetExtension(file))) {
            return null;
        }
        if (Path.IsPathRooted(file)) {
            return File.Exists(file) ? file : null;
        }
        // A relative path depends on the folder the action runs in.
        if (file.Contains('\\') || file.Contains('/')) {
            return null;
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var folders = new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
            .Concat(path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(e => e.Trim('"')));
        foreach (string folder in folders) {
            string candidate = Path.Combine(folder, file);
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> Folders() {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine };
        foreach (var target in targets) {
            string path = Environment.GetEnvironmentVariable("PATH", target) ?? string.Empty;
            foreach (string entry in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                string folder = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
                if (seen.Add(folder)) {
                    yield return folder;
                }
            }
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] known = {
            Path.Combine(programFiles, "LibreOffice", "program"),
            Path.Combine(localAppData, "Microsoft", "WinGet", "Links"),
            Path.Combine(programFiles, "ffmpeg", "bin"),
            Path.Combine(localAppData, "Programs", "Pandoc"),
        };
        foreach (string folder in known) {
            if (seen.Add(folder)) {
                yield return folder;
            }
        }
    }
}
