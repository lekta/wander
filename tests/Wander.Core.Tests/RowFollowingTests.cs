using Wander.Core.Listing;

namespace Wander.Core.Tests;

/// <summary>
/// The selected file does not leave the screen on its own (2026-09-28):
/// what the view does about the main row after a landing, from where it
/// stood before it.
/// </summary>
public class RowFollowingTests {
    private const string Row = @"C:\A\b.jpg";
    private const string Other = @"C:\A\c.jpg";


    /// <summary>The watcher, F5, an operation: a row that stood on screen is put back where it stood.</summary>
    [Fact]
    public void ARowThatStoodOnScreen_IsHeldWhereItStood() {
        Assert.Equal(RowFollow.Hold, RowFollowing.Decide(Stood(Row), held: Row, reveal: false));
    }

    /// <summary>The path compares as a path.</summary>
    [Fact]
    public void TheRowIsKnownByItsPath_WhateverTheCase() {
        Assert.Equal(RowFollow.Hold, RowFollowing.Decide(Stood(Row), held: Row.ToUpperInvariant(), reveal: false));
    }

    /// <summary>The user had scrolled away from the selection: nothing pulls the list back to it.</summary>
    [Fact]
    public void ARowOutOfView_IsLeftThere() {
        Assert.Equal(RowFollow.None, RowFollowing.Decide(stood: null, held: Row, reveal: false));
    }

    /// <summary>A filter, another order, rows asked for: a row out of view is brought into it.</summary>
    [Fact]
    public void ARowOutOfView_IsShown_WhenTheLandingShowsItsRow() {
        Assert.Equal(RowFollow.Reveal, RowFollowing.Decide(stood: null, held: Row, reveal: true));
    }

    /// <summary>The row that stood on screen is not the one held - another row was asked for, the main row handed over: no place to put it back to.</summary>
    [Theory]
    [InlineData(false, RowFollow.None)]
    [InlineData(true, RowFollow.Reveal)]
    public void AnotherRowThanTheOneThatStood_HasNoPlaceToKeep(bool reveal, RowFollow expected) {
        Assert.Equal(expected, RowFollowing.Decide(Stood(Other), held: Row, reveal));
        Assert.Equal(expected, RowFollowing.Decide(Stood(Row), held: null, reveal));
    }

    /// <summary>
    /// A list at its very start stays there while the row fits on screen:
    /// files arriving above it are what the user is looking at. Pushed out
    /// of view, the row is brought back - moving as little as possible.
    /// </summary>
    [Fact]
    public void AtTheStartOfTheList_TheRowIsKeptInView_NotInPlace() {
        Assert.Equal(RowFollow.Reveal, RowFollowing.Decide(Stood(Row, atStart: true), held: Row, reveal: false));
    }

    /// <summary>The user's own filter or order holds the row in place wherever the list stood.</summary>
    [Fact]
    public void AtTheStartOfTheList_TheUsersOwnChange_HoldsTheRow() {
        Assert.Equal(RowFollow.Hold, RowFollowing.Decide(Stood(Row, atStart: true), held: Row, reveal: true));
    }


    private static RowStand Stood(string path, bool atStart = false) {
        return new RowStand(path, Top: 120, atStart);
    }
}
