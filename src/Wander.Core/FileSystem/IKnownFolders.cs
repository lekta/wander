namespace Wander.Core.FileSystem;

/// <summary>
/// Resolves well-known per-user folders that are not addressable through
/// <c>Environment.SpecialFolder</c> reliably (Downloads is the canonical
/// example — its localised display name varies and there is no
/// <c>SpecialFolder.Downloads</c> in the BCL).
/// Returns the absolute path as it exists on disk, or null if the folder
/// can't be resolved (rare — e.g. registry entries missing).
/// </summary>
public interface IKnownFolders {
    string? GetDownloads();
}
