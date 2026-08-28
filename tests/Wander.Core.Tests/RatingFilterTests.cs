using Wander.Core.Companions;
using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class RatingFilterTests {

    private static FileSystemEntry Photo(int? rank = null, int? color = null) {
        var rating = rank is null && color is null ? null : new SidecarRating(rank, color);

        return new FileSystemEntry(
            Name: "IMG_1234.CR3",
            FullPath: @"C:\shoot\IMG_1234.CR3",
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false,
            Rating: rating);
    }


    // --- The empty filter ------------------------------------------------

    [Fact]
    public void None_IsNotActiveAndPassesEverything() {
        Assert.False(RatingFilter.None.IsActive);
        Assert.True(RatingFilter.None.Matches(Photo()));
        Assert.True(RatingFilter.None.Matches(Photo(rank: 5, color: 2)));
    }

    [Fact]
    public void None_LightsNothing() {
        for (int star = 0; star <= RatingFilter.MaxRank; star++) {
            Assert.False(RatingFilter.None.HasRank(star));
        }
    }


    // --- A plain click on a star: that rank and above ---------------------

    [Fact]
    public void PickRank_TakesThatRankAndEverythingAbove() {
        var filter = RatingFilter.None.PickRank(3);

        Assert.True(filter.HasRank(3));
        Assert.True(filter.HasRank(4));
        Assert.True(filter.HasRank(5));
        Assert.False(filter.HasRank(2));
        Assert.False(filter.HasRank(RatingFilter.Unrated));
    }

    [Fact]
    public void PickRank_KeepsEqualAndAbove() {
        var filter = RatingFilter.None.PickRank(3);

        Assert.False(filter.Matches(Photo(rank: 2)));
        Assert.True(filter.Matches(Photo(rank: 3)));
        Assert.True(filter.Matches(Photo(rank: 5)));
    }

    [Fact]
    public void PickRank_DropsTheUnrated() {
        Assert.False(RatingFilter.None.PickRank(1).Matches(Photo()));
    }

    [Fact]
    public void PickRank_Twice_TurnsTheStarFilterOff() {
        // Clicking what is already set unsets it — the same gesture the
        // rating widget itself uses, and the only way to clear one half of
        // the bar without clearing the other.
        var filter = RatingFilter.None.PickRank(3).PickRank(3);

        Assert.Equal(0, filter.Ranks);
        Assert.False(filter.IsActive);
    }

    [Fact]
    public void PickRank_LeavesTheColoursAlone() {
        var filter = RatingFilter.None.PickColor(2).PickRank(4);

        Assert.True(filter.HasColor(2));
        Assert.True(filter.HasRank(4));
    }


    // --- The crossed-out star: unrated ------------------------------------

    [Fact]
    public void PickRank_Unrated_TakesOnlyTheUnrated() {
        // "Unrated and above" would be every photograph in the folder, which
        // is not a filter.
        var filter = RatingFilter.None.PickRank(RatingFilter.Unrated);

        Assert.True(filter.HasRank(RatingFilter.Unrated));
        Assert.False(filter.HasRank(1));
        Assert.False(filter.HasRank(5));
    }

    [Fact]
    public void Unrated_MatchesAPhotoWithNoSidecarAtAll() {
        var filter = RatingFilter.None.PickRank(RatingFilter.Unrated);

        Assert.True(filter.Matches(Photo()));
        Assert.False(filter.Matches(Photo(rank: 1)));
    }

    [Fact]
    public void Unrated_MatchesASidecarThatSaysZero() {
        // "No sidecar" and "a sidecar with no stars in it" are the same
        // statement about a photograph.
        var filter = RatingFilter.None.PickRank(RatingFilter.Unrated);

        Assert.True(filter.Matches(Photo(rank: 0)));
        Assert.True(filter.Matches(Photo(color: 3)));
    }


    // --- Ctrl-click: one rank in or out -----------------------------------

    [Fact]
    public void ToggleRank_TakesOneRankOutOfTheRun() {
        // The example from the report: three and up, then drop the fives.
        var filter = RatingFilter.None.PickRank(3).ToggleRank(5);

        Assert.True(filter.HasRank(3));
        Assert.True(filter.HasRank(4));
        Assert.False(filter.HasRank(5));
        Assert.True(filter.Matches(Photo(rank: 4)));
        Assert.False(filter.Matches(Photo(rank: 5)));
    }

    [Fact]
    public void ToggleRank_PutsItBack() {
        var filter = RatingFilter.None.PickRank(3).ToggleRank(5).ToggleRank(5);

        Assert.True(filter.HasRank(5));
    }

    [Fact]
    public void ToggleRank_CanBuildASetFromNothing() {
        var filter = RatingFilter.None.ToggleRank(2).ToggleRank(4);

        Assert.True(filter.Matches(Photo(rank: 2)));
        Assert.True(filter.Matches(Photo(rank: 4)));
        Assert.False(filter.Matches(Photo(rank: 3)));
    }

    [Fact]
    public void ToggleRank_CanAddTheUnratedToARun() {
        // "Everything I have not looked at, plus the good ones."
        var filter = RatingFilter.None.PickRank(4).ToggleRank(RatingFilter.Unrated);

        Assert.True(filter.Matches(Photo()));
        Assert.True(filter.Matches(Photo(rank: 5)));
        Assert.False(filter.Matches(Photo(rank: 2)));
    }

    [Fact]
    public void ToggleRank_EmptyingTheSet_TurnsTheFilterOff() {
        var filter = RatingFilter.None.ToggleRank(3).ToggleRank(3);

        Assert.False(filter.IsActive);
    }


    // --- Colours ---------------------------------------------------------

    [Fact]
    public void PickColor_TakesThatColourAlone() {
        var filter = RatingFilter.None.PickColor(2);

        Assert.True(filter.Matches(Photo(color: 2)));
        Assert.False(filter.Matches(Photo(color: 3)));
        Assert.False(filter.Matches(Photo()));
    }

    [Fact]
    public void PickColor_Twice_TurnsTheColourFilterOff() {
        Assert.False(RatingFilter.None.PickColor(2).PickColor(2).IsActive);
    }

    [Fact]
    public void ToggleColor_BuildsASet() {
        var filter = RatingFilter.None.PickColor(1).ToggleColor(4);

        Assert.True(filter.Matches(Photo(color: 1)));
        Assert.True(filter.Matches(Photo(color: 4)));
        Assert.False(filter.Matches(Photo(color: 2)));
    }

    [Fact]
    public void BothHalves_MustHold() {
        var filter = RatingFilter.None.PickRank(4).PickColor(5);

        Assert.True(filter.Matches(Photo(rank: 4, color: 5)));
        Assert.False(filter.Matches(Photo(rank: 4, color: 1)));
        Assert.False(filter.Matches(Photo(rank: 2, color: 5)));
    }

    [Fact]
    public void IsActive_TracksEitherHalf() {
        Assert.True(RatingFilter.None.PickRank(1).IsActive);
        Assert.True(RatingFilter.None.PickColor(1).IsActive);
        Assert.False(RatingFilter.None.IsActive);
    }


    // --- Out of range ----------------------------------------------------

    [Fact]
    public void RanksOutOfRange_AreIgnoredRatherThanThrowing() {
        // The parameter comes from a converter parameter in XAML; a typo
        // there should light nothing, not take the window down.
        Assert.False(RatingFilter.None.PickRank(9).IsActive);
        Assert.False(RatingFilter.None.ToggleRank(-1).IsActive);
    }
}
