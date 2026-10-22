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


/// <summary>How full a volume is, as its capacity bar is coloured.</summary>
public enum VolumeFill {
    Normal,
    Filling,
    Full,
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

    /// <summary>
    /// The bar turns amber and then red as the disk fills, as Explorer's
    /// does: the one colour on a drive's row that carries information.
    /// </summary>
    public VolumeFill Fill => UsedFraction switch {
        >= 0.95 => VolumeFill.Full,
        >= 0.85 => VolumeFill.Filling,
        _ => VolumeFill.Normal,
    };


    /// <summary>
    /// "No room" said by a FAT volume that has room for
    /// <paramref name="needed"/> bytes: the folder ran out of entries -
    /// FAT32 keeps 65 534 in one, a long name taking several, FAT16 512 in
    /// its root (PLAN K). Four megabytes are left for the clusters a file
    /// rounds up to.
    /// </summary>
    public bool OutOfEntries(long needed) {
        const long clusterSlack = 4L * 1024 * 1024;

        return FileSystem is "FAT" or "FAT32" && FreeBytes - needed > clusterSlack;
    }


    /// <summary>
    /// The drive as Explorer names it: its label and letter - "Data (D:)" -
    /// or, unlabelled, what it is - "Local Disk (C:)"
    /// (<paramref name="kindWord"/>).
    /// </summary>
    public string DisplayName(string kindWord) {
        string letter = Root.TrimEnd('\\');

        return $"{(Label.Length > 0 ? Label : kindWord)} ({letter})";
    }
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
    /// What kind of volume <paramref name="path"/> sits on, without asking
    /// the drive anything else - no label, no sizes, no "are you ready": a
    /// copy asks it for every file it reads.
    /// </summary>
    VolumeKind KindOf(string path) => Describe(path)?.Kind ?? VolumeKind.Unknown;

    /// <summary>
    /// True when <paramref name="path"/> is the root of a volume — the one
    /// place where "what is this drive" is the question being asked, rather
    /// than "what is in this folder".
    /// </summary>
    bool IsVolumeRoot(string path);
}
