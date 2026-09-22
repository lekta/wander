using System.Windows;
using Wander.App.Diagnostics;
using Wander.App.Dialogs;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.Views;
using Wander.Core;
using Wander.Core.Actions;
using Wander.Core.Localization;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Persistence;
using Wander.Platform.Windows;
using Wander.Platform.Windows.Logging;

namespace Wander.App;

public partial class App : Application {
    /// <summary>
    /// How long the same fault has to wait before it may offer a crash
    /// bundle again. A dispatcher fault repeating on every frame used to
    /// put the dialog up sixty times a second; one report of it is one
    /// report, and the log already says it is still happening.
    /// </summary>
    private static readonly TimeSpan _offerInterval = TimeSpan.FromMinutes(1);

    /// <summary>How long a dying process gives its operations to let go of their files.</summary>
    private static readonly TimeSpan _crashWait = TimeSpan.FromSeconds(3);

    private static string _lastOfferSignature = "";
    private static DateTime _lastOfferUtc = DateTime.MinValue;


    /// <summary>
    /// Started with <c>--smoke</c>: come up, draw a frame, go away, and say
    /// through the exit code whether that worked. It is what
    /// <c>tools\check.bat run</c> asks for — everything the check is really
    /// after (the XAML parses, the resources resolve, the services register,
    /// the window renders) happens with or without anybody looking at it, so
    /// the window stays off-screen and nothing on the desktop moves.
    /// </summary>
    public static bool IsSmokeRun { get; private set; }

    /// <summary>
    /// The window stays off-screen, takes no focus, and neither reads nor
    /// writes its geometry. True for a smoke run and for the test harness,
    /// which sets it before constructing the window.
    /// </summary>
    public static bool Headless { get; internal set; }

    /// <summary>
    /// The application itself is ending the session: a smoke countdown, the
    /// harness, a crash, Windows logging off. WPF closes every window then
    /// with a cancelled close ignored, so the main window must neither ask
    /// nor put the exit off - see <see cref="ShutdownWithoutAsking"/>.
    /// </summary>
    internal static bool IsShuttingDown { get; private set; }


    protected override void OnStartup(StartupEventArgs e) {
        IsSmokeRun = e.Args.Any(arg => string.Equals(arg, "--smoke", StringComparison.OrdinalIgnoreCase));
        // Turned on here, never off. The harness sets it before the
        // application object exists and there is no command line to say so;
        // assigning IsSmokeRun to it cleared that, and the harness window
        // came up on the real desktop and took the focus off whoever was
        // working there.
        Headless |= IsSmokeRun;
        // Where state.json, logs and caches live - decided before the
        // logger opens its file, since the logger is the first thing the
        // bootstrapper builds.
        AppPaths.Resolve(e.Args);
        // Before anything formats a number, and before the thread pool has
        // been handed any work that might: the culture set here is the one
        // background passes inherit.
        NumberFormat.Install();
        PlatformBootstrapper.RegisterDefaults();
        // The string table lives in this assembly, so Core cannot reach it
        // directly. Registering the source here — before anything builds a
        // menu — is what makes ContextMenuCatalog and PathSafety speak
        // Russian instead of returning resource keys.
        ServiceLocator.Register<ITextSource>(new AppTextSource());
        // Every modal question goes through this seam; the harness swaps
        // in a scripted answerer before it builds the view model.
        ServiceLocator.Register<IDialogs>(new WpfDialogs());
        // The compare window, for the conflict window's pairs (PLAN Q5).
        ServiceLocator.Register<Conflict.IPairViewer>(new Views.CompareWindow.Viewer());
        HookCrashLogging();
        // WPF answers an unrefused session end with Shutdown.
        SessionEnding += (_, _) => IsShuttingDown = true;
        WatchWindowsWhenHeadless();
        // Yesterday's scratch copies of archive entries. Swept on the way in
        // rather than on the way out: a crash is precisely when the tidy-up
        // on exit would not have run.
        SweepTempCopies();
        SweepLogs();
        base.OnStartup(e);
    }


