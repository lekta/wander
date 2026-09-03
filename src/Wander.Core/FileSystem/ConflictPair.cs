namespace Wander.Core.FileSystem;

/// <summary>Where a merged folder's own collisions stand.</summary>
public enum MergeScanState {
    /// <summary>Nobody has looked inside yet.</summary>
    NotScanned,
    Scanning,
    /// <summary>Looked; the collisions found are the pair's children.</summary>
    Scanned,
    /// <summary>Could not be read - the answer stands, the contents stay unknown.</summary>
    Failed,
}


/// <summary>
/// One row of the conflict window: two entries under one name, what is
/// known about them, what the user decided - and, for a folder the user
/// chose to merge, the pairs found inside it as children. Only
/// <see cref="ConflictBatch"/> changes it; the window reads.
/// </summary>
public sealed class ConflictPair {
    private readonly List<ConflictPair> _children = new();


    internal ConflictPair(FileConflictInfo conflict, ConflictPair? parent) {
        Conflict = conflict;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        Verdict = ConflictVerdict.Of(conflict);
    }


    public FileConflictInfo Conflict { get; }

    public ConflictVerdict Verdict { get; internal set; }

    public ConflictResolution? Choice { get; internal set; }

    /// <summary>
    /// The answer came from the "skip identical" policy, not from the user:
    /// what the policy takes back when it is switched off.
    /// </summary>
    internal bool FromPolicy { get; set; }

    public ConflictPair? Parent { get; }

    /// <summary>Zero for a pair the batch asked about; one more per merged folder above.</summary>
    public int Depth { get; }

    /// <summary>The collisions inside, once a merge was chosen and the folders read.</summary>
    public IReadOnlyList<ConflictPair> Children => _children;

    public MergeScanState Scan { get; internal set; }

    /// <summary>
    /// Files under this folder pair with nothing in their way - they cross
    /// over without a question. Counted through the whole subtree.
    /// </summary>
    public int FreeFiles { get; internal set; }

    public bool IsFolderPair =>
        Conflict.Source.Kind == EntryKind.Directory && Conflict.ExistingTarget.Kind == EntryKind.Directory;

    /// <summary>
    /// Two folders whose contents can be combined. A folder inside an
    /// archive cannot: only the shell can walk it.
    /// </summary>
    public bool CanMerge => IsFolderPair && Conflict.SourceReachable;

    public bool IsMerging => Choice == ConflictResolution.Merge;

    /// <summary>Every pair under this one, at any depth.</summary>
    public int InnerConflicts {
        get {
            int count = 0;
            foreach (var child in _children) {
                count += 1 + child.InnerConflicts;
            }

            return count;
        }
    }

    /// <summary>
    /// What the row is called: the name for a pair the batch asked about;
    /// below a merged folder, the path from that folder down - among many
    /// rows the name alone would not say where the file sits.
    /// </summary>
    public string DisplayPath {
        get {
            if (Parent is null) {
                return Conflict.Source.Name;
            }

            var root = this;
            while (root.Parent is not null) {
                root = root.Parent;
            }

            return Path.GetRelativePath(root.Conflict.Source.FullPath, Conflict.Source.FullPath);
        }
    }

    /// <summary>
    /// Part of what OK hands back: a top-level pair always, a nested one
    /// only while every folder above it is being merged.
    /// </summary>
    public bool IsEffective => Parent is null || (Parent.IsMerging && Parent.IsEffective);


    internal void AddChild(ConflictPair child) {
        _children.Add(child);
    }
}
