using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class PathSafetyTests {
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
        string text = PathSafety.FormatReason(SelfDropReason.Same, PhotosYear, PhotosYear);

        Assert.Equal("Cannot move '2024' onto itself", text);
    }

    [Fact]
    public void FormatReason_AlreadyInTarget_NamesBothEnds() {
        string text = PathSafety.FormatReason(SelfDropReason.AlreadyInTarget, PhotosYear, PhotosRoot);

        Assert.Equal("'2024' is already in 'photos'", text);
    }

    [Fact]
    public void FormatReason_IntoOwnDescendant_NamesSubfolder() {
        string text = PathSafety.FormatReason(SelfDropReason.IntoOwnDescendant, PhotosRoot, PhotosYear);

        Assert.Equal("Cannot move 'photos' into its own subfolder '2024'", text);
    }

    [Fact]
    public void FormatReason_None_GenericFallback() {
        string text = PathSafety.FormatReason(SelfDropReason.None, null, TargetRoot);

        Assert.Equal("Cannot drop here", text);
    }

    [Fact]
    public void FormatReason_NullOffender_UsesThisPlaceholder() {
        string text = PathSafety.FormatReason(SelfDropReason.Same, null, TargetRoot);

        Assert.Equal("Cannot move 'this' onto itself", text);
    }

    [Fact]
    public void FormatReason_TargetIsDriveRoot_FallsBackToFullPath() {
        // Path.GetFileName(@"C:\") is empty — the formatter should fall back to
        // the raw target string so the message doesn't read "in ''".
        string text = PathSafety.FormatReason(SelfDropReason.AlreadyInTarget, PhotosRoot, DriveCRoot);

        Assert.Equal($"'photos' is already in '{DriveCRoot}'", text);
    }
}
