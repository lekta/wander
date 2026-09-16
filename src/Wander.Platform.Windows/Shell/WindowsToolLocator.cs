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
    private readonly ConcurrentDictionary<string, string?> _found = new(StringComparer.OrdinalIgnoreCase);


    public string? Find(string tool) {
        return _found.GetOrAdd(tool, Search);
    }

    public void Refresh() {
        _found.Clear();
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
