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


    [Fact]
    public void None_IsNotActiveAndPassesEverything() {
        Assert.False(RatingFilter.None.IsActive);
        Assert.True(RatingFilter.None.Matches(Photo()));
        Assert.True(RatingFilter.None.Matches(Photo(rank: 5, color: 2)));
    }

    [Fact]
    public void MinRank_KeepsEqualAndAbove() {
        var filter = new RatingFilter(3, null);

        Assert.False(filter.Matches(Photo(rank: 2)));
        Assert.True(filter.Matches(Photo(rank: 3)));
        Assert.True(filter.Matches(Photo(rank: 5)));
    }

    [Fact]
    public void MinRank_DropsTheUnrated() {
        Assert.False(new RatingFilter(1, null).Matches(Photo()));
    }

    [Fact]
    public void ColorLabel_MatchesExactly() {
        var filter = new RatingFilter(0, 2);

        Assert.True(filter.Matches(Photo(color: 2)));
        Assert.False(filter.Matches(Photo(color: 3)));
        Assert.False(filter.Matches(Photo()));
    }

    [Fact]
    public void BothHalves_MustHold() {
        var filter = new RatingFilter(4, 5);

        Assert.True(filter.Matches(Photo(rank: 4, color: 5)));
        Assert.False(filter.Matches(Photo(rank: 4, color: 1)));
        Assert.False(filter.Matches(Photo(rank: 2, color: 5)));
    }

    [Fact]
    public void IsActive_TracksEitherHalf() {
        Assert.True(new RatingFilter(1, null).IsActive);
        Assert.True(new RatingFilter(0, 1).IsActive);
        Assert.False(new RatingFilter(0, null).IsActive);
    }
}
