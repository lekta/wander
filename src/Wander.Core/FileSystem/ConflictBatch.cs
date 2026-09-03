namespace Wander.Core.FileSystem;

/// <summary>
/// One quick answer for the pairs nobody has decided yet - the window's
/// "apply to the rest", offered after the user has looked at the list
/// rather than before it (PLAN, Q3). The verdict-dependent one touches
/// only the pairs it fits and leaves the rest open.
/// </summary>
public enum ConflictBulkAction {
    /// <summary>Nothing; every pair is answered by hand.</summary>
    None,

    Replace,
    Skip,

    /// <summary>A file under a new name; two folders merged.</summary>
    KeepBoth,

    /// <summary>Replace only where the source is the newer of the two.</summary>
    ReplaceIfSourceNewer,
}


/// <summary>
/// The state behind the conflict window: the pairs the batch asked about,
/// what is known about each (<see cref="ConflictVerdict"/>), what the user
/// decided so far - and, under every folder being merged, the pairs found
/// inside it. The window draws it and feeds it facts; every rule about what
/// an answer means lives here, where a test reaches it.
///
/// <para>
/// Facts arrive late and out of order: bytes are compared in the
/// background, a merged folder is read in the background. So a verdict is
/// not fixed at construction (<see cref="SetCompared"/> replaces it, and
/// with "skip identical" on that can decide the pair by itself), and the
/// tree grows (<see cref="AttachScan"/>). A pair the user has answered is
/// never re-decided behind their back.
/// </para>
/// </summary>
public sealed class ConflictBatch {
    private readonly ConflictRequest _request;
    private readonly List<ConflictPair> _roots;

    private bool _skipIdentical;


    /// <param name="skipIdentical">
    /// The user's "don't ask about files that are already there
    /// byte-for-byte" setting (PLAN, Q4). It cannot fire at construction -
    /// nobody has read any bytes yet - only as comparisons land.
    /// </param>
    public ConflictBatch(ConflictRequest request, bool skipIdentical = false) {
        if (request.Conflicts.Count == 0) {
            throw new ArgumentException("A conflict batch with nothing in it has nothing to ask about.", nameof(request));
        }

        _request = request;
        _roots = request.Conflicts.Select(c => new ConflictPair(c, parent: null)).ToList();
        _skipIdentical = skipIdentical;
    }


    /// <summary>How many items the batch carries altogether - the "of 10" in "3 of 10".</summary>
    public int ItemCount => _request.ItemCount;

    /// <summary>The pairs the batch asked about; what is inside merged folders hangs under them.</summary>
    public IReadOnlyList<ConflictPair> Roots => _roots;

    public int Count => _roots.Count;

    /// <summary>
    /// The source leaves its folder when the batch is applied. One batch is
    /// one operation, so the whole list agrees; the first pair answers for
    /// all of them.
    /// </summary>
    public bool IsMove => _roots[0].Conflict.IsMove;

    /// <summary>Is the "already there byte-for-byte" policy answering for us?</summary>
    public bool SkipIdentical => _skipIdentical;

    public int DecidedCount => Effective().Count(p => p.Choice is not null);

    public bool AllDecided => Effective().All(p => p.Choice is not null);


    /// <summary>
    /// Every pair OK answers for, in the order the window lists them:
    /// parents before children, children only under a merge.
    /// </summary>
    public IReadOnlyList<ConflictPair> Effective() {
        var pairs = new List<ConflictPair>();
        foreach (var root in _roots) {
            Collect(root, pairs);
        }

        return pairs;
    }

    /// <summary>
    /// The user's answer for one pair; null takes the answer back, which is
    /// what clearing both ticks does.
    /// </summary>
    public void Choose(ConflictPair pair, ConflictResolution? choice) {
        pair.Choice = choice;
        pair.FromPolicy = false;
    }

    /// <summary>
    /// Turns the "already there byte-for-byte" policy on or off. On, it
    /// answers every identical pair still open, and every one a comparison
    /// finds later; off, it takes back exactly the answers it gave and
    /// leaves the user's own alone. Returns the pairs that changed.
    /// </summary>
    public IReadOnlyList<ConflictPair> SetSkipIdentical(bool on) {
        _skipIdentical = on;

        var changed = new List<ConflictPair>();
        foreach (var pair in All()) {
            if (on && pair.Choice is null && pair.Verdict.Identical == true) {
                pair.Choice = ConflictResolution.Skip;
                pair.FromPolicy = true;
                changed.Add(pair);
            } else if (!on && pair.FromPolicy) {
                pair.Choice = null;
                pair.FromPolicy = false;
                changed.Add(pair);
            }
        }

        return changed;
    }

