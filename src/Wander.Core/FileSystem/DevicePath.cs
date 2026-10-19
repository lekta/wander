namespace Wander.Core.FileSystem;

/// <summary>
/// A place on a phone or a camera (PLAN Y4b): <c>device:Pixel 6\Internal
/// shared storage\DCIM\Camera</c> - the device by its name, then its
/// storage and the folders by theirs, as the device names them. Pure
/// string work: whether the device is plugged in is <c>IDeviceContent</c>'s
/// to say.
///
/// <para>
/// Not the shell's parsing name: that one has <c>\\?\</c> inside, which
/// <see cref="Path.GetDirectoryName(string)"/> folds into something else,
/// and its device part is the USB port for some devices (OnePlus, Canon
/// R8 - stand Y4a), so the same phone in another port would be another
/// place. Its name is the same in any port. No folder on a disk can be
/// mistaken for one: Windows allows no colon in a name.
/// </para>
/// </summary>
/// <param name="Device">The device's name, as the computer's listing shows it.</param>
/// <param name="Parts">The storage, then the folders under it; empty for the device itself.</param>
public sealed record DevicePath(string Device, IReadOnlyList<string> Parts) {
    public const string Prefix = "device:";


    /// <summary>The device itself: what lists its storages.</summary>
    public bool IsRoot => Parts.Count == 0;

    /// <summary>The device's own path, <c>device:Pixel 6</c>.</summary>
    public string Root => RootOf(Device);

    /// <summary>The storage the path is on; null for the device itself.</summary>
    public string? Storage => Parts.Count > 0 ? Parts[0] : null;

    /// <summary>The folders and the file under the storage, a name per level: what <see cref="DeviceClutter"/> reads.</summary>
    public IReadOnlyList<string> InStorage => Parts.Count > 1 ? Parts.Skip(1).ToArray() : Array.Empty<string>();


    /// <summary>The path of a device by its name.</summary>
    public static string RootOf(string device) {
        return Prefix + device;
    }

    /// <summary>A path on a device - the device itself included. A string test, nothing asked.</summary>
    public static bool Is(string? path) {
        return path is not null && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A storage of a device or anything on one: what is copied off it. Not the device itself.</summary>
    public static bool IsInside(string? path) {
        return Parse(path) is { IsRoot: false };
    }

    /// <summary>The parts of <paramref name="path"/>, or null when it is not a device's.</summary>
    public static DevicePath? Parse(string? path) {
        if (!Is(path)) {
            return null;
        }

        string[] names = path![Prefix.Length..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        return names.Length == 0 ? null : new DevicePath(names[0], names[1..]);
    }

    /// <summary>
    /// A name made fit to be a part of a path: a device or a phone may call
    /// something by a name with a separator in it, which a path would read
    /// as two levels.
    /// </summary>
    public static string SafeName(string name) {
        return name.Replace('\\', '_').Replace('/', '_');
    }
}
