using Wander.Core.Logging;

namespace Wander.Core.Tests;

/// <summary>Audit 2026-11-18, P10: a task let go says how it failed, by name, when it fails.</summary>
public class TaskForgetTests {

    [Fact]
    public async Task AFailedTask_IsAnErrorWithItsName() {
        var log = new Lines();
        var pending = new TaskCompletionSource();

        pending.Task.Forget(log, "Preview update");
        pending.SetException(new InvalidOperationException("broken parser"));
        await WhenWritten(log);

        var line = Assert.Single(log.Written);
        Assert.Equal("ERROR Preview update failed", line.Text);
        Assert.IsType<InvalidOperationException>(line.Error);
    }

    [Fact]
    public async Task ACancelledTask_SaysNothing() {
        var log = new Lines();
        var pending = new TaskCompletionSource();

        pending.Task.Forget(log, "Folder listing");
        pending.SetCanceled();
        await Task.Delay(50);

        Assert.Empty(log.Written);
    }

    [Fact]
    public async Task ATaskThatSucceeds_SaysNothing() {
        var log = new Lines();

        Task.CompletedTask.Forget(log, "Rows re-read");
        await Task.Delay(50);

        Assert.Empty(log.Written);
    }

    private static async Task WhenWritten(Lines log) {
        for (int i = 0; i < 200 && log.Written.Count == 0; i++) {
            await Task.Delay(10);
        }
    }

    private sealed class Lines : ILogger {
        public List<(string Text, Exception? Error)> Written { get; } = new();

        public void Info(string message) => Written.Add((message, null));
        public void Warn(string message) => Written.Add(("WARN " + message, null));
        public void Error(string message, Exception? ex = null) => Written.Add(("ERROR " + message, ex));
    }
}
