using Wander.Core.Diagnostics;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class BusyGateTests {
    private const string Shot = @"C:\photos\shot.cr3";
    private const string Other = @"C:\photos\other.cr3";
    private const string Album = @"C:\photos\album";

    private static readonly TimeSpan _step = BusyWait.DefaultStep;


    [Fact]
    public void WaitForFile_Free_CostsOneLook_AndReportsNothing() {
        var (held, probe, pauses, _) = Setup();

        using var gate = held.Begin(NullLogger.Instance);

        Assert.Null(gate.WaitForFile(Shot, default));
        Assert.Single(probe.Looks);
        Assert.Empty(pauses);
    }

    [Fact]
    public void WaitForFile_LetGoAfterAWhile_GoesAhead_AndNamesWhoHeldIt() {
        var (held, probe, pauses, _) = Setup(new FileLockInfo(812, "Word"));
        probe.HeldFor[Shot] = 2;

        using var gate = held.Begin(NullLogger.Instance);
        var report = gate.WaitForFile(Shot, default);

        Assert.Equal(new BusyReport(Shot, "Word (PID 812)", 2 * _step, Released: true), report);
        Assert.Equal(new[] { _step, _step }, pauses);
    }

    [Fact]
    public void WaitForFile_NeverLetGo_FailsInUse_OnceTheBudgetIsSpent() {
        var (held, probe, pauses, _) = Setup();
        probe.HeldFor[Shot] = int.MaxValue;

        using var gate = held.Begin(NullLogger.Instance);
        var ex = Assert.ThrowsAny<IOException>(() => gate.WaitForFile(Shot, default));

        Assert.True(FileInUse.Is(ex));
        Assert.Equal(BusyWait.DefaultBudget, Sum(pauses));
    }

    [Fact]
    public void TheBudget_IsTheOperations_NotAFiles() {
        // A hundred files held by a program that is not letting go cost two
        // seconds, not two hundred: the second one is given up on at once.
        var (held, probe, pauses, _) = Setup();
        probe.HeldFor[Shot] = int.MaxValue;
        probe.HeldFor[Other] = int.MaxValue;

        using var gate = held.Begin(NullLogger.Instance);
        Assert.ThrowsAny<IOException>(() => gate.WaitForFile(Shot, default));
        Assert.ThrowsAny<IOException>(() => gate.WaitForFile(Other, default));

        Assert.Equal(BusyWait.DefaultBudget, Sum(pauses));
    }

    [Fact]
    public void WaitForFile_Cancelled_StopsWaiting() {
        var (held, probe, _, _) = Setup();
        probe.HeldFor[Shot] = int.MaxValue;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var gate = held.Begin(NullLogger.Instance);

        Assert.Throws<OperationCanceledException>(() => gate.WaitForFile(Shot, cts.Token));
    }

    [Fact]
    public void Retry_TriesTheOperationAgain_WhileItAnswersInUse() {
        var (held, _, pauses, _) = Setup();
        int tries = 0;

        using var gate = held.Begin(NullLogger.Instance);
        var report = gate.Retry(Album, () => {
            if (++tries < 3) {
                throw FileInUse.Error(Album);
            }
        }, default);

        Assert.Equal(3, tries);
        Assert.NotNull(report);
        Assert.True(report.Released);
        Assert.Equal(2 * _step, report.Waited);
        Assert.Equal(2, pauses.Count);
    }

    [Fact]
    public void Retry_AnyOtherFailure_IsNotWaitedFor() {
        var (held, _, pauses, _) = Setup();

        using var gate = held.Begin(NullLogger.Instance);

        Assert.Throws<UnauthorizedAccessException>(() => gate.Retry(Album, () => throw new UnauthorizedAccessException(), default));
        Assert.Empty(pauses);
    }

    [Fact]
    public void Retry_NeverLetGo_ThrowsTheOperationsOwnFailure() {
        var (held, _, _, _) = Setup();
        var inUse = FileInUse.Error(Album);

        using var gate = held.Begin(NullLogger.Instance);
        var thrown = Assert.ThrowsAny<IOException>(() => gate.Retry(Album, () => throw inUse, default));

        Assert.Same(inUse, thrown);
    }

    [Fact]
    public void RetryMany_SendsTheHeldOnesAgain_AllTogether_UntilLetGo() {
        var (held, _, pauses, _) = Setup();
        var paths = new[] { Shot, Other, Album };
        var heldLooks = new Dictionary<int, int> { [0] = 1, [2] = 3 };
        var rounds = new List<int[]>();

        using var gate = held.Begin(NullLogger.Instance);
        var reports = gate.RetryMany(paths, indices => {
            rounds.Add(indices.ToArray());

            return indices.Where(i => heldLooks.ContainsKey(i) && heldLooks[i]-- > 0).ToList();
        }, default);

        Assert.Equal(new[] { 0, 1, 2 }, rounds[0]);
        Assert.Equal(new[] { 0, 2 }, rounds[1]);
        Assert.All(rounds.Skip(2), round => Assert.Equal(new[] { 2 }, round));
        Assert.Equal(new[] { 0, 2 }, reports.Keys.Order());
        Assert.Equal(_step, reports[0].Waited);
        Assert.Equal(3 * _step, reports[2].Waited);
        Assert.All(reports.Values, r => Assert.True(r.Released));
        Assert.Equal(3, pauses.Count);
    }

    [Fact]
    public void RetryMany_Cancelled_KeepsWhatWasDone_AndReportsTheRestHeld() {
        var (held, _, _, _) = Setup();
        using var cts = new CancellationTokenSource();

        using var gate = held.Begin(NullLogger.Instance);
        var reports = gate.RetryMany(new[] { Shot, Other }, indices => {
            cts.Cancel();

            return indices.Where(i => i == 1).ToList();
        }, cts.Token);

        Assert.False(Assert.Single(reports).Value.Released);
    }

    [Fact]
    public void Check_AnotherUserOperation_IsNamed_AndTheItemLeftAlone() {
        var (held, _, _, _) = Setup();
        using var copy = held.Claims.Claim(new[] { Album }, ClaimKind.UserOperation, OperationVerbs.Copy);

        using var gate = held.Begin(NullLogger.Instance);
        var ex = Assert.Throws<ClaimedByOperationException>(() => gate.Check(Album + @"\shot.cr3"));

        Assert.Equal(OperationVerbs.Copy, ex.Verb);
    }

    [Fact]
    public void Check_TheOperationsOwnClaim_IsNeverInItsWay() {
        var (held, _, _, _) = Setup();
        using var own = held.Claims.Claim(new[] { Album }, ClaimKind.UserOperation, OperationVerbs.Move);

        using var gate = held.Begin(NullLogger.Instance, own);

        gate.Check(Album);
    }

    [Fact]
    public void Check_ABackgroundReader_IsToldToLetGo_AndNamedAsOurs() {
        var (held, probe, _, _) = Setup(new FileLockInfo(1, "Wander"));
        using var reader = new CancellationTokenSource();
        using var thumbnail = held.Claims.Claim(new[] { Shot }, ClaimKind.Background, ClaimOwners.Thumbnail, reader);
        probe.HeldFor[Shot] = 1;

        using var gate = held.Begin(NullLogger.Instance);
        gate.Check(Shot);
        var report = gate.WaitForFile(Shot, default);

        Assert.True(reader.IsCancellationRequested);
        Assert.NotNull(report);
        Assert.NotNull(report.Holder);
        Assert.NotEqual("Wander (PID 1)", report.Holder);
    }

    [Fact]
    public void Dispose_WritesTheOperationsClaimsLine() {
        var (held, _, _, _) = Setup();
        var log = new Lines();

        using (var gate = held.Begin(log)) {
            gate.Check(Shot);
            gate.Check(Other);
        }

        Assert.Contains(log.Info, line => line.StartsWith("Claims: 2 lookups", StringComparison.Ordinal));
    }


    private static (HeldPaths Held, FakeBusyProbe Probe, List<TimeSpan> Pauses, PathClaims Claims) Setup(
        FileLockInfo? holder = null) {
        var claims = new PathClaims();
        var probe = new FakeBusyProbe();
        var pauses = new List<TimeSpan>();
        var locks = new FixedLocks(holder);
        var held = new HeldPaths(claims, probe, locks, () => new BusyWait(pauses.Add));

        return (held, probe, pauses, claims);
    }

    private static TimeSpan Sum(IEnumerable<TimeSpan> pauses) {
        return pauses.Aggregate(TimeSpan.Zero, (a, b) => a + b);
    }


    private sealed class FixedLocks : IFileLockInspector {
        private readonly FileLockInfo? _holder;


        public FixedLocks(FileLockInfo? holder) {
            _holder = holder;
        }


        public IReadOnlyList<FileLockInfo> WhoIsLocking(string filePath) {
            return _holder is null ? Array.Empty<FileLockInfo>() : new[] { _holder };
        }
    }

    private sealed class Lines : ILogger {
        public List<string> Info { get; } = new();


        void ILogger.Info(string message) => Info.Add(message);

        public void Warn(string message) => Info.Add(message);

        public void Error(string message, Exception? ex = null) => Info.Add(message);
    }
}
