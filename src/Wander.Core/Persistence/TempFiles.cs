using System.Security.Cryptography;
using System.Text;

namespace Wander.Core.Persistence;

/// <summary>
/// The scratch folder under <see cref="AppPaths.Tmp"/>: where a copy goes
/// when the user opens something that has no path an application could be
/// given - an entry inside an archive.
///
/// <para>
/// One subfolder per source path, named by a hash of it, so opening the
/// same entry twice reuses the same place instead of accumulating
/// "notes (1).txt", and two entries with the same name from different
/// archives never collide. The copy keeps its own name: that is what the
/// title bar of whatever opens it will show.
/// </para>
///
/// <para>
/// Swept at startup rather than on exit - a crash is exactly when the
/// sweep would be skipped, and a day is long enough that nothing the user
/// still has open is taken away underneath them.
/// </para>
/// </summary>
public static class TempFiles {
    /// <summary>How old a scratch folder has to be before the sweep takes it.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);


    /// <summary>The folder a copy of <paramref name="sourcePath"/> belongs in.</summary>
    public static string FolderFor(string sourcePath) {
        byte[] hash = SHA256.HashData(Encoding.Unicode.GetBytes(sourcePath.ToLowerInvariant()));

        return Path.Combine(AppPaths.Tmp, Convert.ToHexString(hash, 0, 8).ToLowerInvariant());
    }

    /// <summary>
    /// Removes scratch folders older than <see cref="MaxAge"/> from the
    /// folder in use. Never throws: a folder still held open by the
    /// application the user launched simply stays for the next run.
    /// </summary>
    /// <returns>How many folders were removed.</returns>
    public static int Sweep(DateTime nowUtc) {
        return Sweep(nowUtc, AppPaths.Tmp);
    }

    /// <summary>
    /// The same, for one scratch root. The application sweeps both roots
    /// (<see cref="AppPaths.DataTmp"/> and <see cref="AppPaths.SystemTmp"/>)
    /// at startup: the setting that picks between them may have been
    /// switched since the copies were made.
    /// </summary>
    public static int Sweep(DateTime nowUtc, string root) {
        if (!Directory.Exists(root)) {
            return 0;
        }

        int removed = 0;
        foreach (string folder in SafeList(root)) {
            try {
                if (nowUtc - Directory.GetLastWriteTimeUtc(folder) < MaxAge) {
                    continue;
                }
                Directory.Delete(folder, recursive: true);
                removed++;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                // In use, or gone since the listing. Next startup tries again.
            }
        }

        return removed;
    }


    private static IReadOnlyList<string> SafeList(string root) {
        try {
            return Directory.GetDirectories(root);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return Array.Empty<string>();
        }
    }
}
