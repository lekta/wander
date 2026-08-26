namespace Wander.Core.Icons;

/// <summary>
/// Limits the user set for thumbnail caching. Passed to
/// <see cref="IIconProvider.ConfigureCache"/> whenever the settings change,
/// so the provider never reads settings itself — Core stays the one that
/// knows what the user asked for.
/// </summary>
/// <param name="MemoryEntries">
/// How many per-file thumbnails to keep in RAM. Small and normal icons are
/// keyed by extension and bounded by how many file types exist; only the
/// large ones are unique per file.
/// </param>
/// <param name="DiskEnabled">
/// Whether thumbnails survive a restart. Off means the memory cache only,
/// and nothing is written to disk at all.
/// </param>
/// <param name="DiskBudgetBytes">
/// Hard ceiling on the on-disk cache. The oldest files are dropped once the
/// folder grows past it.
/// </param>
public sealed record ThumbnailCacheOptions(int MemoryEntries, bool DiskEnabled, long DiskBudgetBytes) {
    /// <summary>What the provider uses before anyone configures it.</summary>
    public static readonly ThumbnailCacheOptions Default = new(512, true, 256L * 1024 * 1024);
}
