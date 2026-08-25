namespace Wander.Core.Shell;

/// <summary>
/// One item as reported by the Windows shell's own context menu — the
/// legacy <c>IContextMenu</c> surface that Explorer shows under Windows 11's
/// "Show more options", and that 7-Zip, TortoiseGit, WinRAR and friends
/// register into.
///
/// <para>
/// <see cref="CommandId"/> is meaningful only for the session that produced
/// it: the shell hands out ids relative to a live COM object, so an id from
/// a closed session is garbage. Callers must invoke through the same
/// <see cref="IShellContextMenuSession"/> they enumerated.
/// </para>
/// </summary>
public sealed record ShellMenuEntry {
    public int CommandId { get; init; } = -1;

    public string Header { get; init; } = string.Empty;

    public bool IsSeparator { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// The handler's canonical verb ("openas", "sendto", …), or empty when
    /// it doesn't publish one — which is most of the time for third-party
    /// entries and always for a submenu header. Unlike
    /// <see cref="Header"/> this is not localised, so it is the only sound
    /// basis for deciding where an entry belongs.
    /// </summary>
    public string Verb { get; init; } = string.Empty;

    /// <summary>PNG bytes of the item's bitmap, when the handler supplied one.</summary>
    public byte[]? IconPng { get; init; }

    public IReadOnlyList<ShellMenuEntry> Children { get; init; } = Array.Empty<ShellMenuEntry>();


    public bool HasChildren => Children.Count > 0;
}


/// <summary>
/// A live query against the shell's context menu. Holds native resources
/// (a COM <c>IContextMenu</c> and an <c>HMENU</c>) from the moment the menu
/// is built until the user's pick has been invoked, so it must be disposed
/// exactly once — after invocation, not before.
/// </summary>
public interface IShellContextMenuSession : IDisposable {
    /// <summary>Top-level items, already filtered of verbs Wander renders itself.</summary>
    IReadOnlyList<ShellMenuEntry> Items { get; }

    /// <summary>
    /// Runs the shell command with the given id. Returns false when the
    /// handler refused or threw — the caller reports it, nothing is retried.
    /// </summary>
    bool Invoke(int commandId);
}


/// <summary>
/// Gateway to third-party context-menu handlers.
///
/// <para>
/// Querying loads the registered handler DLLs into Wander's process, the
/// same way Explorer does. That is the price of supporting them at all;
/// the user can turn the whole mechanism off (or block individual
/// handlers) through <c>AppSettings.ShellExtensionsEnabled</c>.
/// </para>
/// </summary>
public interface IShellContextMenu {
    /// <summary>
    /// Opens a session for <paramref name="paths"/> inside
    /// <paramref name="folderPath"/>. An empty <paramref name="paths"/> asks
    /// for the folder-background menu instead of a per-item one.
    /// Returns null when the shell could not be consulted at all.
    /// </summary>
    IShellContextMenuSession? Open(IReadOnlyList<string> paths, string folderPath);
}
