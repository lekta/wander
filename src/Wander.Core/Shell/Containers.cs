using Wander.Core.FileSystem;

namespace Wander.Core.Shell;

/// <summary>
/// Places Wander reads but never writes into: an archive, and a phone or a
/// camera (PLAN Y4). The questions both answer the same way - may a drop
/// land here, does a paste of this copy bytes out of somewhere - asked
/// once, so a rule about archives does not quietly leave devices out.
/// What differs between the two (which service copies the bytes out, what
/// the status line calls it) is asked of <see cref="Archives"/> and
/// <see cref="DevicePath"/> themselves.
/// </summary>
public static class Containers {
    /// <summary>In an archive or on a device, the archive or the device itself included: nothing is written there.</summary>
    public static bool Contains(string? path) {
        // The device test first: a string test, while an archive's may ask
        // the shell which extensions it opens.
        return DevicePath.Is(path) || Archives.Contains(path);
    }

    /// <summary>
    /// An entry within an archive, or a storage of a device and anything on
    /// it: what is taken out by a copy of its own - extraction or import -
    /// rather than by the filesystem's.
    /// </summary>
    public static bool Inside(string? path) {
        return DevicePath.Is(path) ? DevicePath.IsInside(path) : Archives.Inside(path);
    }
}
