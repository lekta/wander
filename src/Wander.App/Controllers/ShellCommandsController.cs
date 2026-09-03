using System.IO;
using System.Windows;
using Wander.App.Resources;
using Wander.Core;
using Wander.Core.Logging;
using Wander.Core.Persistence;
using Wander.Core.Shell;

namespace Wander.App.Controllers;

/// <summary>
/// The verbs that hand something to the operating system and are finished:
/// open a file's Properties, open it with another app, open a terminal here,
/// open a URL or the session log, put paths or names on the clipboard.
///
/// <para>
/// None of them decides <em>what</em> to act on. The target is a parameter,
/// because choosing it means knowing the selection and where the user is
/// standing, and that stays with the view model. What is here is the other
/// half: call the shell, and turn whatever goes wrong into a line the user
/// can read instead of an exception.
/// </para>
/// </summary>
public sealed class ShellCommandsController {
    private readonly IShellLauncher _shell;
    private readonly ILogger _log;


    public ShellCommandsController(IShellLauncher shell, ILogger log) {
        _shell = shell;
        _log = log;
    }


    /// <summary>Something to tell the user — already localised.</summary>
    public event EventHandler<string>? StatusReported;


    /// <summary>Opens a link in the user's browser.</summary>
    public void OpenUrl(string url) {
        try {
            _shell.Open(url);
        } catch (Exception ex) {
            Report(Strings.StatusBrowserFailed, ex.Message);
        }
    }


    /// <summary>
    /// Opens this session's log file in whatever the system associates with
    /// it. Says so plainly when there is no logging or no file yet, rather
    /// than opening nothing and looking broken.
    /// </summary>
    public void OpenLogFile() {
        if (ServiceLocator.TryGet<ILogFile>() is not { } logFile) {
            StatusReported?.Invoke(this, Strings.StatusNoLogging);

            return;
        }

        string path = logFile.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            StatusReported?.Invoke(this, Strings.StatusNoLogFile);

            return;
        }

        try {
            _shell.Open(path);
        } catch (Exception ex) {
            Report(Strings.StatusOpenLogFailed, ex.Message);
        }
    }


    /// <summary>
    /// Writes what the status bar has said this session to a text file and
    /// opens it in whatever reads text on this machine.
    ///
    /// <para>
    /// A file rather than a window of our own: the journal is text, the
    /// system already has something that shows text well, and a viewer
    /// built here would be a second one to maintain for the sake of a few
    /// hundred lines. Rewritten on every open, beside the session log, so
    /// the two are found in the same place.
    /// </para>
    /// </summary>
    public void OpenJournal(ActionJournal journal) {
        string path = Path.Combine(AppPaths.Logs, $"journal-{Environment.ProcessId}.txt");
        try {
            Directory.CreateDirectory(AppPaths.Logs);
            File.WriteAllText(path, journal.Render(), System.Text.Encoding.UTF8);
        } catch (Exception ex) {
            _log.Error($"Journal write failed: {path}", ex);
            Report(Strings.StatusJournalFailed, ex.Message);

            return;
        }

        try {
            _shell.Open(path);
        } catch (Exception ex) {
            Report(Strings.StatusJournalFailed, ex.Message);
        }
    }


    /// <summary>The system's own Properties dialog for one path.</summary>
    public void ShowProperties(string path) {
        try {
            _shell.ShowProperties(path);
        } catch (Exception ex) {
            Report(Strings.StatusPropertiesFailed, ex.Message);
        }
    }


    /// <summary>The system's "Open with" chooser for one file.</summary>
    public void OpenWith(string path) {
        try {
            _shell.OpenWith(path);
        } catch (Exception ex) {
            _log.Error($"Open with failed: {path}", ex);
            Report(Strings.StatusOpenWithFailed, ex.Message);
        }
    }


    /// <summary>A terminal started in the given folder.</summary>
    public void OpenInTerminal(string folder) {
        try {
            _shell.OpenTerminal(folder);
        } catch (Exception ex) {
            _log.Error($"Open terminal failed: {folder}", ex);
            Report(Strings.StatusTerminalFailed, ex.Message);
        }
    }


    /// <summary>
    /// Quoted, one per line — the shape you can paste straight into a shell,
    /// which is what Explorer's "Copy as path" produces too. The status line
    /// gets the bare paths instead: it is there to show <em>what</em> landed
    /// in the clipboard, and quotes only get in the way of reading it.
    /// </summary>
    public void CopyPaths(IReadOnlyList<string> paths) {
        SetText(string.Join(Environment.NewLine, paths.Select(p => $"\"{p}\"")), Summarize(paths));
    }


    /// <summary>Bare names, one per line.</summary>
    public void CopyNames(IReadOnlyList<string> names) {
        if (names.Count == 0) {
            return;
        }

        SetText(string.Join(Environment.NewLine, names), Summarize(names));
    }


    private void SetText(string text, string what) {
        try {
            Clipboard.SetText(text);
            Report(Strings.StatusCopiedToClipboard, what);
        } catch (Exception ex) {
            // The OS clipboard is a shared, lockable resource — another app
            // holding it turns this into a COMException, not a bug in ours.
            _log.Warn($"Clipboard copy failed: {ex.Message}");
            Report(Strings.StatusClipboardBusy, ex.Message);
        }
    }


    /// <summary>
    /// One line naming what was copied. The status bar trims to its width,
    /// so a long multi-select degrades to "first, second, …" on its own —
    /// but the first entries stay readable, which is the point.
    /// </summary>
    private static string Summarize(IReadOnlyList<string> items) {
        return string.Join(", ", items);
    }


    private void Report(string format, string argument) {
        StatusReported?.Invoke(this, string.Format(format, argument));
    }
}
