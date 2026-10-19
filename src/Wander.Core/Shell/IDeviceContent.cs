using Wander.Core.FileSystem;

namespace Wander.Core.Shell;

/// <summary>
/// What is on the phones and cameras plugged in (PLAN Y4): read only, by
/// <see cref="DevicePath"/>. Listing a device's folder is also
/// <see cref="IShellNamespace.Enumerate"/>'s - a device is the namespace's
/// fourth branch - and this is the rest an import needs: one entry, the
/// bytes, and whether the device is there at all.
///
/// <para>
/// Every call may go over a cable that is pulled out half-way: a device
/// that stops answering throws (<see cref="DeviceErrors.IsGone"/> tells the
/// codes), and <see cref="StateOf"/> says whether it is still plugged in.
/// </para>
/// </summary>
public interface IDeviceContent {
    /// <summary>
    /// Read through the shell rather than Windows Portable Devices: the
    /// fallback "Отладка" switches on when the direct way misbehaves on a
    /// device (decision of 2026-10-19). Paths stay as they are; a device's
    /// folders may read more slowly.
    /// </summary>
    bool ThroughShell { get; set; }

    /// <summary>
    /// The devices plugged in, one folder row each, in a steady order - what
    /// follows the drives in the computer's listing. Asks the shell, so off
    /// the UI thread.
    /// </summary>
    IReadOnlyList<FileSystemEntry> Devices();

    /// <summary>
    /// What <paramref name="path"/> stands for: from the listing it was
    /// read in, otherwise by reading its folder. Null when there is no such
    /// thing, or its device is not there.
    /// </summary>
    FileSystemEntry? GetEntry(string path);

    /// <summary>The same listing <see cref="IShellNamespace.Enumerate"/> gives for a device's path.</summary>
    IReadOnlyList<FileSystemEntry> Enumerate(string path, CancellationToken ct = default);

    /// <summary>
    /// The bytes of a file on a device, from the start, with its length
    /// known (<see cref="Stream.Length"/>). Read only forwards: a device
    /// gives its files one way.
    /// </summary>
    /// <exception cref="FileNotFoundException">No such file - the phone may have re-read its storage since.</exception>
    Stream OpenRead(string path);

    /// <summary>The device's own small picture of a file, as it was stored (a JPEG, usually); null when it keeps none.</summary>
    /// <exception cref="IOException">The device did not give it now - busy with another read, gone.</exception>
    byte[]? ReadThumbnail(string path);

    /// <summary>Whether the device <paramref name="path"/> is on is there and lets its storages be read.</summary>
    DeviceState StateOf(string path);
}


/// <summary>How a device answers.</summary>
public enum DeviceState {
    /// <summary>Plugged in, its storages there.</summary>
    Ready,

    /// <summary>
    /// Plugged in with no storages to show: a phone with its screen locked,
    /// or not set to "File transfer" (stand Y4a: no error code, an empty
    /// device).
    /// </summary>
    Locked,

    /// <summary>Not plugged in - unplugged, switched off, gone to sleep.</summary>
    Gone,
}
