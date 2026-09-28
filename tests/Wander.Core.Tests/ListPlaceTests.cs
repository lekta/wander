using Wander.Core.Persistence;

namespace Wander.Core.Tests;

/// <summary>
/// What the session remembers of the list in its last folder (2026-09-25):
/// the file it was on, the rows around it to find the one that takes its
/// place, and the row first on screen.
/// </summary>
public class ListPlaceTests {

    [Fact]
    public void KeepsTheFileAndItsNeighborsOnBothSides() {
        var rows = Rows(30);

        var place = ListPlace.Of(rows, rows[15], top: rows[10])!;

        Assert.Equal(rows[15], place.Row);
        Assert.Equal(rows[10], place.Top);
        Assert.Equal(rows.Skip(15 - ListPlace.Neighbors).Take((2 * ListPlace.Neighbors) + 1), place.StoodAmong);
    }

    [Fact]
    public void NearAnEnd_TheNeighborsStopAtIt() {
        var rows = Rows(5);

        var place = ListPlace.Of(rows, rows[1], top: null)!;

        Assert.Equal(rows, place.StoodAmong);
        Assert.Null(place.Top);
    }

    [Fact]
    public void NothingSelected_OnlyTheTopRowIsKept() {
        var rows = Rows(5);

        var place = ListPlace.Of(rows, row: null, top: rows[3])!;

        Assert.Null(place.Row);
        Assert.Empty(place.StoodAmong);
        Assert.Equal(rows[3], place.Top);
    }

    [Fact]
    public void RowsNotInTheList_AreNothingToRemember() {
        var rows = Rows(3);

        Assert.Null(ListPlace.Of(rows, row: null, top: null));
        Assert.Null(ListPlace.Of(rows, @"C:\elsewhere\a.txt", top: @"C:\elsewhere\b.txt"));
    }

    [Fact]
    public void TheSamePlace_IsTheSameFileAndTopRow() {
        var rows = Rows(20);
        var place = ListPlace.Of(rows, rows[5], rows[2])!;

        Assert.True(place.SameAs(ListPlace.Of(rows.Skip(1).ToArray(), rows[5].ToUpperInvariant(), rows[2])));
        Assert.False(place.SameAs(ListPlace.Of(rows, rows[6], rows[2])));
        Assert.False(place.SameAs(null));
    }


    private static string[] Rows(int count) {
        return Enumerable.Range(0, count).Select(i => $@"C:\folder\{i:D2}.jpg").ToArray();
    }
}
