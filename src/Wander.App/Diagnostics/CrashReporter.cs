using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Wander.App.Resources;
using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.Logging;

namespace Wander.App.Diagnostics;

/// <summary>
/// Offers to prepare a crash report when an unhandled exception surfaces.
///
/// <para>
/// Reporting channel: a local zip bundle plus a pre-filled GitHub issue
/// page. Wander ships as a public binary with no backend, so there is no
/// secret it could use to talk to an API directly; the pre-filled issue
/// keeps the user in full control — nothing leaves the machine until they
/// review and submit it themselves. The bundle (crash.txt + session log)
/// contains real file paths from the session, so it is only saved locally
/// and attaching it to the issue is the user's explicit choice.
/// </para>
/// </summary>
public static class CrashReporter {
    /// <summary>Project home — the base every other link is built from.</summary>
    public const string ProjectUrl = "https://github.com/lekta/wander";

    /// <summary>The user guide — what the "Помощь" menu row opens.</summary>
    public const string GuideUrl = ProjectUrl + "/blob/master/docs/GUIDE.md";

    private const string NewIssueUrl = ProjectUrl + "/issues/new";

    /// <summary>Template chooser (bug report / feature request) — used by the in-app "Report an issue" menu.</summary>
    public const string IssueChooserUrl = NewIssueUrl + "/choose";

    /// <summary>Stack excerpt cap for the issue URL — browsers/GitHub reject overlong URLs.</summary>
    private const int MaxStackChars = 1800;

    private static bool _offeredThisSession;


    /// <summary>
    /// Show the offer dialog and, if accepted, save the bundle, reveal it in
    /// Explorer and open the pre-filled issue page. Never throws — this runs
    /// inside exception handlers where a second failure would mask the first.
    /// </summary>
    public static void Offer(Exception ex, bool fatal) {
        // Non-fatal dispatcher faults can arrive in bursts (a broken binding
        // or render callback fires on every frame) — one offer per session
        // is enough; later faults are still logged by the App handlers.
        // A fatal crash always gets the offer: it's the last chance.
        if (!fatal && _offeredThisSession) {
            return;
        }
        _offeredThisSession = true;

        try {
            (bool send, bool includeLog) = ShowOffer(ex, fatal);
            if (!send) {
                return;
            }

            string zipPath = SaveBundle(ex, fatal, includeLog);
            Launch("explorer.exe", $"/select,\"{zipPath}\"");
            Launch(BuildIssueUrl(ex, fatal), null);
        } catch {
            // Crash reporting is best-effort by definition.
        }
    }


    // --- Offer dialog ----------------------------------------------------

