using System.Windows;
using System.Windows.Threading;
using Wander.App.Diagnostics;
using Wander.Core;
using Wander.Core.Logging;
using Wander.Platform.Windows;

namespace Wander.App;

public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        PlatformBootstrapper.RegisterDefaults();
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
            CrashReporter.Offer(args.Exception, fatal: false);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            // Process is going down — flush what we know while we still can.
            var ex = args.ExceptionObject as Exception;
            log.Error($"Fatal unhandled exception (terminating={args.IsTerminating})", ex);
            if (ex is not null) {
                CrashReporter.Offer(ex, fatal: true);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) => {
            log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }
}
