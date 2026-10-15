namespace Wander.Core.Operations;

/// <summary>
/// One thing an operation was asked to do and how it went - one per item
/// the user counted: a file with its companions is one item, under its main
/// file. Strings and numbers only: the tracker keeps fifty finished
/// operations for the session, and an entry or a bitmap held here would
/// keep far more alive than the line on screen needs.
/// </summary>
/// <param name="Destination">Where it went or was going; null for a delete.</param>
/// <param name="Error">Why it failed - never null on Failed; a skip may carry one too.</param>
/// <param name="Attempts">How many times it was tried: more than one after a transient error.</param>
/// <param name="Verified">Its copy was read back and matched.</param>
/// <param name="Engine">What moved its bytes - a complaint about a copy is told apart by it.</param>
public sealed record OperationItem(
    string Source, string? Destination, BatchItemStatus Status, Exception? Error, int Attempts = 1,
    bool Verified = false, CopyEngine Engine = CopyEngine.None) {

    /// <summary>Failed, or skipped for a reason - what the finished row names for the user to look at.</summary>
    public bool IsProblem => Status == BatchItemStatus.Failed || (Status == BatchItemStatus.Skipped && Error is not null);

    /// <summary>Did not get where it was going - what "Повторить" takes.</summary>
    public bool IsRetriable => Status is BatchItemStatus.Failed or BatchItemStatus.Cancelled;

    /// <summary>Got where it was going, under whatever name the conflict answer gave it.</summary>
    public bool IsDone => Status is BatchItemStatus.Ok or BatchItemStatus.Replaced or BatchItemStatus.Renamed or BatchItemStatus.Merged;
}


/// <summary>How a finished operation went, as a whole.</summary>
public enum OperationOutcome {
    /// <summary>Everything asked for is done, or skipped on the user's answer.</summary>
    Done,

    /// <summary>Some of it is done, and something failed or was skipped for a reason.</summary>
    Problems,

    /// <summary>Stopped on the user's word, nothing failed.</summary>
    Cancelled,

    /// <summary>Something failed, and nothing is done.</summary>
    Failed,
}


/// <summary>
/// A finished operation as the status bar keeps it until the user clears it
/// away: what it was, how it ended, the counts, and the items - the first of
/// them and every one that did not get there. Problem items are never cut:
/// they are what "Повторить" takes and what the row lists.
/// </summary>
/// <param name="Bytes">What it moved; 0 for a delete and for an operation that counts work, not bytes.</param>
/// <param name="Target">The folder it went into; null for a delete.</param>
/// <param name="ItemsTruncated">How many items were left out of <see cref="Items"/> - none of them failed or cancelled.</param>
/// <param name="Verified">How many items were read back and matched.</param>
public sealed record OperationRecord(
    long Id, string Verb, OperationOutcome Outcome, int Total, int Done, int Failed, int Skipped, int Cancelled,
    long Bytes, TimeSpan Elapsed, DateTime FinishedAtUtc, string? Target, IReadOnlyList<OperationItem> Items,
    int ItemsTruncated = 0, int Verified = 0) {

    /// <summary>How many items a record keeps besides the ones with a problem.</summary>
    public const int MaxItems = 200;


    /// <summary>Failed outright or in part - what "убрать завершённые" leaves for the user to read.</summary>
    public bool HasProblems => Outcome is OperationOutcome.Problems or OperationOutcome.Failed;


    /// <summary>
    /// The drives the operation touched - "E:\" for every item that came
    /// from or went to E:, and the target's - in the order first met: what
    /// a finished row's "Извлечь" may be about. A share or a shell place
    /// has no drive letter and is not among them.
    /// </summary>
    public IReadOnlyList<string> DriveRoots() {
        var roots = new List<string>();
        void Add(string? path) {
            if (path is { Length: >= 2 } && char.IsAsciiLetter(path[0]) && path[1] == ':') {
                string root = char.ToUpperInvariant(path[0]) + @":\";
                if (!roots.Contains(root)) {
                    roots.Add(root);
                }
            }
        }

        foreach (var item in Items) {
            Add(item.Source);
            Add(item.Destination);
        }
        Add(Target);

        return roots;
    }


    /// <summary>
    /// The record of an operation that reported its items. The outcome is
    /// worked out from them: a failure (or a skip with a reason) beside
    /// something done is "problems", without anything done "failed"; a
    /// cancel with nothing failed is "cancelled"; the rest is "done".
    /// </summary>
    public static OperationRecord FromItems(
        long id, string verb, IReadOnlyList<OperationItem> items, long bytes, TimeSpan elapsed, DateTime finishedAtUtc,
        string? target) {
        int done = 0;
        int failed = 0;
        int skipped = 0;
        int cancelled = 0;
        int verified = 0;
        bool problems = false;
        foreach (var item in items) {
            if (item.IsDone) {
                done++;
            }
            if (item.Verified) {
                verified++;
            }
            switch (item.Status) {
                case BatchItemStatus.Failed:
                    failed++;
                    break;
                case BatchItemStatus.Skipped:
                    skipped++;
                    break;
                case BatchItemStatus.Cancelled:
                    cancelled++;
                    break;
            }
            problems |= item.IsProblem;
        }

        var outcome = problems ? (done > 0 ? OperationOutcome.Problems : OperationOutcome.Failed)
            : cancelled > 0 ? OperationOutcome.Cancelled
            : OperationOutcome.Done;
        var kept = Keep(items);

        return new OperationRecord(
            id, verb, outcome, items.Count, done, failed, skipped, cancelled, bytes, elapsed, finishedAtUtc, target,
            kept, items.Count - kept.Count, verified);
    }

    /// <summary>
    /// The record of an operation that closed without saying how each item
    /// went - an extraction, an undo, an action: done when its token was
    /// not cancelled, cancelled when it was, with the counts it advanced.
    /// </summary>
    public static OperationRecord Unitemised(
        long id, string verb, int total, int completed, bool cancelled, long bytes, TimeSpan elapsed,
        DateTime finishedAtUtc) {
        return new OperationRecord(
            id, verb, cancelled ? OperationOutcome.Cancelled : OperationOutcome.Done, total, completed, 0, 0,
            cancelled ? Math.Max(0, total - completed) : 0, bytes, elapsed, finishedAtUtc, null,
            Array.Empty<OperationItem>());
    }


    /// <summary>Every item that did not simply get there, and the first of the rest up to <see cref="MaxItems"/>, in order.</summary>
    private static IReadOnlyList<OperationItem> Keep(IReadOnlyList<OperationItem> items) {
        if (items.Count <= MaxItems) {
            return items.ToArray();
        }

        int room = Math.Max(0, MaxItems - items.Count(Notable));
        var kept = new List<OperationItem>();
        foreach (var item in items) {
            if (Notable(item)) {
                kept.Add(item);
            } else if (room > 0) {
                kept.Add(item);
                room--;
            }
        }

        return kept;
    }

    private static bool Notable(OperationItem item) => item.IsProblem || item.IsRetriable;
}
