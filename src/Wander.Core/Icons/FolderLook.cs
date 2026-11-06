namespace Wander.Core.Icons;

/// <summary>
/// How a view wants a folder's own picture drawn (PLAN F1, decisions
/// 2026-11-06): the gallery lays up to four of its files out as the
/// folder's face (<see cref="Grid"/>), the other views slide cards into
/// it; how many cards follows the size asked for. A file's picture ignores it.
/// </summary>
public readonly record struct FolderLook(bool Grid) {
    /// <summary>What this look adds to a folder's cache key: nothing for the default, so the plain key stays the cascade.</summary>
    public string KeySuffix => Grid ? "|grid" : "";
}
