namespace Wander.Core.FileSystem;

/// <summary>
/// In-memory "filesystem clipboard" — the cut/copy/paste pair shares this
/// state instead of going through the OS clipboard. Holding paths rather
/// than file contents matches Explorer: the actual move/copy happens at
/// Paste time, against whatever the source files look like then.
///
/// <para>
/// Lives in Core (no WPF deps) so it can be unit-tested without the UI
/// layer. The hosting <see cref="object"/> in <c>Wander.App</c> wires
/// <see cref="Changed"/> to its PasteCommand <c>CanExecute</c> refresh.
/// </para>
/// </summary>
public sealed class ClipboardController {
    private List<string> _paths = new();


    /// <summary>Fires after every state-changing call so PasteCommand can re-evaluate.</summary>
    public event EventHandler? Changed;


    /// <summary>Snapshot of the paths captured by the most recent Copy / Cut.</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>True when the most recent action was a Cut (paste should move, not copy).</summary>
    public bool IsCut { get; private set; }

    /// <summary>Convenience flag for command <c>CanExecute</c> bindings.</summary>
    public bool HasContent => _paths.Count > 0;


    /// <summary>
    /// Capture paths for a future copy-paste. Replaces any previous content,
    /// including the Cut/Copy mode. Empty input is treated as <see cref="Clear"/>.
    /// </summary>
    public void Copy(IEnumerable<string> paths) {
        _paths = paths?.ToList() ?? new List<string>();
        IsCut = false;
        RaiseChanged();
    }

    /// <summary>
    /// Capture paths for a future move-paste. Same shape as <see cref="Copy"/>
    /// but flips <see cref="IsCut"/>. After a successful paste the caller is
    /// expected to call <see cref="Clear"/> so a re-paste doesn't re-cut the
    /// already-moved files.
    /// </summary>
    public void Cut(IEnumerable<string> paths) {
        _paths = paths?.ToList() ?? new List<string>();
        IsCut = true;
        RaiseChanged();
    }

    /// <summary>Drop the captured paths and reset Cut mode.</summary>
    public void Clear() {
        if (_paths.Count == 0 && !IsCut) {
            return;
        }
        _paths = new List<string>();
        IsCut = false;
        RaiseChanged();
    }


    private void RaiseChanged() {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
