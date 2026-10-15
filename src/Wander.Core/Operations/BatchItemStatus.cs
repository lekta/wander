namespace Wander.Core.Operations;

/// <summary>
/// How one item of a copy, a move, an extraction or a delete ended. Lives
/// with the operations rather than with the batch that fills it in: the
/// tracker keeps finished operations item by item
/// (<see cref="OperationItem"/>), and the tracker sits below the file system.
/// </summary>
public enum BatchItemStatus { Ok, Skipped, Replaced, Renamed, Merged, Cancelled, Failed }
