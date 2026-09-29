namespace Wander.Core.Imaging;

/// <summary>
/// A fixed number of slots for background work on files, handed out by
/// rank rather than by arrival: the lowest rank goes first, arrival order
/// breaks ties.
///
/// <para>
/// The rank is asked for when a slot frees, not when the wait begins. A
/// review helper waits for a cell that came on screen a second ago, and in
/// that second the user may have selected it or opened it in the preview -
/// the queue has to hear about that, and a rank fixed at the door would not.
/// </para>
///
/// <para>
/// A wait is cancellable: a cell scrolled away takes its file out of the
/// queue instead of leaving it to be measured for nobody.
/// </para>
/// </summary>
public sealed class RankedGate {
    private readonly Func<string, int> _rank;
    private readonly Lock _lock = new();
    private readonly List<Waiter> _waiting = [];
    private int _free;
    private long _arrivals;


    /// <param name="rank">The file's place in line now; lower goes first. Called under the gate's lock, from any thread.</param>
    public RankedGate(int slots, Func<string, int> rank) {
        _free = slots;
        _rank = rank;
    }


    /// <summary>Completes when a slot is taken for <paramref name="key"/>; pair with <see cref="Release"/>.</summary>
    public Task EnterAsync(string key, CancellationToken ct) {
        if (ct.IsCancellationRequested) {
            return Task.FromCanceled(ct);
        }

        lock (_lock) {
            if (_free > 0) {
                _free--;

                return Task.CompletedTask;
            }

            var waiter = new Waiter(key, _arrivals++);
            _waiting.Add(waiter);
            if (ct.CanBeCanceled) {
                waiter.Registration = ct.Register(() => Abandon(waiter, ct));
            }

            return waiter.Done.Task;
        }
    }


    public void Release() {
        Waiter? next;
        lock (_lock) {
            next = Best();
            if (next is null) {
                _free++;

                return;
            }

            _waiting.Remove(next);
        }

        next.Registration.Dispose();
        next.Done.TrySetResult();
    }


    private Waiter? Best() {
        Waiter? best = null;
        int bestRank = int.MaxValue;
        foreach (var waiter in _waiting) {
            int rank = _rank(waiter.Key);
            if (best is null || rank < bestRank || (rank == bestRank && waiter.Arrival < best.Arrival)) {
                best = waiter;
                bestRank = rank;
            }
        }

        return best;
    }


    private void Abandon(Waiter waiter, CancellationToken ct) {
        lock (_lock) {
            if (!_waiting.Remove(waiter)) {
                // Already handed a slot; the holder releases it.
                return;
            }
        }

        waiter.Done.TrySetCanceled(ct);
    }


    private sealed class Waiter(string key, long arrival) {
        public string Key { get; } = key;

        public long Arrival { get; } = arrival;

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration Registration { get; set; }
    }
}
