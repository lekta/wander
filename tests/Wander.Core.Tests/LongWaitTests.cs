using Wander.Core.Diagnostics;
using Wander.Core.Logging;

namespace Wander.Core.Tests;

public class LongWaitTests {
    [Fact]
    public async Task AWaitThatFinishesInTime_WritesNothing() {
        var log = new Lines();

        int result = await LongWait.WatchAsync(Task.FromResult(42), log, "quick", TimeSpan.FromSeconds(1));

        Assert.Equal(42, result);
        Assert.Empty(log.Written);
    }

    [Fact]
    public async Task AWaitPastTheThreshold_IsNamedTwice_AndStillReturnsItsResult() {
        var log = new Lines();
        var pending = new TaskCompletionSource<string>();

        var watched = LongWait.WatchAsync(pending.Task, log, "listing pack.rar", TimeSpan.FromMilliseconds(20));
        await Task.Delay(100);
        pending.SetResult("done");

        Assert.Equal("done", await watched);
        Assert.Equal(2, log.Written.Count);
        Assert.StartsWith("SLOW wait: listing pack.rar - still running after 0 s", log.Written[0]);
        Assert.StartsWith("SLOW done: listing pack.rar - took ", log.Written[1]);
    }

    [Fact]
    public async Task AFailedWait_StillGetsItsDoneLine_AndTheFaultComesThrough() {
        var log = new Lines();
        var pending = new TaskCompletionSource<int>();

        var watched = LongWait.WatchAsync(pending.Task, log, "unpacking", TimeSpan.FromMilliseconds(20));
        await Task.Delay(100);
        pending.SetException(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => watched);
        Assert.Equal(2, log.Written.Count);
        Assert.StartsWith("SLOW done: unpacking", log.Written[1]);
    }

    [Fact]
    public async Task ATaskWithoutAResult_IsWatchedTheSameWay() {
        var log = new Lines();
        var pending = new TaskCompletionSource();

        var watched = LongWait.WatchAsync(pending.Task, log, "tree", TimeSpan.FromMilliseconds(20));
        await Task.Delay(100);
        pending.SetResult();
        await watched;

        Assert.Equal(2, log.Written.Count);
    }


    private sealed class Lines : ILogger {
        public List<string> Written { get; } = new();

        public void Info(string message) => Written.Add(message);
        public void Warn(string message) => Written.Add("WARN " + message);
        public void Error(string message, Exception? ex = null) => Written.Add("ERROR " + message);
    }
}
