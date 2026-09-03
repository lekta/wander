using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

/// <summary>
/// Interactive resolver: one <see cref="ConflictWindow"/> per call, on the
/// UI thread. Always reached through <see cref="DispatcherConflictResolver"/>,
/// because batches run on the thread pool and this one has to be on the
/// dispatcher.
/// </summary>
public sealed class InteractiveConflictResolver : IConflictResolver {
    private readonly bool _skipIdentical;


    /// <param name="skipIdentical">
    /// The user's "don't ask about files that are already there
    /// byte-for-byte" setting, read at the moment the batch starts - see
    /// <see cref="Wander.Core.Persistence.AppSettings.SkipIdenticalOnConflict"/>.
    /// </param>
    public InteractiveConflictResolver(bool skipIdentical) {
        _skipIdentical = skipIdentical;
    }


    public IReadOnlyList<ConflictAnswer>? ResolveAll(ConflictRequest request) {
        return ConflictWindow.Show(request, _skipIdentical);
    }
}