    /// <summary>
    /// The bytes have been read - or could not be, which is what null says:
    /// the pair then stays open on content for good and is not offered for
    /// reading again. Returns true when this also decided the pair - "skip
    /// identical" is on, the two are the same, and nobody had answered yet -
    /// so the window knows to redraw the row as answered.
    /// </summary>
    public bool SetCompared(ConflictPair pair, bool? identical) {
        if (identical is null) {
            pair.Verdict = pair.Verdict with { SourceReachable = false };

            return false;
        }

        pair.Verdict = ConflictVerdict.Of(pair.Conflict, identical);
        if (!_skipIdentical || identical != true || pair.Choice is not null) {
            return false;
        }

        pair.Choice = ConflictResolution.Skip;
        pair.FromPolicy = true;

        return true;
    }

    /// <summary>
    /// The next pair worth reading, or null when there is none: same size,
    /// nobody compared them, the source can be opened. Small files first, in
    /// list order, so the marks on them clear before anybody has scrolled to
    /// them; the ones above <paramref name="smallLimit"/> come after, in a
    /// second pass. Pairs under a folder that is not being merged are not
    /// read - they are not on screen and not in the answer.
    /// </summary>
    public ConflictPair? NextToCompare(long smallLimit) {
        ConflictPair? large = null;
        foreach (var pair in Effective()) {
            if (!pair.Verdict.ContentUndecided) {
                continue;
            }
            if (pair.Conflict.Source.Size <= smallLimit) {
                return pair;
            }
            large ??= pair;
        }

        return large;
    }

    /// <summary>
    /// Applies a quick action to the pairs nobody has decided yet, and
    /// returns the ones it changed. A decided pair is never overwritten:
    /// the point of the list is that a considered answer survives an
    /// impatient click on "replace the rest".
    /// </summary>
    public IReadOnlyList<ConflictPair> Apply(ConflictBulkAction action) {
        var changed = new List<ConflictPair>();
        foreach (var pair in Effective()) {
            if (pair.Choice is not null) {
                continue;
            }

            var choice = ChoiceFor(action, pair);
            if (choice is null) {
                continue;
            }

            pair.Choice = choice;
            pair.FromPolicy = false;
            changed.Add(pair);
        }

        return changed;
    }

    public void MarkScanning(ConflictPair pair) {
        pair.Scan = MergeScanState.Scanning;
    }

    public void MarkScanFailed(ConflictPair pair) {
        pair.Scan = MergeScanState.Failed;
    }

    /// <summary>
    /// The folder's contents are known: its collisions become its children.
    /// A folder pair among them merges in turn - the parent is being merged,
    /// so it can only be merged or singled out by hand - and is already
    /// scanned, because the walk went all the way down.
    /// </summary>
    public void AttachScan(ConflictPair pair, MergeScanner.Result scan) {
        pair.Scan = MergeScanState.Scanned;
        pair.FreeFiles = scan.FreeFiles;
        foreach (var node in scan.Conflicts) {
            Attach(pair, node);
        }
    }

    /// <summary>
    /// One answer per pair OK is answering for - the ones the batch asked
    /// about, and the ones found inside merged folders - in the order they
    /// are listed; what <see cref="IConflictResolver.ResolveAll"/> returns.
    /// </summary>
    public IReadOnlyList<ConflictAnswer> Answers() {
        if (!AllDecided) {
            throw new InvalidOperationException("Answers were asked for while pairs were still open.");
        }

        return Effective().Select(p => new ConflictAnswer(p.Conflict, p.Choice!.Value)).ToList();
    }


    private static void Collect(ConflictPair pair, List<ConflictPair> into) {
        into.Add(pair);
        if (!pair.IsMerging) {
            return;
        }
        foreach (var child in pair.Children) {
            Collect(child, into);
        }
    }

    /// <summary>
    /// Every pair, the hidden ones too: a policy answer under a folder that
    /// is later merged again is still the right answer.
    /// </summary>
    private IEnumerable<ConflictPair> All() {
        var pending = new Stack<ConflictPair>(_roots);
        while (pending.Count > 0) {
            var pair = pending.Pop();
            yield return pair;
            foreach (var child in pair.Children) {
                pending.Push(child);
            }
        }
    }

    private static void Attach(ConflictPair parent, MergeScanner.Node node) {
        var child = new ConflictPair(node.Conflict, parent);
        parent.AddChild(child);
        if (!child.CanMerge) {
            return;
        }

        child.Choice = ConflictResolution.Merge;
        child.Scan = MergeScanState.Scanned;
        child.FreeFiles = node.FreeFiles;
        foreach (var inner in node.Children) {
            Attach(child, inner);
        }
    }

    /// <summary>
    /// What a quick action means for one pair, or null when it has nothing
    /// to say about it.
    /// </summary>
    private static ConflictResolution? ChoiceFor(ConflictBulkAction action, ConflictPair pair) {
        return action switch {
            ConflictBulkAction.Replace => ConflictResolution.Replace,
            ConflictBulkAction.Skip => ConflictResolution.Skip,
            ConflictBulkAction.KeepBoth => pair.CanMerge ? ConflictResolution.Merge : ConflictResolution.Rename,
            ConflictBulkAction.ReplaceIfSourceNewer =>
                pair.Verdict.Age == ConflictAge.SourceNewer ? ConflictResolution.Replace : null,
            _ => null,
        };
    }
}
