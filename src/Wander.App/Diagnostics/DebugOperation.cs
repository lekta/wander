using Wander.Core.Logging;
using Wander.Core.Operations;

namespace Wander.App.Diagnostics;

/// <summary>What the debug menu asks of <see cref="DebugOperation"/>.</summary>
public enum DebugOperationScenario {
    /// <summary>Eight files, no surprises.</summary>
    Plain,

    /// <summary>The fourth item fails; the rest go on.</summary>
    FailsOnFourth,

    /// <summary>The run cancels itself partway through the fifth item.</summary>
    CancelsMidway,
}


/// <summary>How a debug run ended. <see cref="Total"/> is what it set out to do.</summary>
public sealed record DebugOperationOutcome(int Ok, int Failed, int Total);


/// <summary>
/// A file operation that moves no files (PLAN AI1): eight made-up names,
/// made-up sizes, and <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// where the disk would be. It exists so the parts around an operation -
/// the progress window, the status-bar panel, the aggregate bar, cancelling,
/// several operations at once - can be looked at on demand, without finding
/// a folder big enough to copy and without waiting for a real one to fail.
///
/// <para>
/// Touches nothing: no path here exists, and the one place it could be
/// mistaken for real work is the "current file" line, which is the point.
/// It reports into the same <see cref="OperationTracker"/> as everything
/// else, so what is on screen is the real display, not a mock of it.
/// </para>
/// </summary>
public static class DebugOperation {
    /// <summary>Long enough to see "считаем" before the numbers arrive.</summary>
    private static readonly TimeSpan _weighFor = TimeSpan.FromMilliseconds(300);

    /// <summary>One byte report per tick, the rate a real copy reports at.</summary>
    private static readonly TimeSpan _tick = TimeSpan.FromMilliseconds(100);

    /// <summary>Into the fifth item, not before it: a cancel in the middle of a file is the interesting one.</summary>
    private static readonly TimeSpan _cancelAfter = TimeSpan.FromSeconds(1);

    private const int FailsAt = 3;
    private const int CancelsAt = 4;

    /// <summary>What the failing item says. English, like every other log line.</summary>
    private const string FailureMessage = "Debug failure, on purpose";

    /// <summary>
    /// The made-up batch: a RAW shoot's worth of files, 20-80 MB each,
    /// 2-5 seconds each. Fixed rather than random so two runs look the same
    /// and a difference on screen is a difference in Wander.
    /// </summary>
    private static readonly Item[] _items = {
        new(@"C:\wander-debug\shot-01.cr3", 24, 2),
        new(@"C:\wander-debug\shot-02.cr3", 31, 3),
        new(@"C:\wander-debug\shot-03.cr3", 78, 5),
        new(@"C:\wander-debug\shot-04.cr3", 42, 3),
        new(@"C:\wander-debug\shot-05.cr3", 67, 4),
        new(@"C:\wander-debug\shot-06.cr3", 20, 2),
        new(@"C:\wander-debug\shot-07.cr3", 55, 4),
        new(@"C:\wander-debug\shot-08.cr3", 36, 3),
    };


    /// <summary>
    /// Runs one scenario under <paramref name="ct"/> - the token of the
    /// progress window, which is also what the operation registers under, so
    /// the window finds it the way it finds a real one.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// The user cancelled, or <see cref="DebugOperationScenario.CancelsMidway"/>
    /// cancelled for them. The caller cannot tell the two apart, and that is
    /// the point of the scenario.
    /// </exception>
    public static async Task<DebugOperationOutcome> RunAsync(
        DebugOperationScenario scenario, OperationTracker tracker, ILogger log, CancellationToken ct) {

        log.Info($"Debug operation: {scenario}, {_items.Length} item(s), nothing on disk");

        // The scenario's own cancel, and the user's, arrive as one token;
        // the operation is still registered under the window's.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = stop.Token;

        using var operation = tracker.Begin(OperationVerbs.Debug, _items.Length, token: ct);

        operation.SetWeighing(true);
        await Task.Delay(_weighFor, token).ConfigureAwait(false);
        operation.SetWeighing(false);
        operation.SetTotalBytes(_items.Sum(i => i.Bytes));

        int ok = 0;
        int failed = 0;
        for (int i = 0; i < _items.Length; i++) {
            var item = _items[i];
            operation.SetCurrentPath(item.Path);

            if (scenario == DebugOperationScenario.FailsOnFourth && i == FailsAt) {
                // Block 4 will carry this as a Failed item with an
                // IOException of its own (OPERATIONS.md, op.Finish); until
                // then the count and the log line are the whole report.
                failed++;
                log.Warn($"Debug operation: {item.Path} failed - {FailureMessage}");
                operation.Advance(item.Path);
                continue;
            }

            if (scenario == DebugOperationScenario.CancelsMidway && i == CancelsAt) {
                log.Info($"Debug operation: cancelling itself {_cancelAfter.TotalSeconds:F0} s into {item.Path}");
                stop.CancelAfter(_cancelAfter);
            }

            await MoveAsync(operation, item, token).ConfigureAwait(false);
            ok++;
            operation.Advance(item.Path);
        }

        log.Info($"Debug operation done: {ok} ok, {failed} failed");

        return new DebugOperationOutcome(ok, failed, _items.Length);
    }


    /// <summary>One item's worth of bytes, reported at the rate a copy reports.</summary>
    private static async Task MoveAsync(IOperationHandle operation, Item item, CancellationToken ct) {
        int ticks = (int)(item.Duration / _tick);
        long perTick = item.Bytes / ticks;
        for (int i = 0; i < ticks; i++) {
            await Task.Delay(_tick, ct).ConfigureAwait(false);
            operation.AdvanceBytes(perTick);
        }

        // What integer division left over, so the bar ends where it should.
        operation.AdvanceBytes(item.Bytes - (perTick * ticks));
    }


    /// <param name="Megabytes">Its made-up size; <see cref="Bytes"/> is what the display counts.</param>
    /// <param name="Seconds">How long it pretends to take.</param>
    private sealed record Item(string Path, int Megabytes, int Seconds) {
        public long Bytes => (long)Megabytes * 1024 * 1024;

        public TimeSpan Duration => TimeSpan.FromSeconds(Seconds);
    }
}
