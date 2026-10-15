using Wander.Core.FileSystem;
using Wander.Core.Localization;
using Wander.Platform.Windows.FileSystem;

namespace Wander.Platform.Windows.Shell;

/// <summary>
/// The computer's listing (<see cref="Core.Shell.ShellPaths.Computer"/>):
/// one row per ready drive, in letter order, as the drives panel has them.
/// A row is the drive's root as a folder - it opens, previews and takes the
/// folder verbs like the panel's row - named and measured the way Explorer's
/// "This PC" shows it: "Data (D:)", the total as its size, the volume for
/// the bar (<see cref="FileSystemEntry.Volume"/>).
///
/// <para>
/// Not through <c>IShellItem</c>: the shell's own folder would add phones,
/// cameras and network places this listing does not open yet (PLAN Y4),
/// and <see cref="DriveInfo"/> is what the panel and the volume block of
/// the preview already read.
/// </para>
/// </summary>
internal sealed class ComputerDrives {
    private readonly WindowsVolumeInfo _volumes = new();


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
