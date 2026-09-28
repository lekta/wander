namespace Wander.Core.Actions;

/// <summary>
/// Finds an external tool the presets need ("ffmpeg", "soffice",
/// "pandoc") on this machine. Where to look is Windows' business - <c>PATH</c>
/// and the folders the usual installers use - so the implementation lives
/// in Platform. Answers are kept for the session: the menus ask on every
/// opening, the disk does not change that often.
/// </summary>
public interface IToolLocator {
    /// <summary>Full path of the tool's executable, or null when it is nowhere to be found.</summary>
    string? Find(string tool);

    /// <summary>
    /// A program as an action names it - a full path, or a file name, with
    /// <c>.exe</c> when it has no extension - where it is; null when it is not
    /// there or is not a file Windows starts (2026-09-28). The search is the
    /// one the runner's start makes, not <see cref="Find"/>'s wider one: a
    /// program the settings page accepts is one the action starts.
    /// </summary>
    string? Locate(string program);

    /// <summary>Forgets the kept answers - the user may have just installed the tool.</summary>
    void Refresh();
}
