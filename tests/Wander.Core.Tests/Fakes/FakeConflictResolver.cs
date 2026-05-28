using Wander.Core.FileSystem;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="IConflictResolver"/> for tests. Pre-load a single
/// <see cref="StartBatchResult"/> (returned from the StartBatch call) and a
/// queue of per-item <see cref="ConflictResolution"/> values; the resolver
/// records every interaction in <see cref="StartBatchCalls"/> and
/// <see cref="ResolveCalls"/> so tests can assert on call shape.
/// </summary>
internal sealed class FakeConflictResolver : IConflictResolver {
    private readonly Queue<ConflictResolution> _perItem;


    public FakeConflictResolver(ConflictResolution? batchOverride = null, params ConflictResolution[] perItem) {
        StartBatchResult = batchOverride;
        _perItem = new Queue<ConflictResolution>(perItem);
    }


    public ConflictResolution? StartBatchResult { get; set; }
    public List<int> StartBatchCalls { get; } = new();
    public List<(string Src, string Dst)> ResolveCalls { get; } = new();


    public ConflictResolution? StartBatch(int conflictCount) {
        StartBatchCalls.Add(conflictCount);
        return StartBatchResult;
    }

    public ConflictResolution Resolve(FileConflictInfo conflict) {
        ResolveCalls.Add((conflict.Source.FullPath, conflict.ExistingTarget.FullPath));
        if (_perItem.Count == 0) {
            throw new InvalidOperationException($"FakeConflictResolver ran out of per-item answers (conflict on {conflict.Source.FullPath} -> {conflict.ExistingTarget.FullPath})");
        }
        return _perItem.Dequeue();
    }
}
