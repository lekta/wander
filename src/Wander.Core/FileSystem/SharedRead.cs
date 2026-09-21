namespace Wander.Core.FileSystem;

/// <summary>
/// How Wander reads a user's file for itself - a preview, a cover, a
/// thumbnail, a mesh - so that the read never stands in the way of what
/// the user does with the file (PLAN AF, decision of 2026-09-21).
///
/// <para>
/// Opened with <see cref="FileShare.Delete"/>: a file open here can still be
/// deleted, sent to the bin and brought back, or renamed, while the read
/// carries on from its handle (stand of 2026-09-21). Without it the
/// delete fails "in use" and names Wander itself. <see cref="FileShare.ReadWrite"/>
/// besides: a file another program is still writing is shown, not refused.
/// </para>
///
/// <para>
/// Not for an operation the user asked for (a conversion reads its input as
/// the user's work, see PLAN block 0, step 5) and not for Wander's own files.
/// </para>
/// </summary>
public static class SharedRead {
    private const FileShare Share = FileShare.ReadWrite | FileShare.Delete;


    public static FileStream Open(string path, int bufferSize = 4096, FileOptions options = FileOptions.None) {
        return new FileStream(path, FileMode.Open, FileAccess.Read, Share, bufferSize, options);
    }

    /// <summary>
    /// The whole file, as long as it was when opened. A writer that cuts it
    /// short in the meantime gets what was there; one that grows it is not
    /// waited for.
    /// </summary>
    public static byte[] ReadAllBytes(string path) {
        using var stream = Open(path, bufferSize: 1);
        long length = stream.Length;
        if (length > Array.MaxLength) {
            throw new IOException($"'{path}' is too large to read into memory ({length} bytes).");
        }

        var bytes = new byte[length];
        int read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);

        return read == bytes.Length ? bytes : bytes[..read];
    }

    /// <summary>UTF-8 unless a byte order mark says otherwise - what <see cref="File.ReadAllText(string)"/> does.</summary>
    public static string ReadAllText(string path) {
        using var reader = new StreamReader(Open(path));

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Line by line, like <see cref="File.ReadLines(string)"/> - except that
    /// the file is opened on the first line asked for, not on the call.
    /// </summary>
    public static IEnumerable<string> ReadLines(string path) {
        using var reader = new StreamReader(Open(path));
        while (reader.ReadLine() is { } line) {
            yield return line;
        }
    }
}