    /// <summary>
    /// Keeps a window off the real desktop while <see cref="Headless"/> is
    /// on: parked outside the virtual screen, never activated, not in the
    /// taskbar. Every window the app can open calls this in its constructor,
    /// before it is shown. Relying on <c>CenterOwner</c> is not enough: WPF
    /// centres a dialog on an owner it cannot see by putting it at (0, 0) -
    /// on the desktop of whoever is working there, with the focus - which is
    /// what the progress dialog did on every paste of a harness run
    /// (2026-09-02).
    /// </summary>
    internal static void ParkIfHeadless(Window window) {
        if (!Headless) {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }


    /// <summary>
    /// <see cref="Application.Shutdown(int)"/> for callers that end the
    /// session themselves. Marks it first, so the main window stops the
    /// operations on the spot instead of asking a question whose "no" WPF
    /// would ignore anyway.
    /// </summary>
    internal static void ShutdownWithoutAsking(int exitCode) {
        IsShuttingDown = true;
        Current.Shutdown(exitCode);
    }


    /// <summary>
    /// Stops the operations without waiting for them, from any thread: each
    /// window cancels its token, and the programs the custom actions started
    /// are killed. For an exit that cannot wait on them.
    /// </summary>
    internal static void AbandonOperations() {
        ProgressDialog.CancelAll();
        // TryGet: called from the crash handler too, where a startup that
        // died before the platform registered is exactly the case.
        ServiceLocator.TryGet<IProcessRunner>()?.KillAll();
    }


    /// <summary>
    /// Says which windows and popups actually came up while
    /// <see cref="Headless"/> is on: one line per window as it loads (WARN
    /// when it is on the virtual screen rather than parked), one per
    /// context menu or tooltip (always a WARN - WPF keeps a popup on the
    /// screen whatever its owner's position). The answer to "something
    /// flickered for a frame" that the person at the desk cannot give: one
    /// window, one frame, during six harness runs on 2026-09-16, and no
    /// way to say which. Class handlers, so an unparked window is caught
    /// too - the parked ones are the ones that call ParkIfHeadless. Called
    /// by both hosts that construct windows: the app and the harness.
    /// </summary>
    internal static void WatchWindowsWhenHeadless() {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => {
            if (!Headless || sender is not Window window) {
                return;
            }

            var log = ServiceLocator.Get<ILogger>();
            string line = $"HEADLESS window {window.GetType().Name} at ({window.Left:F0}, {window.Top:F0}), active={window.IsActive}";
            if (window.Left <= -20000 && window.Top <= -20000) {
                log.Info(line);
            } else {
                log.Warn(line + " - ON SCREEN");
            }
        }));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu), System.Windows.Controls.ContextMenu.OpenedEvent,
            new RoutedEventHandler((sender, _) => WarnPopup("context menu", sender)));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ToolTip), System.Windows.Controls.ToolTip.OpenedEvent,
            new RoutedEventHandler((sender, _) => WarnPopup("tooltip", sender)));

        static void WarnPopup(string what, object sender) {
            if (Headless) {
                ServiceLocator.Get<ILogger>().Warn($"HEADLESS {what} opened ({sender.GetType().Name}) - ON SCREEN");
            }
        }
    }


    private static void SweepTempCopies() {
        // Both roots, whichever the setting picks today: the copies may
        // have been made under the other one before it was switched.
        var now = DateTime.UtcNow;
        int removed = TempFiles.Sweep(now, AppPaths.DataTmp) + TempFiles.Sweep(now, AppPaths.SystemTmp);
        if (removed > 0) {
            ServiceLocator.Get<ILogger>().Info($"Temporary copies: {removed} folder(s) swept");
        }
    }


    /// <summary>
    /// Caps how many session logs and crash bundles the data folder keeps.
    /// On the pool: it is a directory listing and a pile of deletes, and
    /// nothing on the way to the first frame needs the answer.
    /// </summary>
    private static void SweepLogs() {
        var log = ServiceLocator.Get<ILogger>();
        string? current = ServiceLocator.TryGet<ILogFile>()?.FilePath;
        _ = Task.Run(() => {
            var (logs, crashes) = LogFolders.Sweep(AppPaths.Logs, AppPaths.Crashes, current);
            if (logs > 0 || crashes > 0) {
                log.Info($"Log retention: removed {logs} logs, {crashes} crash bundles");
            }
        });
    }


    /// <summary>
    /// Whether this fault may raise the crash-report dialog now. The fatal
    /// handler does not ask: that process is going down, and its one report
    /// is the only one there will be.
    /// </summary>
    private static bool ShouldOffer(string message, Exception ex, DateTime nowUtc) {
        string signature = RepeatCollapser.Signature("ERROR", message, ex);
        if (signature == _lastOfferSignature && nowUtc - _lastOfferUtc < _offerInterval) {
            return false;
        }
        _lastOfferSignature = signature;
        _lastOfferUtc = nowUtc;

        return true;
    }


    /// <summary>
    /// The process is going down on an exception: the operations are
    /// stopped and get <see cref="_crashWait"/> to close their files, so a
    /// half-written copy is not left locked and an encoder is not left
    /// running. Blocking, and nothing goes through the dispatcher - it may
    /// be the thing that died.
    /// </summary>
    private static void StopOperationsBeforeDying(ILogger log) {
        try {
            AbandonOperations();
            if (!ServiceLocator.Get<OperationTracker>().WhenIdleAsync(_crashWait).Result) {
                log.Warn($"Crash: operation(s) still running after {_crashWait.TotalSeconds:F0} s");
            }
        } catch (Exception ex) {
            log.Error("Crash: stopping the operations failed", ex);
        }
    }


    /// <summary>
    /// Last-resort exception logging. A file manager dying silently mid-batch
    /// is the worst possible failure mode — at minimum the session log must
    /// record what happened, and recoverable UI-thread faults should not take
    /// the whole process down.
    /// </summary>
    private void HookCrashLogging() {
        var log = ServiceLocator.Get<ILogger>();

        DispatcherUnhandledException += (_, args) => {
            log.Error("Unhandled dispatcher exception", args.Exception);
            // No dialog under --smoke: nobody is there to dismiss it, and a
            // check that hangs on a message box is worse than one that fails.
            // The exit code carries the news instead.
            if (Headless) {
                args.Handled = true;
                StopOperationsBeforeDying(log);
                ShutdownWithoutAsking(1);

                return;
            }

            if (ShouldOffer("Unhandled dispatcher exception", args.Exception, DateTime.UtcNow)) {
                CrashReporter.Offer(args.Exception, fatal: false);
            }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            // Process is going down — flush what we know while we still can.
            var ex = args.ExceptionObject as Exception;
            log.Error($"Fatal unhandled exception (terminating={args.IsTerminating})", ex);
            StopOperationsBeforeDying(log);
            if (ex is not null && !Headless) {
                CrashReporter.Offer(ex, fatal: true);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) => {
            log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }
}
