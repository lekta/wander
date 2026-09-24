namespace Wander.Core.FileSystem;

/// <summary>
/// One answer for every collision, asking nobody: "Извлечь рядом" keeps both
/// under a "(1)" name (2026-09-23), the way a copy into its own folder does -
/// a batch that asks nothing may not replace anything either.
/// </summary>
public sealed class FixedConflictResolver : IConflictResolver {
    private readonly ConflictResolution _answer;


    public FixedConflictResolver(ConflictResolution answer) {
        _answer = answer;
    }


    public IReadOnlyList<ConflictAnswer> ResolveAll(ConflictRequest request) {
        return request.Conflicts.Select(c => new ConflictAnswer(c, _answer)).ToList();
    }
}
