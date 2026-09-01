namespace Wander.Core.FileSystem;

/// <summary>
/// The cut/copy/paste pair's shared state — a list of paths and a "copy or
/// move" flag. Holding paths rather than file contents matches Explorer:
/// the actual move/copy happens at Paste time, against whatever the source
/// files look like then.
///
/// <para>
/// The state is <b>mirrored</b> onto the OS clipboard rather than kept to
/// ourselves: <c>Ctrl+C</c> here has to be pasteable in Explorer and the
/// other way round, which is the project's third pillar. The model stays in
/// memory anyway, because the clipboard is a cross-process resource that
/// opens exclusively and throws when someone else holds it — reading it on
/// every <c>PasteCommand.CanExecute</c> (which WPF re-evaluates dozens of
/// times a second) is not an option. So: writes go out immediately, reads
/// come back on <see cref="SyncFromSystem"/>, which the window calls when it
/// is activated.
/// </para>
///
/// <para>
/// Lives in Core (no WPF deps) so it can be unit-tested without the UI
/// layer; the real clipboard arrives as <see cref="ISystemClipboard"/>.
/// Constructed without one, it behaves exactly as it did before the
/// mirroring existed — which is what the tests use.
/// </para>
/// </summary>
public sealed class ClipboardController {
    private readonly ISystemClipboard? _system;

    private List<string> _paths = new();


    public ClipboardController(ISystemClipboard? system = null) {
        _system = system;
    }


    /// <summary>Fires after every state-changing call so PasteCommand can re-evaluate.</summary>
    public event EventHandler? Changed;


    /// <summary>Snapshot of the paths captured by the most recent Copy / Cut.</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>True when the most recent action was a Cut (paste should move, not copy).</summary>
    public bool IsCut { get; private set; }

    /// <summary>Convenience flag for command <c>CanExecute</c> bindings.</summary>
    public bool HasContent => _paths.Count > 0;

    /// <summary>
    /// Set when the last mirrored call could not reach the OS clipboard, or
    /// when what it holds is a kind of file Wander cannot paste. Null when
    /// there is nothing to say. The caller shows it and moves on — neither
    /// case is a failure of the copy itself.
    /// </summary>
    public string? LastSystemIssue { get; private set; }


    /// <summary>
    /// Capture paths for a future copy-paste. Replaces any previous content,
    /// including the Cut/Copy mode. Empty input is treated as <see cref="Clear"/>.
    /// </summary>
    public void Copy(IEnumerable<string> paths) {
        Capture(paths, isCut: false);
    }

    /// <summary>
    /// Capture paths for a future move-paste. Same shape as <see cref="Copy"/>
    /// but flips <see cref="IsCut"/>. After a successful paste the caller is
    /// expected to call <see cref="Clear"/> so a re-paste doesn't re-cut the
    /// already-moved files.
    /// </summary>
    public void Cut(IEnumerable<string> paths) {
        Capture(paths, isCut: true);
    }

    /// <summary>
    /// Drop the captured paths and reset Cut mode — here and on the OS
    /// clipboard, which is what makes Explorer stop drawing the cut files
    /// greyed out after we complete the move.
    /// </summary>
    public void Clear() {
        _system?.Clear();
        ClearLocal();
    }


    /// <summary>
    /// Re-read the OS clipboard and adopt whatever it holds. Called when the
    /// window is activated: to paste, the user has to come back to Wander
    /// anyway, so that is the moment the answer has to be right.
    ///
    /// <para>
    /// A clipboard that cannot be read leaves our state alone — a clipboard
    /// manager holding it for a moment must not silently drop the user's
    /// copy. A clipboard that reads back as "no files" does replace it: the
    /// user copied something else, and Paste should say so rather than paste
    /// what they copied ten minutes ago.
    /// </para>
    /// </summary>
    /// <returns>True when the state changed and the caller should refresh.</returns>
    public bool SyncFromSystem() {
        if (_system is null) {
            return false;
        }

        var files = _system.GetFiles();
        if (files is null) {
            // Unreadable this time round; keep what we have and stay quiet.
            // The next activation usually gets through.
            return false;
        }

        LastSystemIssue = files.Value.HasUnsupportedFiles ? SystemIssue.VirtualFiles : null;

        if (Same(files.Value.Paths, _paths) && files.Value.IsCut == IsCut) {
            return false;
        }

        _paths = files.Value.Paths.ToList();
        IsCut = files.Value.IsCut;
        RaiseChanged();

        return true;
    }


    private void Capture(IEnumerable<string> paths, bool isCut) {
        _paths = paths?.ToList() ?? new List<string>();
        IsCut = isCut;
        LastSystemIssue = null;

        if (_system is not null && _paths.Count > 0 && !_system.SetFiles(_paths, isCut)) {
            // The copy still works inside Wander — the model is right here.
            // Only the hand-off to other applications was lost.
            LastSystemIssue = SystemIssue.WriteFailed;
        }

        RaiseChanged();
    }

    private void ClearLocal() {
        if (_paths.Count == 0 && !IsCut) {
            return;
        }
        _paths = new List<string>();
        IsCut = false;
        RaiseChanged();
    }

    private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) {
        if (a.Count != b.Count) {
            return false;
        }
        for (int i = 0; i < a.Count; i++) {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    private void RaiseChanged() {
        Changed?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>
    /// Marker values for <see cref="LastSystemIssue"/>. Strings, not an
    /// enum, because Core has no access to the resource file — the App layer
    /// maps them onto localized text.
    /// </summary>
    public static class SystemIssue {
        /// <summary>The clipboard holds files that are not on disk (see <see cref="ClipboardFiles.HasUnsupportedFiles"/>).</summary>
        public const string VirtualFiles = "virtual-files";

        /// <summary>The clipboard could not be written; the copy stayed inside Wander.</summary>
        public const string WriteFailed = "write-failed";
    }
}
