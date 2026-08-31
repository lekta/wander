using System.Windows;
using System.Windows.Threading;
using Wander.App.Diagnostics;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core;
using Wander.Core.Localization;
using Wander.Core.Logging;
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


    protected override void OnStartup(StartupEventArgs e) {
        IsSmokeRun = e.Args.Any(arg => string.Equals(arg, "--smoke", StringComparison.OrdinalIgnoreCase));
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
        var log = ServiceLocator.IsRegistered<ILogger>() ? ServiceLocator.Get<ILogger>() : NullLogger.Instance;

        DispatcherUnhandledException += (_, args) => {
            log.Error("Unhandled dispatcher exception", args.Exception);
            // No dialog under --smoke: nobody is there to dismiss it, and a
            // check that hangs on a message box is worse than one that fails.
            // The exit code carries the news instead.
            if (IsSmokeRun) {
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
            if (ex is not null && !IsSmokeRun) {
                CrashReporter.Offer(ex, fatal: true);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) => {
            log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }
}
