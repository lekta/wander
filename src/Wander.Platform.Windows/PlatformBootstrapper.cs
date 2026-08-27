using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Wander.Core;
using Wander.Core.Companions;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Persistence;
using Wander.Core.Shell;
using Wander.Core.Undo;
using Wander.Platform.Windows.Diagnostics;
using Wander.Platform.Windows.FileSystem;
using Wander.Platform.Windows.Icons;
using Wander.Platform.Windows.Logging;
using Wander.Platform.Windows.Persistence;
using Wander.Platform.Windows.Shell;

namespace Wander.Platform.Windows;

public static class PlatformBootstrapper {
    public static void RegisterDefaults() {
        // Logging first so anything below can log during construction if needed.
        var logger = new FileLogger();
        ServiceLocator.Register<ILogger>(logger);
        ServiceLocator.Register<ILogFile>(logger);
        logger.Info($"=== Wander session start ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ===");
        logger.Info($"Log file: {logger.FilePath}");
        // Environment header — makes a lone session log self-sufficient for
        // bug reports (CrashReporter bundles this log as-is).
        logger.Info(
            $"Version {AppVersion()}; {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}); " +
            $"{RuntimeInformation.FrameworkDescription}; culture {CultureInfo.CurrentCulture.Name}/{CultureInfo.CurrentUICulture.Name}; " +
            $"elevated: {IsElevated()}");

        ServiceLocator.Register<IFileSystem>(new SystemIOFileSystem());
        ServiceLocator.Register<IKnownFolders>(new WindowsKnownFolders());
        ServiceLocator.Register<ISystemClipboard>(new WindowsClipboard(logger));
        ServiceLocator.Register<IDirectoryWatcher>(new WindowsDirectoryWatcher(logger));
        ServiceLocator.Register<IShellLauncher>(new ShellLauncher());
        // Thumbnails get a disk tier next to the logs and state.json:
        // %LocalAppData%\Wander	humbs. Limits arrive from the settings
        // once the view model is up; until then the cache stays idle.
        var thumbs = new ThumbnailDiskCache(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Wander",
                "thumbs"),
            logger);
        ServiceLocator.Register<IIconProvider>(new SystemIconProvider(thumbs));
        ServiceLocator.Register<IAppStateStore>(new JsonAppStateStore());
        ServiceLocator.Register<IFileLockInspector>(new RestartManagerLockInspector());
        ServiceLocator.Register<IShortcutService>(new ShellShortcutService());
        ServiceLocator.Register<IShellNamespace>(new WindowsShellNamespace(logger));
        ServiceLocator.Register<IShellContextMenu>(new ShellContextMenu(logger));
        ServiceLocator.Register<IImageMetadataReader>(new MetadataExtractorImageReader());

        // Undo + recycle bin + ops are the single shared instances every
        // caller (VM, drop handlers, future scripting) must reach for.
        ServiceLocator.Register<UndoService>(new UndoService());
        ServiceLocator.Register<OperationTracker>(new OperationTracker());
        ServiceLocator.Register<IRecycleBin>(new ShellRecycleBin(logger));
        ServiceLocator.Register<FileOperationService>(new FileOperationService());

        // Companion ("integrated item") support: the resolver knows which
        // files belong together, the metadata service reads and writes what
        // is inside them.
        ServiceLocator.Register<CompanionResolver>(CompanionResolver.Default);
        ServiceLocator.Register<CompanionMetadataService>(new CompanionMetadataService(
            ServiceLocator.Get<IFileSystem>(), ServiceLocator.Get<UndoService>(), logger));
    }


    private static string AppVersion() {
        var asm = Assembly.GetEntryAssembly();
        return asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm?.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static string IsElevated() {
        try {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ? "yes" : "no";
        } catch {
            return "unknown";
        }
    }
}
