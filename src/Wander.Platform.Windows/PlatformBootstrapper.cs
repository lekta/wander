using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Shell;
using Wander.Platform.Windows.FileSystem;
using Wander.Platform.Windows.Shell;

namespace Wander.Platform.Windows;

public static class PlatformBootstrapper {
    public static void RegisterDefaults() {
        ServiceLocator.Register<IFileSystem>(new SystemIOFileSystem());
        ServiceLocator.Register<IShellLauncher>(new ShellLauncher());
    }
}
