using System.Diagnostics;
using Wander.Core.Actions;

namespace Wander.Core.Tests;

/// <summary>
/// The debug hold of PLAN AI2. Real files in a real temp folder rather than
/// <c>FakeFileSystem</c>: what is being tested is an exclusive share mode,
/// which only the operating system can answer for.
/// </summary>
public class HoldFileActionTests : IDisposable {
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wander-hold-file-tests", Guid.NewGuid().ToString("N"));


    public HoldFileActionTests() {
        Directory.CreateDirectory(_dir);
    }


    public void Dispose() {
        Directory.Delete(_dir, recursive: true);
    }


    [Fact]
    public async Task HoldsTheFileExclusively_WhileItRuns_AndLetsGoAfter() {
        string path = MakeFile(nameof(HoldsTheFileExclusively_WhileItRuns_AndLetsGoAfter));
        var action = new HoldFileAction();

        var run = action.RunAsync(path, null, "seconds=1", CancellationToken.None);
        // While it holds, nobody else gets the file - not even for reading.
        Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read).Dispose());

        await run;

        // And after, as if nothing had happened.
        new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose();
    }

    [Fact]
    public async Task HoldsForAsLongAsItWasAsked() {
        string path = MakeFile(nameof(HoldsForAsLongAsItWasAsked));
        var action = new HoldFileAction();

        var clock = Stopwatch.StartNew();
        await action.RunAsync(path, null, "seconds=1", CancellationToken.None);

        // Not "exactly one second": a loaded machine oversleeps. What the
        // test is about is that the wait happened at all.
        Assert.InRange(clock.Elapsed.TotalMilliseconds, 900, 10_000);
    }

    [Fact]
    public async Task CancelLetsGoAtOnce() {
        string path = MakeFile(nameof(CancelLetsGoAtOnce));
        var action = new HoldFileAction();

        using var cts = new CancellationTokenSource();
        var run = action.RunAsync(path, null, "seconds=600", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose();
    }

    [Fact]
    public async Task NoArgumentsAtAll_StillHolds() {
        // The preset always names its seconds; a row copied and emptied by
        // hand must not throw its way out of the run.
        string path = MakeFile(nameof(NoArgumentsAtAll_StillHolds));
        var action = new HoldFileAction();

        using var cts = new CancellationTokenSource();
        var run = action.RunAsync(path, null, string.Empty, cts.Token);
        Assert.False(run.IsCompleted);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }


    private string MakeFile(string name) {
        string path = Path.Combine(_dir, name + ".bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        return path;
    }
}
