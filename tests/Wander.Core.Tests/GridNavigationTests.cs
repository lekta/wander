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
}
