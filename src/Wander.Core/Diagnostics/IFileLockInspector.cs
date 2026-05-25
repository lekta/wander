namespace Wander.Core.Diagnostics;

/// <summary>
/// Discovers which processes currently hold a file open, so error messages can
/// be specific ("file.txt is open in Word") instead of a raw IOException.
/// </summary>
public interface IFileLockInspector {
    /// <summary>
    /// Returns the processes that have <paramref name="filePath"/> open.
    /// Returns an empty list when the file isn't locked, isn't a file (e.g. a
    /// directory), or the platform doesn't expose this information.
    /// </summary>
    IReadOnlyList<FileLockInfo> WhoIsLocking(string filePath);
}
