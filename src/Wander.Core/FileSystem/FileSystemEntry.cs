namespace Wander.Core.FileSystem;

public sealed record FileSystemEntry(
    string Name,
    string FullPath,
    EntryKind Kind,
    long? Size,
    DateTime ModifiedUtc,
    bool IsHidden,
    bool IsReadOnly,
    bool IsSystem);
