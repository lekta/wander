using System.Windows;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

/// <summary>
/// One name collision in the conflict window: two cards, each with a tick
/// that means "keep this one". The four states of the pair are the four
/// combinations - source only replaces, target only leaves things as they
/// are, both keeps both (a file under a new name, two folders merged),
/// neither is an open question.
///
/// <para>
/// Ticks rather than buttons because a button reads as an action taken, and
/// nothing here is taken until the window's own OK; a tick reads as a
/// choice, and can be unticked. The answer is also written after the name
/// in a word, so a long list reads without looking at the ticks. What the
/// two sides have in common is shown by weight rather than said in a
/// sentence: the bigger size and the newer date are bold, and there is a
/// line of text only where neither number can carry it.
/// </para>
/// </summary>
public sealed class ConflictRowViewModel : ObservableObject {
    private readonly ConflictWindowViewModel _owner;


    public ConflictRowViewModel(ConflictWindowViewModel owner, ConflictPair pair) {
        _owner = owner;
        Pair = pair;
    }


    /// <summary>The pair this row stands for - what an answer answers for.</summary>
    public ConflictPair Pair { get; }

    /// <summary>
    /// Rows under a merged folder step in, one step per level; the gap
    /// below separates rows from each other.
    /// </summary>
    public Thickness Indent => new(Pair.Depth * 20, 0, 0, 14);


    // --- The choice, as two ticks ----------------------------------------

    /// <summary>Keep what we are copying: on its own that is a replace.</summary>
    public bool TakeSource {
        get => Choice is ConflictResolution.Replace or ConflictResolution.Rename or ConflictResolution.Merge;
        set => Set(value, TakeTarget);
    }

    /// <summary>Keep what is already there.</summary>
    public bool TakeTarget {
        get => Choice is ConflictResolution.Skip or ConflictResolution.Rename or ConflictResolution.Merge;
        set => Set(TakeSource, value);
    }

    /// <summary>
    /// The side that lost fades. Only while exactly one is ticked: with
    /// both ticked nothing is being dropped, with neither the question is
    /// still open and dimming half of it would read as an answer.
    /// </summary>
    public bool IsSourceDimmed => TakeTarget && !TakeSource;

    public bool IsTargetDimmed => TakeSource && !TakeTarget;

    /// <summary>
    /// The answer in a word after the name; for a merge, what is inside -
    /// how many names collide and how many files simply cross over - or
    /// that it is still being read. Empty while the pair is open.
    /// </summary>
    public string ChoiceText {
        get {
            return Choice switch {
                ConflictResolution.Replace => Strings.ConflictChoiceReplace,
                ConflictResolution.Skip => Strings.ConflictChoiceKeep,
                ConflictResolution.Rename => Strings.ConflictChoiceBoth,
                ConflictResolution.Merge => Pair.Scan switch {
                    MergeScanState.Scanned => string.Format(Strings.ConflictMergeSummary, Pair.InnerConflicts, Pair.FreeFiles),
                    MergeScanState.Failed => Strings.ConflictMergeFailed,
                    _ => Strings.ConflictMergeScanning,
                },
                _ => "",
            };
        }
    }

    public bool HasChoiceText => ChoiceText.Length > 0;


    // --- The two sides ---------------------------------------------------

    /// <summary>The name; below a merged folder, the path from that folder down.</summary>
    public string Name => Pair.DisplayPath;

    public string SourcePath => Conflict.Source.FullPath;
    public string SourceSize => DescribeSize(Conflict.Source.Size);
    public string SourceModified => TimeFormat.FromUtc(Conflict.Source.ModifiedUtc);

    public string TargetPath => Conflict.ExistingTarget.FullPath;
    public string TargetSize => DescribeSize(Conflict.ExistingTarget.Size);
    public string TargetModified => TimeFormat.FromUtc(Conflict.ExistingTarget.ModifiedUtc);


    /// <summary>
    /// The bigger of the two sizes, and the newer of the two dates, are the
    /// difference - so they are the ones drawn bold. Nothing is bold when
    /// the two agree.
    /// </summary>
    public bool IsSourceBigger => Bigger(Conflict.Source.Size, Conflict.ExistingTarget.Size);

    public bool IsTargetBigger => Bigger(Conflict.ExistingTarget.Size, Conflict.Source.Size);

    public bool IsSourceNewer => Verdict.Age == ConflictAge.SourceNewer;

    public bool IsTargetNewer => Verdict.Age == ConflictAge.TargetNewer;


    // --- What the numbers cannot say -------------------------------------

    /// <summary>
    /// What the sizes and dates do not already show: two files that turned
    /// out to be the same, two of one size that turned out not to be, and a
    /// file meeting a folder. Empty the rest of the time - and it rides on
    /// the name line rather than taking one of its own.
    /// </summary>
    public string VerdictText {
        get {
            if (!Verdict.SameKind) {
                return Strings.ConflictVerdictDifferentKind;
            }

            return Verdict switch {
                { Identical: true } => Strings.ConflictVerdictIdentical,
                { Identical: false, SameSize: true } => Strings.ConflictVerdictContentDiffers,
                _ => "",
            };
        }
    }

    public bool HasVerdictText => VerdictText.Length > 0;

    /// <summary>
    /// Replacing a folder is not what Explorer does to a folder, and the
    /// difference costs data. Said on the row - but only once replace is
    /// what the row says.
    /// </summary>
    public bool IsFolderReplace => Pair.IsFolderPair && Choice == ConflictResolution.Replace;

    /// <summary>
    /// The bytes are on their way: two files of one size the window has
    /// not read yet, queued or being read right now.
    /// </summary>
    public bool IsComparing => Verdict.ContentUndecided;


    private FileConflictInfo Conflict => Pair.Conflict;

    private ConflictVerdict Verdict => Pair.Verdict;

    private ConflictResolution? Choice => Pair.Choice;


    /// <summary>The comparison landed: redraw the verdict and what it enables.</summary>
    public void RefreshVerdict() {
        Raise(nameof(VerdictText));
        Raise(nameof(HasVerdictText));
        Raise(nameof(IsComparing));
    }

    /// <summary>
    /// The answer changed from outside - a quick answer for the rest of the
    /// list, the "skip identical" policy acting on a comparison, a merge
    /// that finished reading.
    /// </summary>
    public void RefreshChoice() {
        Raise(nameof(TakeSource));
        Raise(nameof(TakeTarget));
        Raise(nameof(IsSourceDimmed));
        Raise(nameof(IsTargetDimmed));
        Raise(nameof(ChoiceText));
        Raise(nameof(HasChoiceText));
        Raise(nameof(IsFolderReplace));
    }


    private void Set(bool takeSource, bool takeTarget) {
        ConflictResolution? choice = (takeSource, takeTarget) switch {
            (true, true) => Pair.CanMerge ? ConflictResolution.Merge : ConflictResolution.Rename,
            (true, false) => ConflictResolution.Replace,
            (false, true) => ConflictResolution.Skip,
            _ => null,
        };

        _owner.Batch.Choose(Pair, choice);
        RefreshChoice();
        _owner.OnChoiceChanged(this);
    }

    private static bool Bigger(long? one, long? other) {
        return one is { } a && other is { } b && a > b;
    }

    /// <summary>
    /// Sizes are formatted the same way everywhere; the only thing this
    /// window says differently is what "no size" means. A folder has none,
    /// and here that is worth saying in words - elsewhere an em dash is
    /// enough, because the row already shows the kind.
    /// </summary>
    private static string DescribeSize(long? size) {
        return size is { } bytes ? SizeFormatter.Format(bytes) : Strings.KindFolderNoun;
    }
}
