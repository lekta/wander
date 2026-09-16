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

    /// <summary>Forgets the kept answers - the user may have just installed the tool.</summary>
    void Refresh();
}
