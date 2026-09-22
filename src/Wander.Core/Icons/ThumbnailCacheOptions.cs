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
/// <param name="ThumbnailSide">
/// Pixels of a large thumbnail - <see cref="SideFor"/> the display.
/// </param>
public sealed record ThumbnailCacheOptions(
    int MemoryEntries, bool DiskEnabled, long DiskBudgetBytes, int ThumbnailSide = ThumbnailCacheOptions.BaseSide) {
    /// <summary>The large thumbnail at 100-125 %: the shell's jumbo size.</summary>
    public const int BaseSide = 256;

    /// <summary>What the provider uses before anyone configures it.</summary>
    public static readonly ThumbnailCacheOptions Default = new(512, true, 256L * 1024 * 1024);


    /// <summary>
    /// The side of a large thumbnail for a display scale (1.0 = 96 dpi). A
    /// gallery cell is up to 200 layout units; at 150 % that is 300 device
    /// pixels, and a 256-px thumbnail drawn there is soft (PLAN AM). Steps
    /// rather than the exact size, so the disk cache - keyed by the side -
    /// is not split by every scale setting.
    /// </summary>
    public static int SideFor(double dpiScale) {
        return dpiScale switch {
            <= 1.25 => BaseSide,
            <= 1.75 => 384,
            _ => 512,
        };
    }
}
