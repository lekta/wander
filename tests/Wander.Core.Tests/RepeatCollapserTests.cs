using Wander.Core.Logging;

namespace Wander.Core.Tests;

public class RepeatCollapserTests {
    private const string Line = "ERROR|Unhandled dispatcher exception";

    private static readonly DateTime _start = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void AHundredInOneSecond_BecomeTwoLines() {
        var collapser = new RepeatCollapser();

        int written = 0;
        for (int i = 0; i < 100; i++) {
            if (collapser.Decide(Line, _start.AddMilliseconds(i * 10)).Write) {
                written++;
            }
        }

        Assert.Equal(1, written);
        var summary = collapser.Flush();
        Assert.NotNull(summary);
        Assert.Equal(99, summary!.Value.Count);
    }

    [Fact]
    public void DifferentLines_AreNotCollapsed() {
        var collapser = new RepeatCollapser();

        Assert.True(collapser.Decide("a", _start).Write);
        Assert.True(collapser.Decide("b", _start.AddMilliseconds(1)).Write);
        Assert.True(collapser.Decide("a", _start.AddMilliseconds(2)).Write);
        Assert.Null(collapser.Flush());
    }

    [Fact]
    public void AfterAQuietWindow_TheSameLineIsNewsAgain() {
        var collapser = new RepeatCollapser();

        collapser.Decide(Line, _start);
        var decision = collapser.Decide(Line, _start.AddSeconds(6));

        Assert.True(decision.Write);
        Assert.Null(decision.Repeats);
    }

    [Fact]
    public void ThreeMinutesOfFlooding_AreOneLineAndThreeSummaries() {
        var collapser = new RepeatCollapser();

        int written = 0;
        int summaries = 0;
        for (int tick = 0; tick <= 180 * 10; tick++) {
            var decision = collapser.Decide(Line, _start.AddMilliseconds(tick * 100));
            if (decision.Write) {
                written++;
            }
            if (decision.Repeats is not null) {
                summaries++;
            }
        }
        if (collapser.Flush() is not null) {
            summaries++;
        }

        Assert.Equal(1, written);
        Assert.Equal(3, summaries);
    }

    [Fact]
    public void ASummaryFlushedByAnotherLine_CountsTheRunItEnds() {
        var collapser = new RepeatCollapser();

        collapser.Decide(Line, _start);
        collapser.Decide(Line, _start.AddSeconds(1));
        collapser.Decide(Line, _start.AddSeconds(2));
        var decision = collapser.Decide("other", _start.AddSeconds(3));

        Assert.True(decision.Write);
        Assert.NotNull(decision.Repeats);
        Assert.Equal(2, decision.Repeats!.Value.Count);
        Assert.Equal(TimeSpan.FromSeconds(2), decision.Repeats.Value.Span);
    }

    [Fact]
    public void Signature_SeparatesTheSameMessageFromDifferentPlaces() {
        var first = Caught(() => throw new InvalidOperationException("boom"));
        var second = new InvalidOperationException("boom");

        Assert.NotEqual(
            RepeatCollapser.Signature("ERROR", "failed", first),
            RepeatCollapser.Signature("ERROR", "failed", second));
    }

    [Fact]
    public void Signature_IsStableForTheSameFault() {
        var ex = Caught(() => throw new InvalidOperationException("boom"));

        Assert.Equal(
            RepeatCollapser.Signature("ERROR", "failed", ex),
            RepeatCollapser.Signature("ERROR", "failed", ex));
    }

    [Fact]
    public void Summary_ReadsAsOneLine() {
        var summary = new RepeatCollapser.Summary(16277, TimeSpan.FromSeconds(271));

        Assert.Equal("previous line repeated 16277 times over 271 s", summary.Line);
    }


    private static Exception Caught(Action action) {
        try {
            action();
        } catch (Exception ex) {
            return ex;
        }

        throw new InvalidOperationException("the action did not throw");
    }
}
