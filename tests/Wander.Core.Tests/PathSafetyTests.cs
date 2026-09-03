using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class PathSafetyTests {
    /// <summary>
    /// Structural templates instead of the real Russian copy: what these
    /// tests are for is which name lands in which slot, and asserting the
    /// wording would only mean re-typing it here every time it is edited.
    /// </summary>
    private static readonly FakeTextSource _text = new(new Dictionary<string, string> {
        ["DropThis"] = "<unnamed>",
        ["DropOntoItself"] = "same({0})",
        ["DropAlreadyThere"] = "already({0}|{1})",
        ["DropIntoOwnSubfolder"] = "descendant({0}|{1})",
        ["DropNotAllowed"] = "generic",
    });

    // --- Paths reused across cases ------------------------------------
    // PhotosRoot/PhotosYear/PhotosYearTrip form a parent/child/grandchild
    // chain so the same fixture covers Same, AlreadyInTarget, and
    // IntoOwnDescendant. Other consts cover the corner cases.
    private const string PhotosRoot = @"C:\photos";
    private const string PhotosYear = @"C:\photos\2024";
    private const string PhotosYearTrip = @"C:\photos\2024\trip";
    private const string PhotosTrip = @"C:\photos\trip";
    private const string PhotosRootWithSlash = @"C:\photos\";
    private const string PhotosYearWithSlash = @"C:\photos\2024\";
    private const string PhotosYearWithForwardSlash = @"C:\photos\2024/";
    private const string PhotosRootUpper = @"c:\PHOTOS\2024";
    private const string PhotosBackup = @"C:\photos_backup";
    private const string UnrelatedFile = @"C:\unrelated\file.txt";

    private const string BackupRoot = @"D:\backup";
    private const string DriveCRoot = @"C:\";
    private const string AnywhereRoot = @"C:\anywhere";
    private const string TargetRoot = @"C:\target";


    // --- DetectSelfDrop -------------------------------------------------

    [Fact]
    public void DetectSelfDrop_DroppingItemOntoItself_ReturnsSame() {
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosYear }, PhotosYear, out string? offender);

        Assert.Equal(SelfDropReason.Same, reason);
        Assert.Equal(PhotosYear, offender);
    }

    [Fact]
    public void DetectSelfDrop_DroppingIntoOwnParentFolder_ReturnsAlreadyInTarget() {
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosYear }, PhotosRoot, out string? offender);

        Assert.Equal(SelfDropReason.AlreadyInTarget, reason);
        Assert.Equal(PhotosYear, offender);
    }

    [Fact]
    public void DetectSelfDrop_DroppingFolderIntoOwnSubfolder_ReturnsIntoOwnDescendant() {
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosRoot }, PhotosYearTrip, out string? offender);

        Assert.Equal(SelfDropReason.IntoOwnDescendant, reason);
        Assert.Equal(PhotosRoot, offender);
    }

    [Fact]
    public void DetectSelfDrop_DroppingFolderIntoUnrelatedTarget_ReturnsNone() {
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosTrip }, BackupRoot, out string? offender);

        Assert.Equal(SelfDropReason.None, reason);
        Assert.Null(offender);
    }

    [Fact]
    public void DetectSelfDrop_IsCaseInsensitive() {
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosYear }, PhotosRootUpper, out string? offender);

        Assert.Equal(SelfDropReason.Same, reason);
        Assert.NotNull(offender);
    }

    [Fact]
    public void DetectSelfDrop_TrailingSlashOnSource_NormalisesAway() {
        // Source has trailing backslash; should not change the verdict.
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosYearWithSlash }, PhotosRoot, out _);

        Assert.Equal(SelfDropReason.AlreadyInTarget, reason);
    }

    [Fact]
    public void DetectSelfDrop_TrailingSlashOnTarget_NormalisesAway() {
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosYear }, PhotosRootWithSlash, out _);

        Assert.Equal(SelfDropReason.AlreadyInTarget, reason);
    }

    [Fact]
    public void DetectSelfDrop_SiblingSubstringMatch_IsNotMisclassifiedAsDescendant() {
        // Common bug: prefix-match "C:\photos" against target "C:\photos_backup"
        // (without the path-separator check) would falsely report descendant.
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosRoot }, PhotosBackup, out string? offender);

        Assert.Equal(SelfDropReason.None, reason);
        Assert.Null(offender);
    }

    [Fact]
    public void DetectSelfDrop_MultipleSources_FirstOffenderWins() {
        // The first source that triggers a reason should be the reported offender.
        var sources = new[] {
            UnrelatedFile,
            PhotosTrip,       // triggers AlreadyInTarget into C:\photos
            PhotosRoot,       // would trigger IntoOwnDescendant for a deeper target
        };

        var reason = PathSafety.DetectSelfDrop(sources, PhotosRoot, out string? offender);

        Assert.Equal(SelfDropReason.AlreadyInTarget, reason);
        Assert.Equal(PhotosTrip, offender);
    }

    [Fact]
    public void DetectSelfDrop_EmptySources_ReturnsNone() {
        var reason = PathSafety.DetectSelfDrop(Array.Empty<string>(), AnywhereRoot, out string? offender);

        Assert.Equal(SelfDropReason.None, reason);
        Assert.Null(offender);
    }

    [Fact]
    public void DetectSelfDrop_ForwardSlashOnSource_NormalisesAway() {
        // Path.GetDirectoryName treats / as a separator on Windows; trailing
        // forward slash should be trimmed too so the parent check matches.
        var reason = PathSafety.DetectSelfDrop(new[] { PhotosYearWithForwardSlash }, PhotosRoot, out _);

        Assert.Equal(SelfDropReason.AlreadyInTarget, reason);
    }


    // --- FormatReason --------------------------------------------------

    [Fact]
    public void FormatReason_Same_UsesOffenderLeafName() {
        string text = PathSafety.FormatReason(SelfDropReason.Same, PhotosYear, PhotosYear, _text);

        Assert.Equal("same(2024)", text);
    }

    [Fact]
    public void FormatReason_AlreadyInTarget_NamesBothEnds() {
        string text = PathSafety.FormatReason(SelfDropReason.AlreadyInTarget, PhotosYear, PhotosRoot, _text);

        Assert.Equal("already(2024|photos)", text);
    }

    [Fact]
    public void FormatReason_IntoOwnDescendant_NamesSubfolder() {
        string text = PathSafety.FormatReason(SelfDropReason.IntoOwnDescendant, PhotosRoot, PhotosYear, _text);

        Assert.Equal("descendant(photos|2024)", text);
    }

    [Fact]
    public void FormatReason_None_GenericFallback() {
        string text = PathSafety.FormatReason(SelfDropReason.None, null, TargetRoot, _text);

        Assert.Equal("generic", text);
    }

    [Fact]
    public void FormatReason_NullOffender_UsesAPlaceholderName() {
        string text = PathSafety.FormatReason(SelfDropReason.Same, null, TargetRoot, _text);

        Assert.Equal("same(<unnamed>)", text);
    }

    // --- The one self-drop that is not a refusal -------------------------

    [Theory]
    [InlineData(SelfDropReason.AlreadyInTarget, true, true)]
    [InlineData(SelfDropReason.AlreadyInTarget, false, false)]
    [InlineData(SelfDropReason.Same, true, false)]
    [InlineData(SelfDropReason.IntoOwnDescendant, true, false)]
    [InlineData(SelfDropReason.None, true, false)]
    public void IsAllowedDuplicate_OnlyBackIntoItsOwnFolder_AndOnlyWhenItDuplicates(
        SelfDropReason reason, bool duplicates, bool expected) {
        // Dropping a file back where it lives with Ctrl held is how a copy
        // is made; the same gesture into a folder's own subtree, or onto
        // itself, still has no outcome.
        Assert.Equal(expected, PathSafety.IsAllowedDuplicate(reason, duplicates));
    }

    [Fact]
    public void FormatReason_TargetIsDriveRoot_FallsBackToFullPath() {
        // Path.GetFileName(@"C:\") is empty — the formatter should fall back to
        // the raw target string so the message doesn't read "in ''".
        string text = PathSafety.FormatReason(SelfDropReason.AlreadyInTarget, PhotosRoot, DriveCRoot, _text);

        Assert.Equal($"already(photos|{DriveCRoot})", text);
    }


    // --- A cut pasted back where it came from ---------------------------

    [Fact]
    public void AllAlreadyIn_IsTrue_OnlyWhenEveryPathLivesInTheTarget() {
        Assert.True(PathSafety.AllAlreadyIn(new[] { @"C:\photos\a.jpg", @"C:\photos\b.jpg" }, PhotosRoot));
        Assert.True(PathSafety.AllAlreadyIn(new[] { @"C:\photos\a.jpg" }, PhotosRootWithSlash));
        Assert.False(PathSafety.AllAlreadyIn(new[] { @"C:\photos\a.jpg", @"C:\photos\2024\b.jpg" }, PhotosRoot));
        Assert.False(PathSafety.AllAlreadyIn(Array.Empty<string>(), PhotosRoot));
    }
}
