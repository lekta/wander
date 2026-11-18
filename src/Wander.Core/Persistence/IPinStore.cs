using Wander.Core.Pins;

namespace Wander.Core.Persistence;

/// <summary>
/// Where the pins (<see cref="PinBook"/>) live between sessions -
/// <c>pins.json</c> next to <c>state.json</c>. Its own file for the reasons
/// <see cref="IFolderSettingsStore"/> has one: many small records that grow
/// with use, and a format version of its own.
/// </summary>
public interface IPinStore {
    /// <summary>
    /// True while this instance must not write - the same rule as
    /// <see cref="IAppStateStore.IsReadOnly"/>: another instance owns the
    /// data folder.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>Everything on disk; empty when there is no file or it cannot be read.</summary>
    IReadOnlyList<PinFolder> Load();

    /// <summary>Replaces the file with <paramref name="folders"/>. Best effort: a failure to persist never surfaces.</summary>
    void Save(IReadOnlyList<PinFolder> folders);
}
