using Wander.Core.FileSystem;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="IConflictResolver"/> for tests. Either one
/// <see cref="BatchAnswer"/> for every conflict of every call, or a queue of
/// per-item answers handed out in order; the resolver records every call in
/// <see cref="ResolveAllCalls"/> and every conflict it was shown in
/// <see cref="Conflicts"/> so tests can assert on call shape. It answers
/// exactly what it is asked - it does not walk a folder it was told to
/// merge, so the collisions inside come back as later questions. A Cancel
/// goes back inside the list, not as null: the executors must treat both
/// alike.
/// </summary>
internal sealed class FakeConflictResolver : IConflictResolver {
    private readonly Queue<ConflictResolution> _perItem;


    public FakeConflictResolver(ConflictResolution? batchOverride = null, params ConflictResolution[] perItem) {
        BatchAnswer = batchOverride;
        _perItem = new Queue<ConflictResolution>(perItem);
    }


    public ConflictResolution? BatchAnswer { get; set; }

    /// <summary>How many conflicts each call brought.</summary>
    public List<int> ResolveAllCalls { get; } = new();

    /// <summary>Every conflict ever shown, across calls, in order.</summary>
    public List<FileConflictInfo> Conflicts { get; } = new();


    public IReadOnlyList<ConflictAnswer>? ResolveAll(ConflictRequest request) {
        ResolveAllCalls.Add(request.Conflicts.Count);

        var answers = new List<ConflictAnswer>(request.Conflicts.Count);
        foreach (var conflict in request.Conflicts) {
            Conflicts.Add(conflict);
            if (BatchAnswer is { } forAll) {
                answers.Add(new ConflictAnswer(conflict, forAll));
            } else if (_perItem.Count > 0) {
                answers.Add(new ConflictAnswer(conflict, _perItem.Dequeue()));
            } else {
                throw new InvalidOperationException($"FakeConflictResolver ran out of per-item answers (conflict on {conflict.Source.FullPath} -> {conflict.ExistingTarget.FullPath})");
            }
        }

        return answers;
    }
}
