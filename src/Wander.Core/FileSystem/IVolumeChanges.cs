namespace Wander.Core.FileSystem;

/// <summary>
/// Volumes coming and going - a flash drive put in, a card pulled out. The
/// system says so to a window, and the window's message hook passes it on;
/// what Core hears is a root and which way it went. Raised on the thread
/// the window runs on.
/// </summary>
public interface IVolumeChanges {
    event EventHandler<VolumeChange>? Changed;

    /// <summary>
    /// Windows asks to take the volume the open folder is on - an eject from
    /// the tray, from Explorer or from Wander (E, 2026-10-19): what Wander
    /// has open on it is let go of now, before the answer, or the eject
    /// fails "in use". The root, as in <see cref="VolumeChange.Root"/>.
    /// </summary>
    event EventHandler<string>? Releasing;
}


/// <param name="Root">The volume's root: <c>E:\</c>.</param>
/// <param name="Arrived">True when it came, false when it went.</param>
public sealed record VolumeChange(string Root, bool Arrived);
