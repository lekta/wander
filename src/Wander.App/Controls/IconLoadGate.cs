namespace Wander.App.Controls;

/// <summary>
/// The gate in front of shell icon loads: a fixed number of slots and two
/// queues for whoever is waiting, so that a load for something on screen
/// goes ahead of a load for something that is not.
///
/// <para>
/// A plain semaphore is first come, first served, and "first" is decided
/// by container order, not by what the user can see: a table keeps a page
/// of rows realised above and below the viewport, a tree keeps every
/// expanded node, and all of them ask for icons the moment they exist. The
/// urgent queue is served whenever a slot frees; the other only when the
/// urgent one is empty. With everything marked urgent it is the semaphore
/// it replaced - which is what the setting behind it turns off to.
/// </para>
/// </summary>
internal sealed class IconLoadGate {
    private readonly object _lock = new();
    private readonly Queue<TaskCompletionSource<bool>> _urgent = new();
    private readonly Queue<TaskCompletionSource<bool>> _later = new();
    private int _free;


    public IconLoadGate(int slots) {
        _free = slots;
    }


    /// <summary>Completes when a slot is taken; pair with <see cref="Release"/>.</summary>
    public Task WaitAsync(bool urgent) {
        lock (_lock) {
            if (_free > 0) {
                _free--;

                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            (urgent ? _urgent : _later).Enqueue(waiter);

            return waiter.Task;
        }
    }


    public void Release() {
        TaskCompletionSource<bool>? next = null;
        lock (_lock) {
            if (_urgent.Count > 0) {
                next = _urgent.Dequeue();
            } else if (_later.Count > 0) {
                next = _later.Dequeue();
            } else {
                _free++;
            }
        }

        next?.SetResult(true);
    }
}