    private static (bool Send, bool IncludeLog) ShowOffer(Exception ex, bool fatal) {
        try {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess()) {
                return dispatcher.Invoke(() => ShowOfferDialog(ex, fatal));
            }
            return ShowOfferDialog(ex, fatal);
        } catch {
            // WPF dialog unavailable (crash before Application init or during
            // teardown) — fall back to a bare MessageBox. Without a way to
            // ask about the log separately, leave it out (privacy-safe default).
            var choice = MessageBox.Show(
                string.Format(Strings.CrashFallbackPrompt, ex.GetType().Name, Truncate(ex.Message, 300)),
                Strings.CrashTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Error,
                MessageBoxResult.Yes);
            return (choice == MessageBoxResult.Yes, false);
        }
    }

    private static (bool Send, bool IncludeLog) ShowOfferDialog(Exception ex, bool fatal) {
        var window = new Window {
            Title = Strings.CrashTitle,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true,
            Topmost = fatal,
        };
        try {
            if (Application.Current?.MainWindow is { IsVisible: true } main) {
                window.Owner = main;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowInTaskbar = false;
            }
        } catch {
            // No usable owner — centered on screen is fine.
        }

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(new TextBlock {
            Text = fatal ? Strings.CrashFatal : Strings.CrashNonFatal,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock {
            Text = $"{ex.GetType().Name}: {Truncate(ex.Message, 300)}",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 6, 0, 0),
        });
        stack.Children.Add(new TextBlock {
            Text = Strings.CrashExplain,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });

        var includeLog = new CheckBox {
            Content = new TextBlock {
                Text = Strings.CrashIncludeLog,
                TextWrapping = TextWrapping.Wrap,
            },
            IsChecked = true,
            Margin = new Thickness(0, 10, 0, 0),
        };
        stack.Children.Add(includeLog);

        var buttons = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var report = new Button { Content = Strings.CrashPrepare, Padding = new Thickness(10, 2, 10, 2), IsDefault = true };
        var close = new Button { Content = Strings.CrashClose, Padding = new Thickness(10, 2, 10, 2), IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(report);
        buttons.Children.Add(close);
        stack.Children.Add(buttons);

        window.Content = stack;
        report.Click += (_, _) => window.DialogResult = true;

        bool send = window.ShowDialog() == true;
        return (send, send && includeLog.IsChecked == true);
    }


    // --- Bundle ---------------------------------------------------------

    private static string SaveBundle(Exception ex, bool fatal, bool includeLog) {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wander", "crashes");
        Directory.CreateDirectory(dir);

        string zipPath = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var crashEntry = zip.CreateEntry("crash.txt");
        using (var writer = new StreamWriter(crashEntry.Open())) {
            writer.Write(BuildCrashText(ex, fatal, includeLog));
        }

        // Session log: FileLogger flushes every line and holds the file with
        // shared-read, so a copy taken here contains everything up to the
        // crash itself. Only bundled with the user's explicit consent — the
        // log contains real file paths from the session.
        if (includeLog && ServiceLocator.TryGet<ILogFile>() is { } logFile) {
            string logPath = logFile.FilePath;
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath)) {
                var logEntry = zip.CreateEntry(Path.GetFileName(logPath));
                using var source = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var target = logEntry.Open();
                source.CopyTo(target);
            }
        }

        return zipPath;
    }

    private static string BuildCrashText(Exception ex, bool fatal, bool includeLog) {
        var sb = new StringBuilder();
        sb.AppendLine("Wander crash report");
        sb.AppendLine($"Time:  {DateTime.Now:yyyy-MM-dd HH:mm:ss} local / {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Fatal: {fatal}");
        sb.AppendLine($"Log:   {(includeLog ? "attached" : "excluded by user")}");
        sb.AppendLine(EnvironmentSummary());
        sb.AppendLine();
        sb.AppendLine("--- Exception ---");
        sb.AppendLine(ex.ToString());
        return sb.ToString();
    }


    // --- GitHub issue ----------------------------------------------------

    private static string BuildIssueUrl(Exception ex, bool fatal) {
        string title = $"Crash: {ex.GetType().Name}: {Truncate(ex.Message, 80)}";
        string body =
            "**What happened**\n" +
            "<!-- What were you doing when the error appeared? -->\n\n" +
            "**Environment**\n```\n" + EnvironmentSummary() + "\n```\n\n" +
            $"**Exception** (fatal: {fatal})\n```\n" + Truncate(ex.ToString(), MaxStackChars) + "\n```\n\n" +
            "_A crash bundle (crash.txt + session log) was saved locally by Wander; " +
            "attach the zip here if you are comfortable sharing it — the log contains " +
            "file paths from your session._";
        return $"{NewIssueUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }


    // --- Environment -----------------------------------------------------

    private static string EnvironmentSummary() {
        var sb = new StringBuilder();
        sb.AppendLine($"Version:  {BuildInfo.Line}");
        sb.AppendLine($"Full:     {AppVersion()}");
        sb.AppendLine($"OS:       {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Runtime:  {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Culture:  {CultureInfo.CurrentCulture.Name} / UI {CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine($"Elevated: {IsElevated()}");
        sb.AppendLine($"Uptime:   {Uptime()}");
        sb.Append($"WebView2: {WebView2Version()}");
        return sb.ToString();
    }

    /// <summary>
    /// "0.2.1-beta+&lt;sha&gt;" — the informational version, build metadata and
    /// all. The crash bundle wants every character of it; everything that
    /// wants it readable asks <see cref="BuildInfo.Line"/> instead.
    /// </summary>
    public static string AppVersion() {
        return BuildInfo.InformationalVersion;
    }

    private static string IsElevated() {
        try {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ? "yes" : "no";
        } catch {
            return "unknown";
        }
    }

    private static string Uptime() {
        try {
            TimeSpan t = DateTime.Now - Process.GetCurrentProcess().StartTime;
            return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        } catch {
            return "unknown";
        }
    }

    private static string WebView2Version() {
        try {
            return Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        } catch {
            return "not installed";
        }
    }


    // --- Helpers ---------------------------------------------------------

    private static void Launch(string fileName, string? arguments) {
        var psi = new ProcessStartInfo {
            FileName = fileName,
            UseShellExecute = true,
        };
        if (arguments is not null) {
            psi.Arguments = arguments;
        }
        Process.Start(psi);
    }

    private static string Truncate(string s, int max) {
        return s.Length <= max ? s : s[..max] + "…";
    }
}
