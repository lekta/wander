using Wander.Core.Shell;

namespace Wander.Core.Tests;

/// <summary>
/// A temporary copy out of an archive, before it is made again (audit
/// 2026-11-18, F4): one the user has edited since it was made is not the
/// archive's any more, and must not be deleted under them.
/// </summary>
public class TempCopyStateTests {
    private static readonly DateTime _t = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void NoFile_IsMissing() {
        Assert.Equal(TempCopyState.Missing, TempExtraction.Judge(new TempCopyStamp(10, _t), onDisk: null));
    }

    [Fact]
    public void AsMade_IsFresh() {
        Assert.Equal(TempCopyState.Fresh, TempExtraction.Judge(new TempCopyStamp(10, _t), new TempCopyStamp(10, _t)));
    }

    [Fact]
    public void ADifferentSizeOrTime_IsEdited() {
        Assert.Equal(TempCopyState.Edited, TempExtraction.Judge(new TempCopyStamp(10, _t), new TempCopyStamp(12, _t)));
        Assert.Equal(TempCopyState.Edited, TempExtraction.Judge(new TempCopyStamp(10, _t), new TempCopyStamp(10, _t.AddSeconds(5))));
    }

    [Fact]
    public void ACopyThisSessionDidNotMake_IsUnrecorded() {
        Assert.Equal(TempCopyState.Unrecorded, TempExtraction.Judge(recorded: null, new TempCopyStamp(10, _t)));
    }
}
