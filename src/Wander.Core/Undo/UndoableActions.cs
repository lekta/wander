using Wander.Core.FileSystem;

namespace Wander.Core.Undo;

/// <summary>
/// Undo: rename <paramref name="NewPath"/> back to <paramref name="OldName"/>.
/// </summary>
public sealed record RenameAction(IFileSystem Fs, string NewPath, string OldName) : IUndoableAction {
    public string Description => $"Rename to '{Path.GetFileName(NewPath)}'";
    public void Undo() => Fs.Rename(NewPath, OldName);

    public IReadOnlyList<string> PathsAfterUndo =>
        new[] { Path.Combine(Path.GetDirectoryName(NewPath) ?? "", OldName) };
}

/// <summary>
/// Undo: move <paramref name="NewPath"/> back to <paramref name="OldPath"/>.
/// </summary>
public sealed record MoveAction(IFileSystem Fs, string OldPath, string NewPath) : IUndoableAction {
    public string Description => $"Move '{Path.GetFileName(OldPath)}'";
    public void Undo() => Fs.MoveEntry(NewPath, OldPath);

    public IReadOnlyList<string> PathsAfterUndo => new[] { OldPath };
}

/// <summary>
/// Undo of "I created X" — send X to the recycle bin (Explorer parity:
/// Ctrl+Z after New Folder deletes the empty folder to the bin, not silently).
/// Also used as the undo for Copy ops, where the copy at the destination is
/// just another created item.
/// </summary>
public sealed record CreateAction(IRecycleBin Bin, string CreatedPath) : IUndoableAction {
    public string Description => $"Create '{Path.GetFileName(CreatedPath)}'";
    public void Undo() => Bin.Send(CreatedPath);

    /// <summary>Nothing to select: undoing a create takes the item away.</summary>
    public IReadOnlyList<string> PathsAfterUndo => Array.Empty<string>();
}

/// <summary>
/// Undo of "I deleted X to recycle bin" — restore X from the bin using the
/// handle captured at delete time.
/// </summary>
public sealed record DeleteAction(IRecycleBin Bin, RecycleHandle Handle) : IUndoableAction {
    public string Description => $"Delete '{Path.GetFileName(Handle.OriginalPath)}'";
    public void Undo() => Bin.Restore(Handle);

    public IReadOnlyList<string> PathsAfterUndo => new[] { Handle.OriginalPath };
}

/// <summary>
/// Bundles N sub-actions into a single undo step (a paste or drop of many
/// files lands as one Ctrl+Z, not N).
/// </summary>
public sealed class CompositeAction : IUndoableAction {
    private readonly IReadOnlyList<IUndoableAction> _actions;


    public CompositeAction(string description, IReadOnlyList<IUndoableAction> actions) {
        Description = description;
        _actions = actions;
    }


    public string Description { get; }


    public void Undo() {
        // Reverse order so dependent ops unwind correctly.
        for (int i = _actions.Count - 1; i >= 0; i--) {
            _actions[i].Undo();
        }
    }


    /// <summary>Everything the members put back, in their original order.</summary>
    public IReadOnlyList<string> PathsAfterUndo =>
        _actions.SelectMany(a => a.PathsAfterUndo).ToArray();

    /// <summary>
    /// The union — but only when <b>every</b> member is a metadata action.
    /// A composite that moves a file and edits a rating changes the listing,
    /// and a caller that took the cheap path on it would be showing a folder
    /// that no longer exists.
    /// </summary>
    public IReadOnlyList<string> MetadataTargets =>
        _actions.Count > 0 && _actions.All(a => a.MetadataTargets.Count > 0)
            ? _actions.SelectMany(a => a.MetadataTargets).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
}
