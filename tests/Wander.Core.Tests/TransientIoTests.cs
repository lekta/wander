using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class TransientIoTests {
    [Theory]
    [InlineData(21)]
    [InlineData(23)]
    [InlineData(31)]
    [InlineData(59)]
    [InlineData(64)]
    [InlineData(121)]
    [InlineData(1117)]
    [InlineData(1167)]
    [InlineData(1231)]
    public void ADeviceOrNetworkHiccup_IsTransient(int code) {
        Assert.True(TransientIo.IsTransient(Win32(code)));
    }

    [Theory]
    [InlineData(2)]       // file not found
    [InlineData(5)]       // access denied
    [InlineData(32)]      // in use - BusyGate's
    [InlineData(112)]     // disk full
    [InlineData(223)]     // too large for FAT32
    public void AnErrorThatWillHappenAgain_IsNot(int code) {
        Assert.False(TransientIo.IsTransient(Win32(code)));
    }

    [Fact]
    public void ACodeOutsideWin32_IsNotReadAsOne() {
        // 0x80131515 - a .NET HRESULT whose low word happens to be 5397;
        // and one built to end in 21 under another facility.
        Assert.False(TransientIo.IsTransient(new IOException("x", unchecked((int)0x80040015))));
        Assert.Equal(0, TransientIo.Win32Code(new IOException("x", unchecked((int)0x80131515))));
    }

    [Fact]
    public void ACancel_IsNeverTransient() {
        Assert.False(TransientIo.IsTransient(new OperationCanceledException()));
    }


    internal static IOException Win32(int code) {
        return new IOException($"Win32 {code}", unchecked((int)0x80070000) | code);
    }
}


public class IoRetryTests {
    private static readonly RetryPolicy _quick = new(3, new[] { TimeSpan.Zero });


    [Fact]
    public void TwoTransientFailures_ThenSuccess_TakesThreeTries() {
        int calls = 0;
        var retries = new List<int>();

        int attempts = IoRetry.Run(() => {
            if (++calls < 3) {
                throw TransientIoTests.Win32(23);
            }
        }, _quick, default, (attempt, _) => retries.Add(attempt));

        Assert.Equal(3, attempts);
        Assert.Equal(new[] { 2, 3 }, retries);
    }

    [Fact]
    public void ThreeTransientFailures_TheLastGoesOut() {
        int calls = 0;

        var ex = Assert.Throws<IOException>(() => IoRetry.Run(() => throw TransientIoTests.Win32(++calls == 3 ? 1117 : 21), _quick, default));

        Assert.Equal(3, calls);
        Assert.Equal(1117, TransientIo.Win32Code(ex));
    }

    [Fact]
    public void AnErrorThatIsNotTransient_GoesOutAtOnce() {
        int calls = 0;

        Assert.Throws<IOException>(() => IoRetry.Run(() => {
            calls++;
            throw TransientIoTests.Win32(5);
        }, _quick, default));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void ACancelDuringThePause_EndsItAtOnce() {
        using var cts = new CancellationTokenSource();
        var slow = new RetryPolicy(3, new[] { TimeSpan.FromMinutes(5) });
        int calls = 0;

        var started = DateTime.UtcNow;
        Assert.Throws<OperationCanceledException>(() => IoRetry.Run(() => {
            calls++;
            throw TransientIoTests.Win32(21);
        }, slow, cts.Token, (_, _) => cts.CancelAfter(50)));

        Assert.Equal(1, calls);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TheDelays_GoInOrder_AndTheLastRepeats() {
        var policy = new RetryPolicy(5, new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) });

        Assert.Equal(
            new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3) },
            new[] { policy.DelayBefore(2), policy.DelayBefore(3), policy.DelayBefore(4) });
    }
}
