using Wander.Core.Layout;

namespace Wander.Core.Tests;

public class GridNavigationTests {
    /// <summary>
    /// Four columns, eleven items — three full rows and a last row of three,
    /// which is the shape every "short last row" case below needs.
    ///
    /// <code>
    ///   0  1  2  3
    ///   4  5  6  7
    ///   8  9 10
    /// </code>
    /// </summary>
    private static int Move(int index, GridStep step, int columns = 4, int count = 11) {
        return GridNavigation.Move(index, step, columns, count);
    }


    // --- Left / Right: the grid read as one folded list ------------------

    [Fact]
    public void Right_AtRowEnd_GoesToStartOfNextRow() {
        Assert.Equal(4, Move(3, GridStep.Right));
    }

    [Fact]
    public void Left_AtRowStart_GoesToEndOfPreviousRow() {
        Assert.Equal(3, Move(4, GridStep.Left));
    }

    [Fact]
    public void Right_MidRow_IsJustTheNextItem() {
        Assert.Equal(2, Move(1, GridStep.Right));
    }

    /// <summary>
    /// The two outer ends stay put. Wrapping the last item round to the
    /// first is a jump across the whole list, not a step.
    /// </summary>
    [Fact]
    public void Right_OnLastItem_StaysPut() {
        Assert.Equal(-1, Move(10, GridStep.Right));
    }

    [Fact]
    public void Left_OnFirstItem_StaysPut() {
        Assert.Equal(-1, Move(0, GridStep.Left));
    }


    // --- Up / Down: whole rows, with Home/End at the edges ---------------

    [Fact]
    public void Up_MidGrid_MovesOneRow() {
        Assert.Equal(1, Move(5, GridStep.Up));
    }

    [Fact]
    public void Up_OnTopRow_GoesToTheFirstItem() {
        Assert.Equal(0, Move(2, GridStep.Up));
    }

    [Fact]
    public void Up_OnTheFirstItem_StaysPut() {
        Assert.Equal(-1, Move(0, GridStep.Up));
    }

    [Fact]
    public void Down_MidGrid_MovesOneRow() {
        Assert.Equal(5, Move(1, GridStep.Down));
    }

    [Fact]
    public void Down_OnBottomRow_GoesToTheLastItem() {
        Assert.Equal(10, Move(8, GridStep.Down));
    }

    [Fact]
    public void Down_OnTheLastItem_StaysPut() {
        Assert.Equal(-1, Move(10, GridStep.Down));
    }

    /// <summary>
    /// Item 7 sits in the last full row, under a column the short last row
    /// never reaches. Explorer lands on the last item there rather than
    /// refusing to move.
    /// </summary>
    [Fact]
    public void Down_IntoAShortLastRow_LandsOnItsLastItem() {
        Assert.Equal(10, Move(7, GridStep.Down));
    }

    [Fact]
    public void Down_IntoAShortLastRow_TakesTheColumnWhenItExists() {
        Assert.Equal(10, Move(6, GridStep.Down));
    }


    // --- Degenerate input ------------------------------------------------

    [Theory]
    [InlineData(GridStep.Left)]
    [InlineData(GridStep.Right)]
    [InlineData(GridStep.Up)]
    [InlineData(GridStep.Down)]
    public void EmptyList_GoesNowhere(GridStep step) {
        Assert.Equal(-1, GridNavigation.Move(0, step, columns: 4, count: 0));
    }

    [Fact]
    public void NoSelection_GoesNowhere() {
        Assert.Equal(-1, Move(-1, GridStep.Down));
    }

    [Fact]
    public void IndexPastTheEnd_GoesNowhere() {
        Assert.Equal(-1, Move(11, GridStep.Up));
    }

    /// <summary>
    /// A pane narrower than one cell reports zero columns while the layout
    /// catches up; nothing is on screen to move between yet.
    /// </summary>
    [Fact]
    public void NoColumns_GoesNowhere() {
        Assert.Equal(-1, GridNavigation.Move(3, GridStep.Down, columns: 0, count: 11));
    }

