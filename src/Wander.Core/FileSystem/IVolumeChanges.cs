namespace Wander.Core.FileSystem;

/// <summary>
/// Volumes coming and going - a flash drive put in, a card pulled out. The
/// system says so to a window, and the window's message hook passes it on;
/// what Core hears is a root and which way it went. Raised on the thread
/// the window runs on.
/// </summary>
public interface IVolumeChanges {
    event EventHandler<VolumeChange>? Changed;
}


/// <param name="Root">The volume's root: <c>E:\</c>.</param>
/// <param name="Arrived">True when it came, false when it went.</param>
public sealed record VolumeChange(string Root, bool Arrived);
