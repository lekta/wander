namespace Wander.Core.Operations;

/// <param name="Free">The path was let go - the operation goes ahead.</param>
/// <param name="WasBusy">It was held at the first look, whatever happened after: worth a line in the journal either way.</param>
/// <param name="Waited">How long this wait took out of the budget.</param>
public readonly record struct BusyWaitResult(bool Free, bool WasBusy, TimeSpan Waited);


/// <summary>
/// How long a file operation waits for a path somebody holds (PLAN AF,
/// decision of 2026-09-21): a look every <see cref="DefaultStep"/>, for up
/// to <see cref="DefaultBudget"/> - long enough for a reader of ours to let
/// go, short enough that the question comes before the user starts to
/// wonder. Let go sooner, it goes ahead sooner.
///
/// <para>
/// One instance per operation, because the budget is the operation's, not
/// the file's: a hundred files held by a program that is not going to let
/// go must cost two seconds, not two hundred. Once the budget is spent every
/// further busy path is reported at the first look.
/// </para>
/// </summary>
public sealed class BusyWait {
    public static readonly TimeSpan DefaultStep = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(2);

    private readonly Action<TimeSpan> _pause;
    private readonly TimeSpan _step;


    /// <param name="pause">The sleep, so a test does not have to.</param>
    /// <param name="step">How often to look again.</param>
    /// <param name="budget">All the waiting this operation is allowed.</param>
    public BusyWait(Action<TimeSpan>? pause = null, TimeSpan? step = null, TimeSpan? budget = null) {
        _pause = pause ?? Thread.Sleep;
        _step = step ?? DefaultStep;
        Remaining = budget ?? DefaultBudget;
    }


    /// <summary>What is left of the operation's budget.</summary>
    public TimeSpan Remaining { get; private set; }


    /// <exception cref="OperationCanceledException">The operation was cancelled while waiting.</exception>
    public BusyWaitResult UntilFree(Func<bool> isBusy, CancellationToken ct = default) {
        if (!isBusy()) {
            return new BusyWaitResult(Free: true, WasBusy: false, TimeSpan.Zero);
        }

        var waited = TimeSpan.Zero;
        while (Remaining > TimeSpan.Zero) {
            ct.ThrowIfCancellationRequested();
            var pause = Remaining < _step ? Remaining : _step;
            _pause(pause);
            waited += pause;
            Remaining -= pause;
            if (!isBusy()) {
                return new BusyWaitResult(Free: true, WasBusy: true, waited);
            }
        }

        return new BusyWaitResult(Free: false, WasBusy: true, waited);
    }
}
