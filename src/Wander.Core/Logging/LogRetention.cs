namespace Wander.Core.Logging;

/// <summary>
/// Which log files a startup sweep should delete: everything past the
/// newest <c>keep</c>. The rule lives here, away from the directory
/// listing that feeds it, because it is the half worth a test - the
/// platform side only turns the names back into deletions.
/// </summary>
/// <remarks>
/// A count, not an age, and no setting for it: what filled the folder was
/// 593 files in one release cycle, most of them two-second debug runs, and
/// the only thing anybody wants bounded is how many there are.
/// </remarks>
public static class LogRetention {
    /// <summary>
    /// The names to remove: all but the <paramref name="keep"/> most
    /// recently written. Ties on the timestamp are broken by name, so the
    /// answer does not depend on the order the listing arrived in.
    /// </summary>
    public static IReadOnlyList<string> Select(IReadOnlyList<(string Name, DateTime WrittenUtc)> files, int keep) {
        if (files.Count <= keep) {
            return Array.Empty<string>();
        }

        return files
            .OrderByDescending(f => f.WrittenUtc)
            .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(keep, 0))
            .Select(f => f.Name)
            .ToList();
    }
}
