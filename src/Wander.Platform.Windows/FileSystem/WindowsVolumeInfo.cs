using System.IO;
using Wander.Core.FileSystem;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// <see cref="IVolumeInfoProvider"/> over <see cref="DriveInfo"/>.
///
/// <para>
/// Every property here throws on a drive that isn't ready — an empty
/// optical drive, a card reader with no card, a mapped share whose server
/// is asleep — and a listing must not fall over because the user clicked a
/// drive letter. So the whole read is wrapped, and "not ready" comes back
/// as a described volume with zero capacity rather than as an exception or
/// a null.
/// </para>
/// </summary>
public sealed class WindowsVolumeInfo : IVolumeInfoProvider {
    public VolumeInfo? Describe(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        DriveInfo drive;
        string root;
        try {
            root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
            if (root.Length == 0) {
                return null;
            }
            drive = new DriveInfo(root);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            // A shell-namespace sentinel, or a UNC path with no drive
            // letter — nothing DriveInfo can be built from.
            return null;
        }

        var kind = drive.DriveType switch {
            DriveType.Fixed => VolumeKind.Fixed,
            DriveType.Removable => VolumeKind.Removable,
            DriveType.Network => VolumeKind.Network,
            DriveType.CDRom => VolumeKind.Optical,
            DriveType.Ram => VolumeKind.Ram,
            _ => VolumeKind.Unknown,
        };

        try {
            if (!drive.IsReady) {
                return new VolumeInfo(root, "", "", kind, 0, 0, IsReady: false);
            }

            return new VolumeInfo(
                root,
                drive.VolumeLabel ?? "",
                drive.DriveFormat ?? "",
                kind,
                drive.TotalSize,
                // Available, not free: on a volume with quotas the number
                // that matters is what this user may still write.
                drive.AvailableFreeSpace,
                IsReady: true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return new VolumeInfo(root, "", "", kind, 0, 0, IsReady: false);
        }
    }


    public bool IsVolumeRoot(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        try {
            string full = Path.GetFullPath(path);

            return string.Equals(
                full.TrimEnd(Path.DirectorySeparatorChar),
                (Path.GetPathRoot(full) ?? "").TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }
}
