namespace Wander.Core.FileSystem;

/// <summary>
/// Are two files byte-for-byte the same? Read side by side and compared as
/// they come, rather than hashed: both files are right here, so the first
/// differing block is an answer and the rest of both stays unread, while a
/// checksum has to read each to the end every time. Sizes are the caller's
/// shortcut (<see cref="ConflictVerdict"/> settles that without I/O); this
/// one reads.
/// </summary>
public static class FileContentComparer {
    /// <summary>
    /// Above this the comparison is not started on its own - a window that
    /// reads two 4 GB files because they happened to share a name is a
    /// window that hangs on a network drive. The user can still ask for it.
    /// </summary>
    public const long AutoCompareLimit = 64L * 1024 * 1024;

    private const int BlockSize = 64 * 1024;


    /// <summary>True when the two files hold the same bytes.</summary>
    /// <exception cref="OperationCanceledException">Cancelled part-way; nothing is left open.</exception>
    public static bool AreIdentical(IFileSystem fs, string first, string second, CancellationToken ct = default) {
        using var left = fs.OpenRead(first);
        using var right = fs.OpenRead(second);

        if (left.CanSeek && right.CanSeek && left.Length != right.Length) {
            return false;
        }

        var leftBlock = new byte[BlockSize];
        var rightBlock = new byte[BlockSize];
        while (true) {
            ct.ThrowIfCancellationRequested();

            // ReadAtLeast fills the block unless the stream ends, so a short
            // read is the end of the file - not a network hiccup.
            int leftRead = left.ReadAtLeast(leftBlock, BlockSize, throwOnEndOfStream: false);
            int rightRead = right.ReadAtLeast(rightBlock, BlockSize, throwOnEndOfStream: false);
            if (leftRead != rightRead || !leftBlock.AsSpan(0, leftRead).SequenceEqual(rightBlock.AsSpan(0, rightRead))) {
                return false;
            }
            if (leftRead < BlockSize) {
                return true;
            }
        }
    }
}
