using System.Windows;
using System.Windows.Threading;
using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

/// <summary>
/// Wraps a UI-thread resolver so async batch ops running on the thread pool
/// can still pop modal conflict dialogs. Every call is marshalled back to
/// the dispatcher synchronously — the background worker blocks until the
/// user clicks a button.
/// </summary>
public sealed class DispatcherConflictResolver : IConflictResolver {
    private readonly IConflictResolver _inner;
    private readonly Dispatcher _dispatcher;


    public DispatcherConflictResolver(IConflictResolver inner) {
        _inner = inner;
        _dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("No WPF Application available — DispatcherConflictResolver requires a UI dispatcher.");
    }


    public ConflictResolution? StartBatch(int conflictCount) {
        return _dispatcher.CheckAccess()
            ? _inner.StartBatch(conflictCount)
            : _dispatcher.Invoke(() => _inner.StartBatch(conflictCount));
    }

    public ConflictResolution Resolve(FileConflictInfo conflict) {
        return _dispatcher.CheckAccess()
            ? _inner.Resolve(conflict)
            : _dispatcher.Invoke(() => _inner.Resolve(conflict));
    }
}
