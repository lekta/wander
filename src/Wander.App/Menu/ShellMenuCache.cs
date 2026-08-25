using Wander.Core;
using Wander.Core.Shell;

namespace Wander.App.Menu;

/// <summary>
/// Keeps the last shell-menu query alive so a second right-click on the
/// same target is instant.
///
/// <para>
/// Asking the shell costs real time — roughly 400 ms the first time (the
/// handler DLLs load into our process) and 80–180 ms afterwards, because
/// each handler gets to think: TortoiseGit, for one, reads the repository
/// status before it can decide what to offer. Explorer pays the same price;
/// it just never repeats it for a target it already asked about.
/// </para>
///
/// <para>
/// Lifetime rests on one fact: a right-click that opens a menu has already
/// closed any menu that was open. So the moment <see cref="Acquire"/> is
/// asked for a *different* target is the moment the previous session is
/// provably unused, and that is where it gets disposed. Nothing has to
/// track "is a menu still up" — which is what makes this safe against the
/// menu's Closed handler running after the next right-click was already
/// processed (it does: Closed defers to Background priority, input does not).
/// </para>
/// </summary>
public sealed class ShellMenuCache : IDisposable {
    private IShellContextMenuSession? _session;
    private string? _key;


    /// <summary>
    /// The session for this target — the cached one when it still applies,
    /// a fresh one otherwise. Ownership stays here; callers must not dispose
    /// what they get back.
    /// </summary>
    public IShellContextMenuSession? Acquire(IReadOnlyList<string> paths, string folderPath) {
        if (!ServiceLocator.IsRegistered<IShellContextMenu>()) {
            return null;
        }

        string key = BuildKey(paths, folderPath);
        if (_session is not null && _key == key) {
            return _session;
        }

        Drop();
        _session = ServiceLocator.Get<IShellContextMenu>().Open(paths, folderPath);
        _key = key;

        return _session;
    }

    /// <summary>
    /// Something happened that could change the shell's answer — a listing
    /// refresh, a navigation, or a third-party command that just ran. The
    /// session is only unhooked from its key, not disposed: it may still be
    /// backing a menu the user is looking at, and the next
    /// <see cref="Acquire"/> is both the first safe moment to free it and
    /// the first moment anyone needs it gone.
    /// </summary>
    public void Invalidate() {
        _key = null;
    }

    public void Dispose() {
        Drop();
    }


    private void Drop() {
        _session?.Dispose();
        _session = null;
        _key = null;
    }

    private static string BuildKey(IReadOnlyList<string> paths, string folderPath) {
        // Selection order is a UI accident, not part of the target; the
        // shell is asked about the same set either way.
        return folderPath + " " + string.Join(" ", paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }
}
