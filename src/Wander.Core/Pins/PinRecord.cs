namespace Wander.Core.Pins;

/// <summary>
/// One pinned file or folder, as its parent folder holds it: the name, when
/// it was pinned, and whether it went to the Recycle Bin - a pin in the bin
/// is kept out of sight and out of the sweep until the item is back or gone
/// for good.
/// </summary>
public sealed record PinRecord(string Name, DateTime PinnedUtc, bool InRecycleBin = false);


/// <summary>
/// A folder's pins as <c>pins.json</c> holds them. <paramref name="Key"/> is
/// the folder's storage key (<see cref="PinKeys"/>): its path, or for a
/// removable volume its serial and the path below the root.
/// </summary>
public sealed record PinFolder(string Key, IReadOnlyList<PinRecord> Pins);
