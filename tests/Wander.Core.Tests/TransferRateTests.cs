using Wander.Core.Operations;

namespace Wander.Core.Tests;

public class TransferRateTests {
    private static readonly DateTime _start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void OneSample_SaysNothing() {
        var rate = new TransferRate();

        rate.Add(_start, 1000);

        Assert.Null(rate.BytesPerSecond);
        Assert.Null(rate.Remaining(9000));
    }

    [Fact]
    public void TwoSamplesTooCloseTogether_SayNothing() {
        // A tenth of a second between readings is the throttle's rhythm, not
        // a measurement: dividing by it gives noise.
        var rate = new TransferRate();

        rate.Add(_start, 0);
        rate.Add(_start.AddMilliseconds(100), 1_000_000);

        Assert.Null(rate.BytesPerSecond);
    }

    [Fact]
    public void Speed_IsBytesOverTheSpan() {
        var rate = new TransferRate();

        rate.Add(_start, 0);
        rate.Add(_start.AddSeconds(2), 20_000_000);

        Assert.Equal(10_000_000.0, rate.BytesPerSecond!.Value, 0);
    }

    [Fact]
    public void Speed_ForgetsWhatFellOutOfTheWindow() {
        // Fast for a second, then a tenth of that: the number has to follow
        // the disk it is on now, not the one it started on.
        var rate = new TransferRate(TimeSpan.FromSeconds(3));

        rate.Add(_start, 0);
        rate.Add(_start.AddSeconds(1), 100_000_000);
        for (int i = 2; i <= 8; i++) {
            rate.Add(_start.AddSeconds(i), 100_000_000 + (i - 1) * 10_000_000);
        }

        // The window now holds only the slow tail.
        Assert.Equal(10_000_000.0, rate.BytesPerSecond!.Value, 0);
    }

    [Fact]
    public void Remaining_IsWhatIsLeftAtTheCurrentSpeed() {
        var rate = new TransferRate();

        rate.Add(_start, 0);
        rate.Add(_start.AddSeconds(2), 20_000_000);

        Assert.Equal(TimeSpan.FromSeconds(5), rate.Remaining(50_000_000));
    }

    [Fact]
    public void Remaining_IsNull_WhenNothingIsLeft() {
        var rate = new TransferRate();

        rate.Add(_start, 0);
        rate.Add(_start.AddSeconds(2), 20_000_000);

        Assert.Null(rate.Remaining(0));
        Assert.Null(rate.Remaining(-1));
    }

    [Fact]
    public void Remaining_IsNull_WhenTheEstimateIsAbsurd() {
        // One byte a second against a terabyte: an honest "no idea" beats a
        // number counted in years.
        var rate = new TransferRate();

        rate.Add(_start, 0);
        rate.Add(_start.AddSeconds(2), 2);

        Assert.Null(rate.Remaining(1_000_000_000_000));
    }

    [Fact]
    public void ACounterThatWentBackwards_StartsOver() {
        // The true-up at the end of an item can subtract; a speed worked out
        // across that moment would come out negative.
        var rate = new TransferRate();

        rate.Add(_start, 0);
        rate.Add(_start.AddSeconds(2), 20_000_000);
        rate.Add(_start.AddSeconds(3), 19_000_000);

        Assert.Null(rate.BytesPerSecond);

        rate.Add(_start.AddSeconds(5), 39_000_000);
        Assert.Equal(10_000_000.0, rate.BytesPerSecond!.Value, 0);
    }

    [Fact]
    public void AStalledCopy_ReportsNoSpeed() {
        var rate = new TransferRate();

        rate.Add(_start, 5_000);
        rate.Add(_start.AddSeconds(2), 5_000);
        rate.Add(_start.AddSeconds(4), 5_000);

        Assert.Null(rate.BytesPerSecond);
        Assert.Null(rate.Remaining(1_000));
    }
}
