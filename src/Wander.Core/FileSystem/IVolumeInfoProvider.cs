namespace Wander.Core.FileSystem;

/// <summary>What kind of volume this is, in the terms the user thinks in.</summary>
public enum VolumeKind {
    Unknown,
    Fixed,
    Removable,
    Network,
    Optical,
    Ram,
}


/// <summary>
/// A volume, described. Sizes are bytes; <paramref name="TotalBytes"/> is
/// zero when the drive is not ready (an empty card reader, a disconnected
/// share), which is the case the pane has to be able to say out loud.
/// </summary>
public sealed record VolumeInfo(
    string Root,
    string Label,
    string FileSystem,
    VolumeKind Kind,
    long TotalBytes,
    long FreeBytes,
    bool IsReady) {

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    /// <summary>Share of the volume in use, 0…1. Zero when the size is unknown.</summary>
    public double UsedFraction => TotalBytes > 0 ? Math.Clamp((double)UsedBytes / TotalBytes, 0, 1) : 0;
}


/// <summary>
/// Reads what the operating system knows about a volume — label, file
/// system, capacity. Separate from <see cref="IFileSystem"/> because it
/// answers about the medium rather than about the tree on it, and because
/// the answer costs a system call that the listing path must never make.
/// </summary>
public interface IVolumeInfoProvider {
    /// <summary>
    /// Describes the volume <paramref name="path"/> sits on, or null when
    /// the path names no volume at all (a shell namespace, a UNC path with
    /// no drive behind it).
    /// </summary>
    VolumeInfo? Describe(string path);

    /// <summary>
    /// True when <paramref name="path"/> is the root of a volume — the one
    /// place where "what is this drive" is the question being asked, rather
    /// than "what is in this folder".
    /// </summary>
    bool IsVolumeRoot(string path);
}
