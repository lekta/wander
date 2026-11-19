using System.Text;
using Wander.Core.Companions;
using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

/// <summary>
/// The undo of a sidecar Wander created (audit 2026-11-18, F1): its own file
/// goes, a file an editor has written into since stays, with only what
/// Wander wrote and nobody changed taken out of it.
/// </summary>
public class CreatedSidecarTests {
    private static readonly byte[] _written = Encoding.UTF8.GetBytes("[General]\r\nRank=5\r\nColorLabel=0\r\n");


    [Fact]
    public void TheSameBytes_AreDeleted() {
        var created = CreatedSidecar.Of(_written, rank: 5, colorLabel: 0);

        var plan = created.PlanUndo(_written.ToArray(), new SidecarRating(5, 0));

        Assert.True(plan.Delete);
        Assert.Empty(plan.Strip);
    }

    [Fact]
    public void AChangedFile_IsKept_AndLosesBothFieldsWhenTheyAreStillWandersOwn() {
        var created = CreatedSidecar.Of(_written, rank: 5, colorLabel: 0);
        byte[] edited = Encoding.UTF8.GetBytes("[General]\r\nRank=5\r\nColorLabel=0\r\n\r\n[Exposure]\r\nCompensation=0.35\r\n");

        var plan = created.PlanUndo(edited, new SidecarRating(5, 0));

        Assert.False(plan.Delete);
        Assert.Equal(new[] { RatingField.Rank, RatingField.ColorLabel }, plan.Strip);
    }

    [Fact]
    public void AFieldTheEditorChanged_IsLeftAlone() {
        var created = CreatedSidecar.Of(_written, rank: 5, colorLabel: 0);
        byte[] edited = Encoding.UTF8.GetBytes("[General]\r\nRank=3\r\nColorLabel=0\r\n\r\n[Exposure]\r\nCompensation=0.35\r\n");

        var plan = created.PlanUndo(edited, new SidecarRating(3, 0));

        Assert.False(plan.Delete);
        Assert.Equal(new[] { RatingField.ColorLabel }, plan.Strip);
    }

    [Fact]
    public void ACustomLabel_IsNotTakenForNoColour() {
        // An XMP label outside the five colours reads as index 0, the same
        // number as the "no colour" Wander wrote: the name tells them apart.
        var created = CreatedSidecar.Of(_written, rank: 5, colorLabel: 0);

        var plan = created.PlanUndo(new byte[] { 1, 2, 3 }, new SidecarRating(5, 0, "Approved"));

        Assert.Equal(new[] { RatingField.Rank }, plan.Strip);
    }

    [Fact]
    public void AChangedFileThatDoesNotParse_IsNotTouched() {
        var created = CreatedSidecar.Of(_written, rank: 5, colorLabel: 0);

        var plan = created.PlanUndo(new byte[] { 0xFF, 0xFE, 0x00 }, read: null);

        Assert.False(plan.Delete);
        Assert.Empty(plan.Strip);
    }
}
