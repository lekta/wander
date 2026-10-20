using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Undo;

namespace Wander.Core.FileSystem;

/// <summary>
/// Undo: rename <paramref name="NewPath"/> back to <paramref name="OldName"/>.
/// </summary>
internal sealed record RenameAction(IFileSystem Fs, string NewPath, string OldName) : IUndoableAction {
    public string Description => $"Rename to '{Path.GetFileName(NewPath)}'";

    public string Verb => OperationVerbs.Rename;

    public IReadOnlyList<string> PathsBeforeUndo => new[] { NewPath };

    public IReadOnlyList<string> PathsAfterUndo => new[] { OldPath };

    /// <summary>
    /// The name goes back, so whatever remembers the path under the new
    /// name - the open folder when this is it or one above it, the
    /// history, a bookmark, a panel row - follows to the old one.
    /// </summary>
    public IReadOnlyList<(string From, string To)> MovesOnUndo => new[] { (NewPath, OldPath) };

    public void Undo() => Fs.Rename(NewPath, OldName);

    private string OldPath => Path.Combine(Path.GetDirectoryName(NewPath) ?? "", OldName);
}

/// <summary>
/// Undo: move <paramref name="NewPath"/> back to <paramref name="OldPath"/>.
/// </summary>
internal sealed record MoveAction(IFileSystem Fs, string OldPath, string NewPath) : IUndoableAction {
    public string Description => $"Move '{Path.GetFileName(OldPath)}'";
    public string Verb => OperationVerbs.Move;
    public IReadOnlyList<string> PathsBeforeUndo => new[] { NewPath };
    public IReadOnlyList<string> PathsAfterUndo => new[] { OldPath };
    public IReadOnlyList<(string From, string To)> MovesOnUndo => new[] { (NewPath, OldPath) };

    public IUndoableAction Redirected(IReadOnlyDictionary<string, string> folders) {
        return folders.TryGetValue(Path.GetDirectoryName(OldPath) ?? "", out string? instead)
            ? this with { OldPath = Path.Combine(instead, Path.GetFileName(OldPath)) }
            : this;
    }

    public void Undo() => Fs.MoveEntry(NewPath, OldPath);
}

/// <summary>
/// Undo of "I created X" — send X to the recycle bin (Explorer parity:
/// Ctrl+Z after New Folder deletes the empty folder to the bin, not silently).
/// Also used as the undo for Copy ops, where the copy at the destination is
/// just another created item.
/// </summary>
/// <param name="Verb">What made it - a copy, an action's output; a plain create by default.</param>
public sealed record CreateAction(IRecycleBin Bin, string CreatedPath, string Verb = OperationVerbs.Create) : IUndoableAction {
    public string Description => $"Create '{Path.GetFileName(CreatedPath)}'";

    public IReadOnlyList<string> PathsBeforeUndo => new[] { CreatedPath };

    /// <summary>Nothing to select: undoing a create takes the item away.</summary>
    public IReadOnlyList<string> PathsAfterUndo => Array.Empty<string>();

    public void Undo() => Bin.Send(CreatedPath);
}

/// <summary>
/// Undo of "I deleted X to recycle bin" — restore X from the bin using the
/// handle captured at delete time.
/// </summary>
internal sealed record DeleteAction(IRecycleBin Bin, RecycleHandle Handle) : IUndoableAction {
    public string Description => $"Delete '{Path.GetFileName(Handle.OriginalPath)}'";
    public string Verb => OperationVerbs.Recycle;
    public IReadOnlyList<string> PathsAfterUndo => new[] { Handle.OriginalPath };

    public void Undo() => Bin.Restore(Handle);
}

/// <summary>
/// The undo of something sent to the bin on the way - a replaced target, an
/// emptied folder, a single delete. None when the bin took it without
/// anything to find it by (<see cref="RecycleHandle.IsUntraceable"/>): a
/// step that could only fail. What went that way is as gone as after a
/// permanent delete, and so is what older steps would need of it.
/// </summary>
internal static class RecycleUndo {
    public static DeleteAction? Of(RecycleHandle handle, IRecycleBin bin, UndoService undo, ILogger log) {
        if (!handle.IsUntraceable) {
            return new DeleteAction(bin, handle);
        }

        log.Warn($"Recycle: {handle.OriginalPath} - nothing to find it by in the bin, counted as deleted for good");
        undo.Forget(new[] { handle.OriginalPath });

        return null;
    }
}

/// <summary>
/// Undo of "I took X out of an archive" — send the copy to the recycle
/// bin. The same act as undoing a create, said in the words of the
/// operation the user actually performed: after extracting, "Undo:
/// Create 'notes.txt'" describes something they never did.
/// </summary>
internal sealed record ExtractAction(IRecycleBin Bin, string ExtractedPath) : IUndoableAction {
    public string Description => $"Extract '{Path.GetFileName(ExtractedPath)}'";

    public string Verb => OperationVerbs.Extract;

    public IReadOnlyList<string> PathsBeforeUndo => new[] { ExtractedPath };

    /// <summary>Nothing to select: undoing an extraction takes the copy away.</summary>
    public IReadOnlyList<string> PathsAfterUndo => Array.Empty<string>();

    public void Undo() => Bin.Send(ExtractedPath);
}

/// <summary>
/// Bundles N sub-actions into a single undo step (a paste or drop of many
/// files lands as one Ctrl+Z, not N).
/// </summary>
public sealed class CompositeAction : IUndoableAction {
    private readonly IReadOnlyList<IUndoableAction> _actions;


    /// <param name="verb">What the bundle did; the last member's when null.</param>
    /// <param name="itemCount">How many items the user would count; one per member when null.</param>
    public CompositeAction(string description, IReadOnlyList<IUndoableAction> actions, string? verb = null, int? itemCount = null) {
        Description = description;
        _actions = actions;
        Verb = verb ?? (actions.Count > 0 ? actions[^1].Verb : OperationVerbs.Undo);
        ItemCount = itemCount ?? actions.Count;
    }


    public string Description { get; }

    public string Verb { get; }

    public int ItemCount { get; }

    public IReadOnlyList<IUndoableAction> Steps => _actions;


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

    /// <summary>The members' moves in the order <see cref="Undo"/> makes them - last member first.</summary>
    public IReadOnlyList<(string From, string To)> MovesOnUndo =>
        _actions.Reverse().SelectMany(a => a.MovesOnUndo).ToArray();


    public IUndoableAction WithSteps(IReadOnlyList<IUndoableAction> steps) {
        // Part of the bundle: the same verb, and as many items as there are
        // steps left - the best count there is without knowing which step
        // was whose.
        return new CompositeAction(Description, steps, Verb, Math.Min(ItemCount, steps.Count));
    }

    public IUndoableAction Redirected(IReadOnlyDictionary<string, string> folders) {
        var redirected = _actions.Select(a => a.Redirected(folders)).ToList();

        return redirected.Where((a, i) => !ReferenceEquals(a, _actions[i])).Any()
            ? new CompositeAction(Description, redirected, Verb, ItemCount)
            : this;
    }

    public void Undo() {
        // Reverse order so dependent ops unwind correctly.
        for (int i = _actions.Count - 1; i >= 0; i--) {
            _actions[i].Undo();
        }
    }
}
