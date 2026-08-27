using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class TypeAheadControllerTests {
    private static readonly string[] _names = {
        "apple.txt",
        "Banana.txt",
        "berry.txt",
        "Cherry.txt",
        "banjo.txt",
    };


    /// <summary>
    /// A controller on a clock the test moves by hand — the timeout is the
    /// interesting half of this class, and waiting a real second for it is
    /// how flaky tests are made.
    /// </summary>
    private sealed class Clock {
        public DateTime Now { get; private set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan by) {
            Now += by;
        }
    }

    private static (TypeAheadController Ctrl, Clock Clock) Make() {
        var clock = new Clock();

        return (new TypeAheadController(() => clock.Now), clock);
    }


    [Fact]
    public void OneLetter_JumpsToTheFirstMatch() {
        var (ctrl, _) = Make();

        Assert.Equal(1, ctrl.Type("b", _names, currentIndex: 0));
    }

    [Fact]
    public void Letters_Accumulate_IntoAPrefix() {
        var (ctrl, _) = Make();
        ctrl.Type("b", _names, 0);

        // "be" can only be berry, not Banana.
        Assert.Equal(2, ctrl.Type("e", _names, 1));
        Assert.Equal("be", ctrl.Prefix);
    }

    [Fact]
    public void Matching_IgnoresCase() {
        var (ctrl, _) = Make();

        Assert.Equal(1, ctrl.Type("B", _names, 0));
    }

    [Fact]
    public void SameLetterAgain_CyclesThroughTheMatches() {
        var (ctrl, _) = Make();

        int first = ctrl.Type("b", _names, 0);
        int second = ctrl.Type("b", _names, first);
        int third = ctrl.Type("b", _names, second);

        Assert.Equal(1, first);   // Banana
        Assert.Equal(2, second);  // berry
        Assert.Equal(4, third);   // banjo
    }

    [Fact]
    public void Cycling_WrapsAroundTheEnd() {
        var (ctrl, _) = Make();

        // Starting past the last "b", the next one is found from the top.
        Assert.Equal(1, ctrl.Type("b", _names, 4));
    }

    [Fact]
    public void APause_StartsANewSearch() {
        var (ctrl, clock) = Make();
        ctrl.Type("b", _names, 0);

        clock.Advance(TypeAheadController.DefaultTimeout + TimeSpan.FromMilliseconds(1));

        // Not "be" any more — a fresh "e", which matches nothing here.
        Assert.Equal(-1, ctrl.Type("e", _names, 1));
        Assert.Equal("e", ctrl.Prefix);
    }

    [Fact]
    public void WithinTheTimeout_ThePrefixSurvives() {
        var (ctrl, clock) = Make();
        ctrl.Type("b", _names, 0);

        clock.Advance(TypeAheadController.DefaultTimeout - TimeSpan.FromMilliseconds(1));

        Assert.Equal(2, ctrl.Type("e", _names, 1));
    }

    [Fact]
    public void Search_StartsAtTheSelection_AndWraps() {
        var (ctrl, _) = Make();

        // From "Cherry" the next "b" is found by wrapping to the top.
        Assert.Equal(4, ctrl.Type("b", _names, 3));
    }

    [Fact]
    public void NothingMatches_LeavesTheSelectionAlone() {
        var (ctrl, _) = Make();

        Assert.Equal(-1, ctrl.Type("z", _names, 0));
    }

    [Fact]
    public void EmptyList_IsNotASearch() {
        var (ctrl, _) = Make();

        Assert.Equal(-1, ctrl.Type("a", Array.Empty<string>(), -1));
    }

    [Fact]
    public void ControlCharacters_AreIgnored() {
        var (ctrl, _) = Make();

        Assert.Equal(-1, ctrl.Type("\b", _names, 0));
        Assert.Equal("", ctrl.Prefix);
    }

    [Fact]
    public void NothingSelected_SearchesFromTheTop() {
        var (ctrl, _) = Make();

        Assert.Equal(0, ctrl.Type("a", _names, currentIndex: -1));
    }

    [Fact]
    public void Reset_ForgetsThePrefix() {
        var (ctrl, _) = Make();
        ctrl.Type("b", _names, 0);

        ctrl.Reset();

        // "e" on its own, not "be".
        Assert.Equal(-1, ctrl.Type("e", _names, 1));
    }
}
