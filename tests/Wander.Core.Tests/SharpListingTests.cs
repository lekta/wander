using Wander.Core.FileSystem;
using Wander.Core.Listing;

namespace Wander.Core.Tests;

public class SharpListingTests {
    private static FileSystemEntry Photo(string name, double? sharpness = null) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\shoot\" + name,
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false,
            Sharpness: sharpness);
    }


    [Fact]
    public void NothingToSay_ReturnsTheSameList() {
        var rows = new[] { Photo("a.cr3"), Photo("b.cr3") };

        Assert.Same(rows, SharpListing.WithScores(rows, _ => null));
    }


    [Fact]
    public void Scores_ReplaceOnlyTheRowsTheyChange() {
        var rows = new[] { Photo("a.cr3"), Photo("b.cr3", 0.5), Photo("c.cr3") };
        var scores = new Dictionary<string, double> { ["a.cr3"] = 1.0, ["b.cr3"] = 0.5 };

        var scored = SharpListing.WithScores(rows, e => scores.TryGetValue(e.Name, out double v) ? v : null);

        Assert.NotSame(rows, scored);
        Assert.Equal(1.0, scored[0].Sharpness);
        Assert.Same(rows[1], scored[1]);
        Assert.Same(rows[2], scored[2]);
    }


    [Fact]
    public void NullClearsAScore() {
        var rows = new[] { Photo("a.cr3", 0.3) };

        var cleared = SharpListing.WithScores(rows, _ => null);

        Assert.Null(cleared[0].Sharpness);
    }


    /// <summary>A changed score is a changed row: the list shows it, and a re-read without it is not "the same".</summary>
    [Fact]
    public void Sharpness_CountsInSaysTheSameAs() {
        Assert.False(Photo("a.cr3", 0.3).SaysTheSameAs(Photo("a.cr3")));
        Assert.True(Photo("a.cr3", 0.3).SaysTheSameAs(Photo("a.cr3", 0.3)));
    }
}
