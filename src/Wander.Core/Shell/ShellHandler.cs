namespace Wander.Core.Shell;

/// <summary>How a handler gets its rows into the shell's context menu.</summary>
public enum ShellHandlerKind {
    /// <summary>
    /// A COM object under <c>shellex\ContextMenuHandlers</c>. It builds its
    /// rows at popup time, so the registry knows it exists but not what it
    /// will draw — 7-Zip, TortoiseGit, antivirus scanners.
    /// </summary>
    ContextMenuHandler,

    /// <summary>
    /// A plain verb under <c>shell\&lt;verb&gt;</c>: one row, its label and
    /// its command line sitting in the registry in plain sight. "Open Git
    /// Bash here", "Воспроизвести в VLC".
    /// </summary>
    Verb,
}


/// <summary>
/// One installed context-menu extension, as the registry describes it.
///
/// <para>
/// This is the half <c>IContextMenu</c> cannot tell us. The shell hands
/// Wander a merged menu — labels, icons, command ids — and nothing about
/// which handler produced which row, which application it belongs to, or
/// what it is registered for. Everything in the settings table beyond "a
/// row called X exists" comes from here.
/// </para>
///
/// <para>
/// Reading it is ordinary unprivileged work: <c>HKLM\SOFTWARE\Classes</c>
/// and <c>HKCU\SOFTWARE\Classes</c> are readable by <c>Users</c>, nothing
/// is written, and no elevation is involved. Every shell-extension manager
/// (ShellExView, Autoruns) reads exactly these keys.
/// </para>
/// </summary>
public sealed record ShellHandler {
    /// <summary>
    /// What a menu row produced by this handler is expected to key on —
    /// see <see cref="ShellEntryKey"/>. Exact for a
    /// <see cref="ShellHandlerKind.Verb"/> (the verb is the registry key
    /// name and the shell reports it back verbatim); best-effort for a COM
    /// handler, where the registry key name is the closest thing to the
    /// label it will draw, and in practice matches it ("7-Zip",
    /// "TortoiseGit").
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>What to show in the settings table's "Пункт" column.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The application it belongs to, from the version info of the DLL or
    /// EXE behind it. Empty when the file is gone or carries no version
    /// resource — the row is still listed, just without an owner.
    /// </summary>
    public string AppName { get; init; } = string.Empty;

    /// <summary>
    /// Where it is registered: <c>*</c>, <c>Directory</c>, an extension
    /// (<c>.7z</c>), … — see <see cref="ShellScopes"/>. One handler is
    /// commonly registered several times over; 7-Zip sits on <c>*</c>,
    /// <c>Directory</c> and <c>Folder</c> at once.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    public ShellHandlerKind Kind { get; init; }

    /// <summary>
    /// Windows' own plumbing rather than something a user installed.
    ///
    /// <para>
    /// Roughly forty of the fifty handlers on a stock machine are this:
    /// "Отправить", BitLocker's six verbs, Defender, Work Folders, the
    /// sharing menu. Listing them by default would bury the six rows anyone
    /// actually came to switch off, so the settings table hides them behind
    /// a checkbox rather than dropping them — some of them (SendTo) really
    /// are worth turning off.
    /// </para>
    /// </summary>
    public bool IsSystem { get; init; }
}


/// <summary>
/// Reads the installed context-menu extensions out of the registry.
/// Implemented in <c>Wander.Platform.Windows</c>; Core only states the
/// shape, so the catalog and its tests never touch a registry.
/// </summary>
public interface IShellHandlerRegistry {
    /// <summary>
    /// Handlers registered on any of <paramref name="scopes"/>. Scanning is
    /// scoped rather than exhaustive on purpose: walking every one of the
    /// ~800 registered extensions is affordable (tens of milliseconds) but
    /// produces a table nobody wants to read.
    /// </summary>
    IReadOnlyList<ShellHandler> Scan(IReadOnlyList<string> scopes);

    /// <summary>
    /// Every file extension the system knows about, for the "Добавить"
    /// picker. Names only — no per-extension work — so this stays cheap.
    /// </summary>
    IReadOnlyList<string> ListExtensions();
}
