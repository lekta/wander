using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class ConflictVerdictTests {
    private static readonly DateTime _noon = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);


    private static FileSystemEntry SomeFile(long? size, DateTime modified) {
        return new FileSystemEntry("a.txt", @"C:\src\a.txt", EntryKind.File, size, modified, false, false, false, false);
    }

    private static FileSystemEntry SomeFolder(DateTime modified) {
        return new FileSystemEntry("a", @"C:\src\a", EntryKind.Directory, null, modified, false, false, false, false);
    }

    private static ConflictVerdict Verdict(FileSystemEntry source, FileSystemEntry target, bool? sameContent = null) {
        return ConflictVerdict.Of(new FileConflictInfo(source, target), sameContent);
    }


    // --- Kind and size ---------------------------------------------------

    [Fact]
    public void SameSizeFiles_LeaveContentOpen_UntilSomebodyReads() {
        var verdict = Verdict(SomeFile(10, _noon), SomeFile(10, _noon));

        Assert.True(verdict.SameKind);
        Assert.True(verdict.SameSize);
        Assert.Null(verdict.Identical);
        Assert.True(verdict.ContentUndecided);
    }

    [Fact]
    public void DifferentSizes_SettleContent_WithoutReading() {
        // A reader that claims "same" over two sizes is wrong; the facts win.
        var verdict = Verdict(SomeFile(10, _noon), SomeFile(11, _noon), sameContent: true);

        Assert.False(verdict.SameSize);
        Assert.False(verdict.Identical);
        Assert.False(verdict.ContentUndecided);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameSize_TakesTheReadersWord(bool same) {
        var verdict = Verdict(SomeFile(10, _noon), SomeFile(10, _noon), sameContent: same);

        Assert.Equal(same, verdict.Identical);
        Assert.False(verdict.ContentUndecided);
    }

    [Fact]
    public void FileOverFolder_IsNeverTheSameThing() {
        var verdict = Verdict(SomeFile(10, _noon), SomeFolder(_noon), sameContent: true);

        Assert.False(verdict.SameKind);
        Assert.Null(verdict.SameSize);
        Assert.False(verdict.Identical);
    }

    [Fact]
    public void TwoFolders_AreNotCompared() {
        var verdict = Verdict(SomeFolder(_noon), SomeFolder(_noon), sameContent: true);

        Assert.True(verdict.SameKind);
        Assert.Null(verdict.SameSize);
        Assert.Null(verdict.Identical);
        Assert.False(verdict.ContentUndecided);
    }

    [Fact]
    public void UnknownSize_LeavesEverythingOpen() {
        var verdict = Verdict(SomeFile(null, _noon), SomeFile(10, _noon));

        Assert.Null(verdict.SameSize);
        Assert.Null(verdict.Identical);
        Assert.False(verdict.ContentUndecided);
    }


    [Fact]
    public void UnreachableSource_IsNeverWorthReading() {
        // An archive entry: name, size and date are all there is.
        var conflict = new FileConflictInfo(SomeFile(10, _noon), SomeFile(10, _noon), SourceReachable: false);

        var verdict = ConflictVerdict.Of(conflict);

        Assert.True(verdict.SameSize);
        Assert.Null(verdict.Identical);
        Assert.False(verdict.ContentUndecided);
    }


    // --- Dates -----------------------------------------------------------

    [Fact]
    public void SourceNewer_SaysByHowMuch() {
        var verdict = Verdict(SomeFile(10, _noon.AddDays(3)), SomeFile(10, _noon));

        Assert.Equal(ConflictAge.SourceNewer, verdict.Age);
        Assert.Equal(TimeSpan.FromDays(3), verdict.AgeDifference);
    }

    [Fact]
    public void TargetNewer_DifferenceIsPositiveToo() {
        var verdict = Verdict(SomeFile(10, _noon), SomeFile(10, _noon.AddHours(5)));

        Assert.Equal(ConflictAge.TargetNewer, verdict.Age);
        Assert.Equal(TimeSpan.FromHours(5), verdict.AgeDifference);
    }

    [Fact]
    public void WithinFatResolution_IsTheSameDate() {
        // A copy onto a FAT volume lands up to two seconds off its original.
        var verdict = Verdict(SomeFile(10, _noon.AddSeconds(2)), SomeFile(10, _noon));

        Assert.Equal(ConflictAge.Same, verdict.Age);
        Assert.Equal(TimeSpan.Zero, verdict.AgeDifference);
    }

    [Fact]
    public void NoDateOnOneSide_IsUnknown_NotTwoThousandYears() {
        var verdict = Verdict(SomeFile(10, _noon), SomeFile(10, DateTime.MinValue));

        Assert.Equal(ConflictAge.Unknown, verdict.Age);
        Assert.Equal(TimeSpan.Zero, verdict.AgeDifference);
    }
}
