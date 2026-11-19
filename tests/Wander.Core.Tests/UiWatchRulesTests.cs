using Wander.Core.Diagnostics;

namespace Wander.Core.Tests;

/// <summary>Audit 2026-11-18, L5, L7: what the watch over the window's thread writes, and when.</summary>
public class UiWatchRulesTests {

    [Fact]
    public void TheWindowSilent_IsSaidAtTwoFiveAndTenSeconds_ThenEveryTen() {
        var said = new List<int>();
        int last = 0;
        for (int ms = 0; ms <= 41_000; ms += 250) {
            if (UiWatchRules.SilenceToSay(TimeSpan.FromMilliseconds(ms), last) is { } mark) {
                said.Add(mark);
                last = mark;
            }
        }

        Assert.Equal(new[] { 2, 5, 10, 20, 30, 40 }, said);
    }

    [Fact]
    public void AShortStall_SaysNothing() {
        Assert.Null(UiWatchRules.SilenceToSay(TimeSpan.FromMilliseconds(1900), 0));
    }

    [Fact]
    public void AWatchThatWokeLate_SaysTheLatestMarkOnly() {
        Assert.Equal(10, UiWatchRules.SilenceToSay(TimeSpan.FromSeconds(12), 2));
        Assert.Null(UiWatchRules.SilenceToSay(TimeSpan.FromSeconds(12), 10));
    }

    [Fact]
    public void ThePulse_IsFiveSecondsActive_AMinuteInTheBackground() {
        Assert.Equal(TimeSpan.FromSeconds(5), UiWatchRules.PulseInterval(active: true));
        Assert.Equal(TimeSpan.FromSeconds(60), UiWatchRules.PulseInterval(active: false));
    }
}