    /// <summary>
    /// One column is a plain vertical list: Right and Left have nowhere
    /// sideways to go, so they behave as Down and Up.
    /// </summary>
    [Fact]
    public void SingleColumn_BehavesAsAList() {
        Assert.Equal(4, GridNavigation.Move(3, GridStep.Right, columns: 1, count: 11));
        Assert.Equal(2, GridNavigation.Move(3, GridStep.Left, columns: 1, count: 11));
        Assert.Equal(4, GridNavigation.Move(3, GridStep.Down, columns: 1, count: 11));
    }


    // --- IsAtEdge: which presses are ours at all -------------------------

    private static bool AtEdge(int index, GridStep step, int columns = 4, int count = 11) {
        return GridNavigation.IsAtEdge(index, step, columns, count);
    }

    [Fact]
    public void MidGrid_IsNotAnEdge_InAnyDirection() {
        Assert.False(AtEdge(5, GridStep.Left));
        Assert.False(AtEdge(5, GridStep.Right));
        Assert.False(AtEdge(5, GridStep.Up));
        Assert.False(AtEdge(5, GridStep.Down));
    }

    [Fact]
    public void RowStartAndRowEnd_AreEdgesSideways() {
        Assert.True(AtEdge(4, GridStep.Left));
        Assert.True(AtEdge(7, GridStep.Right));
        Assert.False(AtEdge(4, GridStep.Right));
        Assert.False(AtEdge(7, GridStep.Left));
    }

    /// <summary>
    /// The last item ends its row even when the row is short, so Right
    /// there is ours - and it is the press that must not wrap.
    /// </summary>
    [Fact]
    public void LastItem_EndsItsRow_EvenInAShortRow() {
        Assert.True(AtEdge(10, GridStep.Right));
    }

    [Fact]
    public void TopRowAndBottomRow_AreEdgesVertically() {
        Assert.True(AtEdge(2, GridStep.Up));
        Assert.True(AtEdge(9, GridStep.Down));
        Assert.False(AtEdge(2, GridStep.Down));
        Assert.False(AtEdge(9, GridStep.Up));
    }

    /// <summary>
    /// Item 7 has nothing below it - the last row stops before its column -
    /// so the press is ours even though 7 is not on the bottom row.
    /// </summary>
    [Fact]
    public void AFullRowAboveAShortOne_IsAnEdgeGoingDown() {
        Assert.True(AtEdge(7, GridStep.Down));
    }

    [Fact]
    public void NoCaret_IsNeverAnEdge() {
        Assert.False(AtEdge(-1, GridStep.Down));
    }

    /// <summary>
    /// Zero columns is a pane too narrow for one cell, mid-layout. The
    /// modulus below would throw on it, so it never gets there.
    /// </summary>
    [Fact]
    public void NoColumns_IsNeverAnEdge() {
        Assert.False(GridNavigation.IsAtEdge(3, GridStep.Left, columns: 0, count: 11));
    }


    // --- Anchor: the end of the run the caret is not on -------------------

    private static int Anchor(int caret, params int[] selected) {
        return GridNavigation.Anchor(caret, count: 11, i => Array.IndexOf(selected, i) >= 0);
    }

    [Fact]
    public void CaretOnTheEndOfTheRun_AnchorsAtItsStart() {
        Assert.Equal(3, Anchor(6, 3, 4, 5, 6));
    }

    [Fact]
    public void CaretOnTheStartOfTheRun_AnchorsAtItsEnd() {
        Assert.Equal(6, Anchor(3, 3, 4, 5, 6));
    }

    [Fact]
    public void OneSelectedItem_AnchorsOnItself() {
        Assert.Equal(5, Anchor(5, 5));
    }

    /// <summary>
    /// Nothing selected - a fresh Shift+Arrow from a caret the mouse left
    /// behind - grows from where the caret is.
    /// </summary>
    [Fact]
    public void NothingSelected_AnchorsOnTheCaret() {
        Assert.Equal(4, Anchor(4));
    }

    /// <summary>
    /// A selection the user built with Ctrl is not a run, and there is no
    /// anchor to recover from it. The far end is the answer that keeps
    /// Shift growing away from the caret rather than collapsing onto it.
    /// </summary>
    [Fact]
    public void ScatteredSelection_AnchorsOnTheFarEnd() {
        Assert.Equal(9, Anchor(1, 1, 5, 9));
    }
}
