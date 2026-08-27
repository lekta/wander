namespace Wander.Core.FileSystem;

/// <summary>
/// What the operating system's clipboard holds when it holds files: a list
/// of paths plus a flag saying whether the owner meant "copy" or "cut".
/// Nothing else about the payload is modelled — that is all Wander can
/// either read or write with confidence.
/// </summary>
/// <param name="Paths">The files and folders on the clipboard, in the order the owner put them.</param>
/// <param name="IsCut">True when the owner meant a move (Windows' <c>DROPEFFECT_MOVE</c>).</param>
/// <param name="HasUnsupportedFiles">
/// The clipboard holds files that never existed on disk — an Outlook
/// attachment, a file inside an open <c>.zip</c>, a photo pulled straight
/// off a phone. Windows offers those as a content stream rather than as
/// paths, and Wander cannot paste them. Worth telling the user about,
/// because from their side of the screen they did copy something.
/// </param>
public readonly record struct ClipboardFiles(
    IReadOnlyList<string> Paths,
    bool IsCut,
    bool HasUnsupportedFiles = false) {

    public static readonly ClipboardFiles Empty = new(Array.Empty<string>(), false);

    public bool HasContent => Paths.Count > 0;
}


/// <summary>
/// The OS clipboard, as far as file operations care about it. Implemented
/// outside Core (<c>Wander.Platform.Windows</c>) because the real thing is
/// a Win32 resource with Win32 failure modes; faked in tests.
///
/// <para>
/// Every call is cross-process and can fail: the clipboard is opened
/// exclusively by whoever touches it, and a clipboard manager, Office or an
/// RDP session holding it makes the call throw. Implementations swallow
/// that — a clipboard that cannot be reached is a lost copy, never a crash
/// — and report it through <see cref="LastError"/> so the caller can say so
/// in the status bar.
/// </para>
/// </summary>
public interface ISystemClipboard {
    /// <summary>
    /// Puts <paramref name="paths"/> on the clipboard as a file list.
    /// Returns false if the clipboard could not be written.
    /// </summary>
    bool SetFiles(IReadOnlyList<string> paths, bool isCut);

    /// <summary>
    /// What the clipboard holds right now, or <c>null</c> when it could not
    /// be read at all. The distinction matters: an unreadable clipboard must
    /// leave Wander's own copy/cut state alone, while a readable one that
    /// holds no files (text, a bitmap) means the user really did replace the
    /// file list with something else.
    /// </summary>
    ClipboardFiles? GetFiles();

    /// <summary>
    /// Empties the clipboard — used after a cut-paste, so a second
    /// <c>Ctrl+V</c> cannot try to move files that already moved, and so
    /// Explorer drops the "ghosted" look off the cut files.
    /// </summary>
    void Clear();

    /// <summary>Message from the last failed call, for the status bar. Null when the last call worked.</summary>
    string? LastError { get; }
}
