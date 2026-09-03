using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class TimecodeTests {
    [Fact]
    public void APositionRoundsDown() {
        Assert.Equal("0:07", Timecode.Format(TimeSpan.FromSeconds(7.9)));
    }

    /// <summary>
    /// The bug this exists for: a clip of 0.6 s showed "0:00", which reads
    /// as a file with nothing in it.
    /// </summary>
    [Fact]
    public void ALengthRoundsUp() {
        Assert.Equal("0:01", Timecode.Format(TimeSpan.FromSeconds(0.6), roundUp: true));
        Assert.Equal("0:01", Timecode.Format(TimeSpan.FromSeconds(0.04), roundUp: true));
    }

    [Fact]
    public void ZeroIsZero_EvenRoundedUp() {
        Assert.Equal("0:00", Timecode.Format(TimeSpan.Zero, roundUp: true));
    }

    [Fact]
    public void MinutesAndHours() {
        Assert.Equal("1:07", Timecode.Format(TimeSpan.FromSeconds(67)));
        Assert.Equal("59:59", Timecode.Format(TimeSpan.FromSeconds(3599)));
        Assert.Equal("1:00:00", Timecode.Format(TimeSpan.FromSeconds(3600)));
        Assert.Equal("2:03:04", Timecode.Format(new TimeSpan(2, 3, 4)));
    }

    [Fact]
    public void ANegativePositionIsNotDrawnAsNegative() {
        Assert.Equal("0:00", Timecode.Format(TimeSpan.FromSeconds(-3)));
    }
}


public class PlaybackClockTests {
    private static readonly TimeSpan _tenSeconds = TimeSpan.FromSeconds(10);


    // --- "Play" pressed: does it have to rewind first? --------------------

    [Fact]
    public void AtEnd_FalseAtTheStart() {
        Assert.False(PlaybackClock.AtEnd(TimeSpan.Zero, _tenSeconds));
        Assert.False(PlaybackClock.AtEnd(TimeSpan.Zero, null));
    }

    [Fact]
    public void AtEnd_FalseInTheMiddle() {
        Assert.False(PlaybackClock.AtEnd(TimeSpan.FromSeconds(4), _tenSeconds));
    }

    /// <summary>A player stops a frame or two short of the length it declared.</summary>
    [Fact]
    public void AtEnd_TrueJustShortOfTheLength() {
        Assert.True(PlaybackClock.AtEnd(TimeSpan.FromSeconds(9.8), _tenSeconds));
        Assert.True(PlaybackClock.AtEnd(_tenSeconds, _tenSeconds));
    }

    /// <summary>
    /// No length declared (or a length of zero — what the file behind this
    /// whole fix reports): anything past the start counts as the end,
    /// because a press of "play" there does nothing without a rewind.
    /// </summary>
    [Fact]
    public void AtEnd_WithNoLength_TrueAnywherePastTheStart() {
        Assert.True(PlaybackClock.AtEnd(TimeSpan.FromSeconds(0.5), null));
        Assert.True(PlaybackClock.AtEnd(TimeSpan.FromSeconds(0.5), TimeSpan.Zero));
    }


    // --- Repeat on by default ---------------------------------------------

    [Fact]
    public void LoopsByDefault_ShortClipsOnly() {
        Assert.True(PlaybackClock.LoopsByDefault(TimeSpan.FromSeconds(2)));
        Assert.False(PlaybackClock.LoopsByDefault(TimeSpan.FromSeconds(3)));
        Assert.False(PlaybackClock.LoopsByDefault(TimeSpan.FromMinutes(4)));
    }

    [Fact]
    public void LoopsByDefault_ALengthNobodyDeclaredCountsAsShort() {
        Assert.True(PlaybackClock.LoopsByDefault(null));
        Assert.True(PlaybackClock.LoopsByDefault(TimeSpan.Zero));
    }


    // --- The end nobody announced -----------------------------------------

    [Fact]
    public void AMovingPosition_IsNotTheEnd() {
        var clock = new PlaybackClock();

        for (int i = 1; i <= 20; i++) {
            Assert.False(clock.NoteTick(TimeSpan.FromSeconds(i * 0.2), _tenSeconds, playing: true));
        }
    }

    [Fact]
    public void APositionStuckAtTheEnd_IsTheEnd_AfterASecond() {
        var clock = new PlaybackClock();
        var end = TimeSpan.FromSeconds(9.9);
        clock.NoteTick(TimeSpan.FromSeconds(9.7), _tenSeconds, playing: true);

        // The tick that first reports 9.9 only records it; the five after
        // it are the second of standing still that calls the end. Five of
        // the pane's 200 ms ticks.
        for (int i = 0; i < 5; i++) {
            Assert.False(clock.NoteTick(end, _tenSeconds, playing: true));
        }

        Assert.True(clock.NoteTick(end, _tenSeconds, playing: true));
    }

    /// <summary>
    /// The file this was written for: it reports no usable length, so
    /// "stuck past the start" is all there is to go on.
    /// </summary>
    [Fact]
    public void WithNoLength_AStuckPositionIsStillTheEnd() {
        var clock = new PlaybackClock();
        var stuck = TimeSpan.FromSeconds(0.8);

        bool ended = false;
        for (int i = 0; i < 6 && !ended; i++) {
            ended = clock.NoteTick(stuck, null, playing: true);
        }

        Assert.True(ended);
    }

    [Fact]
    public void APlayerStillOpeningItsFile_IsNotTheEnd() {
        // Position zero for a second: the file has not started, and calling
        // that the end would rewind a clip that never played.
        var clock = new PlaybackClock();

        for (int i = 0; i < 20; i++) {
            Assert.False(clock.NoteTick(TimeSpan.Zero, _tenSeconds, playing: true));
        }
    }

    [Fact]
    public void APausedPlayer_IsNeverTheEnd() {
        var clock = new PlaybackClock();
        var end = TimeSpan.FromSeconds(9.9);

        for (int i = 0; i < 20; i++) {
            Assert.False(clock.NoteTick(end, _tenSeconds, playing: false));
        }
    }

    [Fact]
    public void APositionStuckInTheMiddle_IsNotTheEnd() {
        // A stutter — a cold disk, a share — is not a finished clip.
        var clock = new PlaybackClock();

        for (int i = 0; i < 20; i++) {
            Assert.False(clock.NoteTick(TimeSpan.FromSeconds(4), _tenSeconds, playing: true));
        }
    }

    [Fact]
    public void TheEndIsReportedOnce_NotOnEveryTickAfterIt() {
        var clock = new PlaybackClock();
        var end = TimeSpan.FromSeconds(9.9);
        bool first = false;
        for (int i = 0; i < 6; i++) {
            first |= clock.NoteTick(end, _tenSeconds, playing: true);
        }

        Assert.True(first);
        // The caller has been told; it rewinds and either stops or replays.
        // Until its position moves again, nothing more should be claimed.
        Assert.False(clock.NoteTick(end, _tenSeconds, playing: true));
    }

    [Fact]
    public void Reset_ForgetsWhereThePositionWas() {
        var clock = new PlaybackClock();
        var end = TimeSpan.FromSeconds(9.9);
        for (int i = 0; i < 4; i++) {
            clock.NoteTick(end, _tenSeconds, playing: true);
        }

        clock.Reset();

        Assert.False(clock.NoteTick(end, _tenSeconds, playing: true));
    }
}
