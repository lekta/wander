namespace Wander.Platform.Windows.Logging;

/// <summary>
/// The startup sweep of the log folders: the newest few hundred session
/// logs stay, the newest few crash bundles stay, the rest go. Which names
/// those are is <see cref="Wander.Core.Logging.LogRetention.Select"/>;
/// what is here is the listing, the masks and the deletion. Named apart
/// from the Core rule on purpose - one name in two namespaces makes every
/// file that needs both, the composition root first, ambiguous.
/// </summary>
/// <remarks>
/// Only what Wander itself names is touched. A file dropped into the
/// folder by hand or by a trace switch (layout-trace.log) matches no mask
/// and survives, and so does the log this session is writing.
/// </remarks>
public static class LogFolders {
    /// <summary>Session logs and status journals, counted together.</summary>
    public const int KeepLogs = 200;

    /// <summary>Crash bundles - bigger files, and each one is a report someone may still want.</summary>
    public const int KeepCrashes = 20;

    private static readonly string[] _logMasks = { "session-*.log", "journal-*.txt" };
    private static readonly string[] _crashMasks = { "crash-*.zip" };


    /// <summary>
    /// Removes the excess from both folders. Never throws: a file held
    /// open, a folder that is not there, a profile that will not allow the
    /// delete - the next startup tries again.
    /// </summary>
    /// <param name="logsFolder">Where session logs and journals live.</param>
    /// <param name="crashesFolder">Where crash bundles live.</param>
    /// <param name="currentLogFile">
    /// The file this session is writing (<c>ILogFile.FilePath</c>), which is
    /// never removed - it is open, and it is the one log that matters now.
    /// </param>
    /// <returns>How many logs and how many crash bundles were removed.</returns>
    public static (int Logs, int Crashes) Sweep(string logsFolder, string crashesFolder, string? currentLogFile) {
        int logs = SweepFolder(logsFolder, _logMasks, KeepLogs, currentLogFile);
        int crashes = SweepFolder(crashesFolder, _crashMasks, KeepCrashes, null);

        return (logs, crashes);
    }


    private static int SweepFolder(string folder, string[] masks, int keep, string? spare) {
        var listed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (string mask in masks) {
            try {
                foreach (string path in Directory.EnumerateFiles(folder, mask)) {
                    if (spare is not null && string.Equals(path, spare, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    listed[path] = File.GetLastWriteTimeUtc(path);
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                return 0;
            }
        }

        var files = listed.Select(pair => (Name: pair.Key, WrittenUtc: pair.Value)).ToList();
        int removed = 0;
        foreach (string path in Core.Logging.LogRetention.Select(files, keep)) {
            try {
                File.Delete(path);
                removed++;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                // In use, or gone since the listing.
            }
        }

        return removed;
    }
}
