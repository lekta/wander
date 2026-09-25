using System.Collections.Immutable;
using Wander.Core.Panels;

namespace Wander.Core.Tests;

/// <summary>The keys of a panel drawn as a list (decision B26).</summary>
public class PanelKeyNavigationTests {
    //   C:\            0
    //     A            1  open
    //       B          2  closed, has a chevron
    //       D          3  no chevron
    //     E            4  open, no subfolders left
    //   D:\            5
    private static readonly IReadOnlyList<VisibleRow> _lines = Lines();


    [Fact]
    public void UpAndDown_MoveToTheNeighbourLine() {
        Assert.Equal(Move(@"C:\A\D"), Press(@"C:\A\B", PanelKey.Down));
        Assert.Equal(Move(@"C:\A"), Press(@"C:\A\B", PanelKey.Up));
    }

    [Fact]
    public void AtTheEdges_NothingHappens() {
        Assert.Equal(PanelKeyResult.None, Press(@"C:\", PanelKey.Up));
        Assert.Equal(PanelKeyResult.None, Press(@"D:\", PanelKey.Down));
        Assert.Equal(PanelKeyResult.None, Press(@"C:\", PanelKey.Home));
        Assert.Equal(PanelKeyResult.None, Press(@"D:\", PanelKey.End));
    }

    [Fact]
    public void Left_ClosesAnOpenRow_ElseGoesToTheRowAbove() {
        Assert.Equal(new PanelKeyResult(PanelKeyOutcome.Collapse, @"C:\A"), Press(@"C:\A", PanelKey.Left));
        Assert.Equal(Move(@"C:\A"), Press(@"C:\A\D", PanelKey.Left));
        Assert.Equal(PanelKeyResult.None, Press(@"D:\", PanelKey.Left));
    }

    [Fact]
    public void Right_OpensAClosedRow_ElseGoesToItsFirstChild() {
        Assert.Equal(new PanelKeyResult(PanelKeyOutcome.Expand, @"C:\A\B"), Press(@"C:\A\B", PanelKey.Right));
        Assert.Equal(Move(@"C:\A\B"), Press(@"C:\A", PanelKey.Right));
        Assert.Equal(PanelKeyResult.None, Press(@"C:\A\D", PanelKey.Right));
    }

    /// <summary>An open row with no subfolders has nothing to close: Left goes up at once, Right has nowhere to go.</summary>
    [Fact]
    public void AnOpenRowWithNoSubfolders_LeftGoesUp_RightDoesNothing() {
        Assert.Equal(Move(@"C:\"), Press(@"C:\E", PanelKey.Left));
        Assert.Equal(PanelKeyResult.None, Press(@"C:\E", PanelKey.Right));
    }

    [Fact]
    public void HomeAndEnd_GoToTheEnds() {
        Assert.Equal(Move(@"C:\"), Press(@"C:\A\D", PanelKey.Home));
        Assert.Equal(Move(@"D:\"), Press(@"C:\A\D", PanelKey.End));
    }

    /// <summary>A page is the lines the panel shows, less one - the line the page turns on stays in view.</summary>
    [Fact]
    public void Pages_AreThePanelsHeightLessOne() {
        Assert.Equal(Move(@"C:\A\D"), Press(@"C:\A", PanelKey.PageDown, pageSize: 3));
        Assert.Equal(Move(@"D:\"), Press(@"C:\A", PanelKey.PageDown, pageSize: 50));
        Assert.Equal(Move(@"C:\A"), Press(@"C:\A\D", PanelKey.PageUp, pageSize: 3));
        Assert.Equal(Move(@"C:\"), Press(@"C:\A\D", PanelKey.PageUp, pageSize: 50));
    }

    /// <summary>A cursor hidden in a closed branch: Left and Right land on the row it is hidden under, the rest go on from there.</summary>
    [Fact]
    public void AHiddenCursor_GoesOnFromTheRowItIsHiddenUnder() {
        Assert.Equal(Move(@"C:\A\B"), Press(@"C:\A\B\C", PanelKey.Left));
        Assert.Equal(Move(@"C:\A\B"), Press(@"C:\A\B\C", PanelKey.Right));
        Assert.Equal(Move(@"C:\A\D"), Press(@"C:\A\B\C", PanelKey.Down));
    }

    /// <summary>No cursor in the panel: the first key enters it from the top going down, from the bottom going up.</summary>
    [Fact]
    public void NoCursor_TheFirstKeyEnters() {
        Assert.Equal(Move(@"C:\"), Press(null, PanelKey.Down));
        Assert.Equal(Move(@"D:\"), Press(null, PanelKey.Up));
    }

    [Fact]
    public void AnEmptyPanel_AnswersNothing() {
        Assert.Equal(PanelKeyResult.None, PanelKeyNavigation.Press(Array.Empty<VisibleRow>(), null, PanelKey.Down, 10));
    }


    private static PanelKeyResult Press(string? caret, PanelKey key, int pageSize = 10) {
        return PanelKeyNavigation.Press(_lines, caret, key, pageSize);
    }

    private static PanelKeyResult Move(string path) {
        return new PanelKeyResult(PanelKeyOutcome.MoveCaret, path);
    }

    private static IReadOnlyList<VisibleRow> Lines() {
        var drive = new PanelRow(@"C:\", @"C:\", PanelRowKind.Drive) { Children = ChildrenKnown.Yes };
        var a = new PanelRow(@"C:\A", "A", PanelRowKind.Folder) { Children = ChildrenKnown.Yes };
        var b = new PanelRow(@"C:\A\B", "B", PanelRowKind.Folder) { Children = ChildrenKnown.Yes };
        var d = new PanelRow(@"C:\A\D", "D", PanelRowKind.Folder) { Children = ChildrenKnown.No };
        var e = new PanelRow(@"C:\E", "E", PanelRowKind.Folder) { Children = ChildrenKnown.No };
        var other = new PanelRow(@"D:\", @"D:\", PanelRowKind.Drive) { Children = ChildrenKnown.Yes };
        var panel = PanelState.Empty
            .WithLevel(PanelState.TopKey, new PanelLevel(LevelState.Loaded, ImmutableArray.Create(drive, other), 1))
            .WithLevel(@"C:\", new PanelLevel(LevelState.Loaded, ImmutableArray.Create(a, e), 2))
            .WithLevel(@"C:\A", new PanelLevel(LevelState.Loaded, ImmutableArray.Create(b, d), 3))
            .WithExpanded(@"C:\", true)
            .WithExpanded(@"C:\A", true)
            .WithExpanded(@"C:\E", true);

        return PanelView.Rows(panel);
    }
}
