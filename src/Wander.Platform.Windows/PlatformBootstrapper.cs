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
using Wander.Core.Search;
using Wander.Core.Shell;
using Wander.Core.Undo;
using Wander.Platform.Windows.Diagnostics;
using Wander.Platform.Windows.FileSystem;
using Wander.Platform.Windows.Icons;
using Wander.Platform.Windows.Logging;
using Wander.Platform.Windows.Persistence;
using Wander.Platform.Windows.Search;
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
            $"{BuildInfo.Line}; {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}); " +
            $"{RuntimeInformation.FrameworkDescription}; culture {CultureInfo.CurrentCulture.Name}/{CultureInfo.CurrentUICulture.Name}; " +
            $"elevated: {IsElevated()}");

        ServiceLocator.Register<IFileSystem>(new SystemIOFileSystem());
        ServiceLocator.Register<IVolumeInfoProvider>(new WindowsVolumeInfo());
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
        ServiceLocator.Register<IShellHandlerRegistry>(new ShellHandlerRegistry(logger));
        ServiceLocator.Register<IImageMetadataReader>(new MetadataExtractorImageReader());

        // Search inside files. The extractors are tried in this order, and
        // the order is the whole design: the zip-based documents first
        // because Core reads them without leaving the process, the system's
        // own document filters next for the formats nothing else here can
        // open (.doc, .rtf, .pdf where a reader is installed), and
        // "anything that turns out to be text" last, since it is willing to
        // try every file it is offered.
        var searchFs = ServiceLocator.Get<IFileSystem>();
        var searchCache = new ExtractedTextCache();
        ServiceLocator.Register<ExtractedTextCache>(searchCache);
        ServiceLocator.Register<ContentSearchService>(new ContentSearchService(
            searchFs,
            new IContentExtractor[] {
                new ZipDocumentExtractor(searchFs),
                new FilterTextExtractor(logger),
                new PlainTextExtractor(searchFs),
            },
            searchCache,
            logger));

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
            ServiceLocator.Get<IFileSystem>(), ServiceLocator.Get<UndoService>(), logger,
            ServiceLocator.Get<CompanionResolver>()));
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
