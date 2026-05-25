namespace Wander.Core.Shell;

public interface IShellLauncher {
    void Open(string path);

    /// <summary>Open the OS-native Properties dialog for the given file or folder.</summary>
    void ShowProperties(string path);
}
