using System.Diagnostics;
using Wander.Core.Shell;

namespace Wander.Platform.Windows.Shell;

public sealed class ShellLauncher : IShellLauncher {
    public void Open(string path) {
        var psi = new ProcessStartInfo {
            FileName = path,
            UseShellExecute = true,
        };
        Process.Start(psi);
    }
}
