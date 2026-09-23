using Wander.Core.Layout;

namespace Wander.Core.Tests;

/// <summary>Drag deeper (U1, REDESIGN 4.13): a place held long enough opens, once; the clock is the test's.</summary>
public class DragHoverTests {
    private const int Delay = 800;
    private static readonly HoverTarget _folder = new(@"C:\A\Photos", HoverSurface.List, CanOpen: true);
    private static readonly HoverTarget _line = new(@"C:\A", HoverSurface.Panel, CanOpen: true);


    [Fact]
    public void BeforeTheDelay_ItWaits_UntilItIsUp() {
        var state = DragHover.Track(DragHoverState.None, _folder, 1000);

        var decision = DragHover.Decide(state, 1500, Delay, Array.Empty<string>());

        Assert.Equal(DragHoverOutcome.Wait, decision.Outcome);
        Assert.Equal(1800, decision.AtMs);
    }

    [Fact]
    public void HeldOverAFolderInTheList_GoesIntoIt() {
        var state = DragHover.Track(DragHoverState.None, _folder, 1000);

        var decision = DragHover.Decide(state, 1800, Delay, Array.Empty<string>());

        Assert.Equal(new DragHoverDecision(DragHoverOutcome.Enter, Path: _folder.Path), decision);
    }

    [Fact]
    public void HeldOverAClosedPanelLine_OpensIt() {
        var state = DragHover.Track(DragHoverState.None, _line, 0);

        Assert.Equal(DragHoverOutcome.Expand, DragHover.Decide(state, Delay, Delay, Array.Empty<string>()).Outcome);
    }

    /// <summary>The hand shakes inside one row: the same place, the clock runs on.</summary>
    [Fact]
    public void TheSamePlaceAgain_KeepsItsClock() {
        var state = DragHover.Track(DragHoverState.None, _folder, 1000);

        state = DragHover.Track(state, _folder with { Path = @"c:\a\photos\" }, 1700);

        Assert.Equal(1000, state.SinceMs);
        Assert.Equal(DragHoverOutcome.Enter, DragHover.Decide(state, 1800, Delay, Array.Empty<string>()).Outcome);
    }

    [Fact]
    public void AnotherPlace_StartsTheClockOver() {
        var state = DragHover.Track(DragHoverState.None, _folder, 1000);

        state = DragHover.Track(state, _folder with { Path = @"C:\A\Music" }, 1700);

        Assert.Equal(DragHoverOutcome.Wait, DragHover.Decide(state, 1800, Delay, Array.Empty<string>()).Outcome);
    }

    /// <summary>Off anything, out of the window, dropped: the hover is over.</summary>
    [Fact]
    public void NothingUnderTheCursor_ResetsTheHover() {
        var state = DragHover.Track(DragHoverState.None, _folder, 1000);

        state = DragHover.Track(state, null, 1500);

        Assert.Equal(DragHoverState.None, state);
        Assert.Equal(DragHoverOutcome.Nothing, DragHover.Decide(state, 5000, Delay, Array.Empty<string>()).Outcome);
    }

    [Fact]
    public void APlace_IsActedOnOnce() {
        var state = DragHover.Track(DragHoverState.None, _line, 0) with { Done = true };

        Assert.Equal(DragHoverOutcome.Nothing, DragHover.Decide(state, 5000, Delay, Array.Empty<string>()).Outcome);
        Assert.True(DragHover.Track(state, _line, 6000).Done);
    }

    /// <summary>An archive, the bin, an open line with nothing more to show: nothing to go into.</summary>
    [Fact]
    public void WhatCannotBeOpened_IsNotWaitedFor() {
        var state = DragHover.Track(DragHoverState.None, _folder with { CanOpen = false }, 0);

        Assert.Equal(DragHoverOutcome.Nothing, DragHover.Decide(state, 5000, Delay, Array.Empty<string>()).Outcome);
    }

    /// <summary>Never into the folder being dragged, or anything under it.</summary>
    [Theory]
    [InlineData(@"C:\A\Photos")]
    [InlineData(@"C:\A")]
    public void TheDraggedFolder_AndAnythingUnderIt_AreNotGoneInto(string dragged) {
        var state = DragHover.Track(DragHoverState.None, _folder, 0);

        Assert.Equal(DragHoverOutcome.Nothing, DragHover.Decide(state, 5000, Delay, new[] { dragged }).Outcome);
    }

    /// <summary>A sibling whose name merely starts the same is not under the dragged folder.</summary>
    [Fact]
    public void ASiblingWithTheSamePrefix_IsGoneInto() {
        var state = DragHover.Track(DragHoverState.None, _folder, 0);

        Assert.Equal(DragHoverOutcome.Enter, DragHover.Decide(state, 5000, Delay, new[] { @"C:\A\Photo" }).Outcome);
    }
}
