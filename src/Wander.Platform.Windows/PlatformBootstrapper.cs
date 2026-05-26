using Wander.Core;
using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Persistence;
using Wander.Core.Shell;
using Wander.Platform.Windows.Diagnostics;
using Wander.Platform.Windows.FileSystem;
using Wander.Platform.Windows.Icons;
using Wander.Platform.Windows.Persistence;
using Wander.Platform.Windows.Shell;

namespace Wander.Platform.Windows;

public static class PlatformBootstrapper {
    public static void RegisterDefaults() {
        ServiceLocator.Register<IFileSystem>(new SystemIOFileSystem());
        ServiceLocator.Register<IShellLauncher>(new ShellLauncher());
        ServiceLocator.Register<IIconProvider>(new SystemIconProvider());
        ServiceLocator.Register<IAppStateStore>(new JsonAppStateStore());
        ServiceLocator.Register<IFileLockInspector>(new RestartManagerLockInspector());
        ServiceLocator.Register<IShortcutService>(new ShellShortcutService());
    }
}
