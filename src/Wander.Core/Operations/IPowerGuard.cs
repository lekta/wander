namespace Wander.Core.Operations;

/// <summary>
/// Keeps the computer from going to sleep for as long as something holds
/// what it hands out. A copy off a card runs for minutes with nobody at the
/// keyboard, and a machine that sleeps in the middle of it wakes up to an
/// operation that failed on a device that went away. The screen may still
/// go dark: only the system is kept up.
/// </summary>
public interface IPowerGuard {
    /// <param name="reason">What the system is told - <c>powercfg /requests</c> shows it.</param>
    /// <returns>Dispose to let the computer sleep again.</returns>
    IDisposable KeepAwake(string reason);
}
