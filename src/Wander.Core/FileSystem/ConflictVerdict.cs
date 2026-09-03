namespace Wander.Core.FileSystem;

/// <summary>Which side of a collision is the more recent one.</summary>
public enum ConflictAge {
    /// <summary>Same date, within the tolerance a copy keeps it at.</summary>
    Same,
    SourceNewer,
    TargetNewer,
    /// <summary>One side reported no date (an archive entry, a shell item).</summary>
    Unknown,
}


/// <summary>
/// What the two sides of a collision have in common, worked out from the
/// facts the listing already carries plus, when somebody paid for it, the
/// bytes. Lives here rather than in the window because the answers drive
/// decisions - "skip the identical ones" is a rule, not a caption - and a
/// rule wants a test.
/// </summary>
/// <param name="SameKind">Both files, or both folders. A file over a folder is never "the same thing".</param>
/// <param name="SameSize">null when either side has no size: folders, entries nobody could stat.</param>
/// <param name="Age">Who is newer.</param>
/// <param name="AgeDifference">By how much; zero when <see cref="Age"/> is Same or Unknown.</param>
/// <param name="Identical">
/// true / false once the bytes were compared; false straight away when
/// kinds or sizes differ, without reading anything; null while the
/// question is open - two files of one size nobody has read yet, or two
/// folders, which are not compared at all.
/// </param>
/// <param name="SourceReachable">
/// The bytes can be read at all - false for an archive entry, and for a
/// file that refused to open when somebody tried. Such a pair stays open on
/// content for good, and the window stops saying it is comparing.
/// </param>
public sealed record ConflictVerdict(
    bool SameKind,
    bool? SameSize,
    ConflictAge Age,
    TimeSpan AgeDifference,
    bool? Identical,
    bool SourceReachable = true) {

    /// <summary>
    /// FAT keeps modification times to two seconds, and a copy across such
    /// a volume lands within that of its original; anything closer than
    /// this is "the same date" to a person reading the window.
    /// </summary>
    public static readonly TimeSpan AgeTolerance = TimeSpan.FromSeconds(2);


    /// <summary>
    /// Is reading the bytes worth anything? Only when the facts leave the
    /// question open: two files of the same size that nobody compared, and
    /// the source is somewhere a read can reach.
    /// </summary>
    public bool ContentUndecided => SourceReachable && Identical is null && SameSize == true;


    /// <param name="sameContent">
    /// The outcome of <see cref="FileContentComparer"/> when it ran; null
    /// when it did not. Ignored when the facts already settle the answer.
    /// </param>
    public static ConflictVerdict Of(FileConflictInfo conflict, bool? sameContent = null) {
        var source = conflict.Source;
        var target = conflict.ExistingTarget;

        bool sameKind = source.Kind == target.Kind;
        bool? sameSize = source.Size is { } a && target.Size is { } b ? a == b : null;

        bool? identical;
        if (!sameKind || sameSize == false) {
            identical = false;
        } else if (source.Kind == EntryKind.File && sameSize == true) {
            identical = sameContent;
        } else {
            identical = null;
        }

        var (age, difference) = CompareDates(source.ModifiedUtc, target.ModifiedUtc);

        return new ConflictVerdict(sameKind, sameSize, age, difference, identical, conflict.SourceReachable);
    }


    private static (ConflictAge Age, TimeSpan Difference) CompareDates(DateTime source, DateTime target) {
        // MinValue is how a reader says "no date" (FileSystemEntry has no
        // room for null there): comparing against it would call every file
        // two thousand years newer than an archive entry.
        if (source == DateTime.MinValue || target == DateTime.MinValue) {
            return (ConflictAge.Unknown, TimeSpan.Zero);
        }

        var delta = source - target;
        if (delta.Duration() <= AgeTolerance) {
            return (ConflictAge.Same, TimeSpan.Zero);
        }

        return delta > TimeSpan.Zero
            ? (ConflictAge.SourceNewer, delta)
            : (ConflictAge.TargetNewer, -delta);
    }
}
