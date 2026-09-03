namespace Wander.Core.FileSystem;

/// <summary>
/// What a batch asks about: the collisions it found before touching
/// anything, and how many items the batch carries altogether, so the window
/// can say "3 of 10" rather than just "3".
/// </summary>
public sealed record ConflictRequest(IReadOnlyList<FileConflictInfo> Conflicts, int ItemCount);


/// <summary>
/// One decision, tied to the pair it is about. Pairs are matched by the
/// source path: the batch that asked walks the same folders again when it
/// applies the answers, so a pair found inside a merged folder - which the
/// window discovered, not the batch - is looked up the same way as a
/// top-level one.
/// </summary>
public sealed record ConflictAnswer(FileConflictInfo Conflict, ConflictResolution Resolution);
