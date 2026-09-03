using System.Windows;
using System.Windows.Threading;
using Wander.App.Util;
using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

/// <summary>
/// Wraps a UI-thread resolver so async batch ops running on the thread pool
/// can still pop modal conflict dialogs. The call is marshalled back to
/// the dispatcher synchronously - the background worker blocks until the
/// user has answered for every item.
/// </summary>
public sealed class DispatcherConflictResolver : IConflictResolver {
    private readonly IConflictResolver _inner;
    private readonly Dispatcher _dispatcher;


    public DispatcherConflictResolver(IConflictResolver inner) {
        _inner = inner;
        _dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("No WPF Application available - DispatcherConflictResolver requires a UI dispatcher.");
    }


    public IReadOnlyList<ConflictAnswer>? ResolveAll(ConflictRequest request) {
        return _dispatcher.Ask(() => _inner.ResolveAll(request));
    }
}
