using System.Windows;
using Wander.App.Diagnostics;
using Wander.App.Dialogs;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core;
using Wander.Core.Localization;
using Wander.Core.Logging;
using Wander.Core.Persistence;
using Wander.Platform.Windows;

namespace Wander.App;

public partial class App : Application {
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
        HookCrashLogging();
        base.OnStartup(e);
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
                Shutdown(1);

                return;
            }

            CrashReporter.Offer(args.Exception, fatal: false);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            // Process is going down — flush what we know while we still can.
            var ex = args.ExceptionObject as Exception;
            log.Error($"Fatal unhandled exception (terminating={args.IsTerminating})", ex);
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
