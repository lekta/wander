using Wander.Core.FileSystem;
using Wander.Core.Localization;
using Wander.Platform.Windows.FileSystem;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// The computer's listing (<see cref="Core.Shell.ShellPaths.Computer"/>):
/// one row per ready drive, in letter order, as the drives panel has them,
/// then the phones and cameras plugged in (PLAN Y4b). A drive's row is its
/// root as a folder - it opens, previews and takes the folder verbs like
/// the panel's row - named and measured the way Explorer's "This PC" shows
/// it: "Data (D:)", the total as its size, the volume for the bar
/// (<see cref="FileSystemEntry.Volume"/>). A device's row is the device:
/// its name, its storages added up for the bar.
///
/// <para>
/// Drives through <see cref="DriveInfo"/>, not <c>IShellItem</c>: it is
/// what the panel and the volume block of the preview already read, and
/// the shell's folder would add network places this listing does not open.
/// The devices are the shell's (<see cref="DeviceFolders"/>).
/// </para>
/// </summary>
internal sealed class ComputerDrives {
    private readonly WindowsVolumeInfo _volumes = new();
    private readonly DeviceFolders? _devices;


    public ComputerDrives(DeviceFolders? devices) {
        _devices = devices;
    }


    public IReadOnlyList<FileSystemEntry> Enumerate(CancellationToken ct) {
        var rows = new List<FileSystemEntry>();
        foreach (var drive in DriveInfo.GetDrives()) {
            ct.ThrowIfCancellationRequested();
            if (_volumes.Describe(drive.Name) is not { IsReady: true } volume) {
                continue;
            }

            rows.Add(new FileSystemEntry(
                Name: volume.DisplayName(KindWord(volume.Kind)),
                FullPath: drive.RootDirectory.FullName,
                Kind: EntryKind.Directory,
                Size: volume.TotalBytes,
                ModifiedUtc: DateTime.MinValue,
                IsHidden: false,
                IsReadOnly: false,
                IsSystem: false,
                LinksToDirectory: false,
                Volume: volume));
        }

        if (_devices is not null) {
            ct.ThrowIfCancellationRequested();
            rows.AddRange(_devices.Devices());
        }

        return rows;
    }


    private static string KindWord(VolumeKind kind) {
        return Text.Get(kind switch {
            VolumeKind.Fixed => "VolumeKindFixed",
            VolumeKind.Removable => "VolumeKindRemovable",
            VolumeKind.Network => "VolumeKindNetwork",
            VolumeKind.Optical => "VolumeKindOptical",
            VolumeKind.Ram => "VolumeKindRam",
            _ => "VolumeKindUnknown",
        });
    }
}
