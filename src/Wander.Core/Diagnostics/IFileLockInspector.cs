namespace Wander.Core.Diagnostics;

/// <summary>
/// Discovers which processes currently hold a file open, so error messages can
/// be specific ("file.txt is open in Word") instead of a raw IOException.
/// </summary>
public interface IFileLockInspector {
    /// <summary>
    /// Returns the processes that have <paramref name="filePath"/> open -
    /// for a folder, any of the files inside it. Returns an empty list when
    /// nothing is locked, the path is gone, or the platform doesn't expose
    /// this information.
    /// </summary>
    IReadOnlyList<FileLockInfo> WhoIsLocking(string filePath);
}
