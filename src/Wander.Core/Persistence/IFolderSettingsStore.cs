using Wander.Core.Folders;

namespace Wander.Core.Persistence;

/// <summary>
/// Where the per-folder records (<see cref="FolderRecord"/>) live between
/// sessions - <c>folders.json</c> next to <c>state.json</c>. Its own file
/// rather than a block in the state: the state is a few dozen values read
/// once, this is thousands of small records that grow with use
/// (REJECTED, "binary state.json"), and it carries its own format version.
/// </summary>
public interface IFolderSettingsStore {
    /// <summary>
    /// True while this instance must not write - the same rule as
    /// <see cref="IAppStateStore.IsReadOnly"/>: another instance owns the
    /// data folder.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>Everything on disk; empty when there is no file or it cannot be read.</summary>
    IReadOnlyList<FolderRecord> Load();

    /// <summary>Replaces the file with <paramref name="records"/>. Best effort: a failure to persist never surfaces.</summary>
    void Save(IReadOnlyList<FolderRecord> records);
}
