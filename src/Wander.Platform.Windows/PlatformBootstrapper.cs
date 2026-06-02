using Wander.Core;
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

        ServiceLocator.Register<IFileSystem>(new SystemIOFileSystem());
        ServiceLocator.Register<IKnownFolders>(new WindowsKnownFolders());
        ServiceLocator.Register<IShellLauncher>(new ShellLauncher());
        ServiceLocator.Register<IIconProvider>(new SystemIconProvider());
        ServiceLocator.Register<IAppStateStore>(new JsonAppStateStore());
        ServiceLocator.Register<IFileLockInspector>(new RestartManagerLockInspector());
        ServiceLocator.Register<IShortcutService>(new ShellShortcutService());
        ServiceLocator.Register<IShellNamespace>(new WindowsShellNamespace(logger));
        ServiceLocator.Register<IImageMetadataReader>(new MetadataExtractorImageReader());

        // Undo + recycle bin + ops are the single shared instances every
        // caller (VM, drop handlers, future scripting) must reach for.
        ServiceLocator.Register<UndoService>(new UndoService());
        ServiceLocator.Register<OperationTracker>(new OperationTracker());
        ServiceLocator.Register<IRecycleBin>(new ShellRecycleBin(logger));
        ServiceLocator.Register<FileOperationService>(new FileOperationService());
    }
}
