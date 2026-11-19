using Wander.Core.Persistence;

namespace Wander.Core.Tests;

/// <summary>
/// What a data file's store does with how its read went (audit 2026-11-18,
/// P6/V14/L8): a file that did not read is never written over as it is.
/// </summary>
public class StoreReadTests {

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoFile_StartsFromDefaults_AndWrites(bool firstRead) {
        Assert.Equal(
            new StoreReadPlan(UseDefaults: true, MayWrite: true, MoveAsideFirst: false),
            StoreRead.Plan(StoreReadOutcome.Missing, firstRead));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFileThatRead_IsUsed_AndWritten(bool firstRead) {
        Assert.Equal(
            new StoreReadPlan(UseDefaults: false, MayWrite: true, MoveAsideFirst: false),
            StoreRead.Plan(StoreReadOutcome.Read, firstRead));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABrokenFile_StartsFromDefaults_AndIsMovedAsideBeforeTheFirstWrite(bool firstRead) {
        Assert.Equal(
            new StoreReadPlan(UseDefaults: true, MayWrite: true, MoveAsideFirst: true),
            StoreRead.Plan(StoreReadOutcome.Broken, firstRead));
    }

    [Fact]
    public void AFileThatCouldNotBeOpenedAtTheFirstRead_ClosesWritingForTheSession() {
        // The model was built from the defaults: a write from it once the
        // file opens again would put them in place of the bookmarks.
        Assert.Equal(
            new StoreReadPlan(UseDefaults: true, MayWrite: false, MoveAsideFirst: false, ClosedForSession: true),
            StoreRead.Plan(StoreReadOutcome.Unavailable, firstRead: true));
    }

    [Fact]
    public void AFileThatCouldNotBeOpenedLater_ClosesWritingUntilAReadGetsThrough() {
        Assert.Equal(
            new StoreReadPlan(UseDefaults: true, MayWrite: false, MoveAsideFirst: false, ClosedForSession: false),
            StoreRead.Plan(StoreReadOutcome.Unavailable, firstRead: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFileOfANewerShape_IsReadAsItGoes_AndNeitherWrittenNorMoved(bool firstRead) {
        Assert.Equal(
            new StoreReadPlan(UseDefaults: false, MayWrite: false, MoveAsideFirst: false),
            StoreRead.Plan(StoreReadOutcome.Newer, firstRead));
    }
}
