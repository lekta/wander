using System.Windows;
using Wander.Platform.Windows;

namespace Wander.App;

public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        PlatformBootstrapper.RegisterDefaults();
        base.OnStartup(e);
    }
}
