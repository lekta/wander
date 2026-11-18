using System.Globalization;
using Wander.Core.FileSystem;

namespace Wander.Core.Pins;

/// <summary>A volume as the pins see it: its root now, its serial, whether it is a removable one.</summary>
public sealed record PinVolume(string Root, uint Serial, bool IsRemovable);


/// <summary>
/// How a folder's path is stored in the pins (PLAN, "Pins", step 2). On a
/// removable volume the key is the volume's serial and the path below its
/// root - <c>volume:1A2B3C4D\Photos</c> - so the same flash drive finds its
/// pins under another letter and another drive under the same letter does
/// not; elsewhere the key is the path as it is. A folder a volume is mounted
/// into goes by the root of its host. A key of a volume not plugged in
/// turns back into no path: those pins are kept out of sight, not lost.
/// </summary>
public static class PinKeys {
    /// <summary>The head of a key that goes by a volume's serial: no path starts with it.</summary>
    public const string VolumePrefix = "volume:";

    private const int SerialLength = 8;


    /// <summary>The volumes the keys are worked out against: ready ones whose serial is known.</summary>
    public static IReadOnlyList<PinVolume> Snapshot(IEnumerable<VolumeInfo> volumes) {
        return volumes
            .Where(v => v.IsReady && v.Serial is not null)
            .Select(v => new PinVolume(v.Root, v.Serial!.Value, v.Kind == VolumeKind.Removable))
            .ToList();
    }

    /// <summary>The key <paramref name="path"/> is stored under, among <paramref name="volumes"/>.</summary>
    public static string ToKey(string path, IReadOnlyList<PinVolume> volumes) {
        string full = Path.TrimEndingDirectorySeparator(path);
        string root = Path.GetPathRoot(full) ?? "";
        if (root.Length == 0 || VolumeAt(root, volumes) is not { IsRemovable: true } volume) {
            return full;
        }

        string below = full[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string head = VolumePrefix + volume.Serial.ToString("X8", CultureInfo.InvariantCulture);

        return below.Length == 0 ? head : head + Path.DirectorySeparatorChar + below;
    }

    /// <summary>
    /// The path <paramref name="key"/> stands for now, or null when it goes
    /// by a volume that is not among <paramref name="volumes"/>.
    /// </summary>
    public static string? ToPath(string key, IReadOnlyList<PinVolume> volumes) {
        if (!key.StartsWith(VolumePrefix, StringComparison.OrdinalIgnoreCase)) {
            return key;
        }

        string rest = key[VolumePrefix.Length..];
        if (rest.Length < SerialLength
            || !uint.TryParse(rest.AsSpan(0, SerialLength), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint serial)) {
            return null;
        }
        var volume = volumes.FirstOrDefault(v => v.IsRemovable && v.Serial == serial);
        if (volume is null) {
            return null;
        }

        string below = rest[SerialLength..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return below.Length == 0 ? volume.Root : Path.Combine(volume.Root, below);
    }


    private static PinVolume? VolumeAt(string root, IReadOnlyList<PinVolume> volumes) {
        string trimmed = Path.TrimEndingDirectorySeparator(root);

        return volumes.FirstOrDefault(v =>
            string.Equals(Path.TrimEndingDirectorySeparator(v.Root), trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
