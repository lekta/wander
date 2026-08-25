namespace Wander.Core.Shell;

public interface IShellLauncher {
    void Open(string path);

    /// <summary>Open the OS-native Properties dialog for the given file or folder.</summary>
    void ShowProperties(string path);

    /// <summary>
    /// Show the OS "Open with" picker for a file. Distinct from
    /// <see cref="Open"/>, which runs the registered default handler.
    /// </summary>
    void OpenWith(string path);

    /// <summary>
    /// Open a terminal whose working directory is <paramref name="folderPath"/>.
    /// Implementations pick whatever terminal the machine actually has.
    /// </summary>
    void OpenTerminal(string folderPath);
}
