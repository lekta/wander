namespace Wander.Core.Operations;

/// <summary>
/// What moved a file's bytes: Wander's own loop, which hashes on the way
/// and can read the copy back (FILEOPS, "copy check"), or Windows'
/// <c>CopyFileEx</c>, which keeps what the loop cannot - encryption, sparse
/// files, extra streams, a copy kept on the server. Written into the
/// operation's record and log line item by item, so a complaint about a
/// copy can be told apart by engine.
/// </summary>
public enum CopyEngine {
    /// <summary>No bytes were copied - a rename, a folder made, a skip.</summary>
    None,

    Own,

    System,
}
