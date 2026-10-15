using System.Buffers;
using System.Security.Cryptography;

namespace Wander.Core.FileSystem;

/// <summary>
/// Wander's own copy loop over two streams: read a buffer, hash it, write
/// it, report it - so the copy can be checked later against what was read,
/// without reading the source a second time (on a card that is the slow
/// half). SHA-256 from the BCL: hardware-accelerated and faster than any
/// USB link. The streams are the caller's - a file, a device's stream; the
/// loop knows neither.
/// </summary>
public static class StreamCopy {
    /// <summary>One read, one write: a megabyte (OPERATIONS, "Проверка копии": 1-4 MB, the stand decides).</summary>
    public const int BufferSize = 1 << 20;


    /// <summary>
    /// Copies <paramref name="from"/> to <paramref name="to"/> to the end,
    /// telling <paramref name="progress"/> each buffer, and looking at
    /// <paramref name="ct"/> between them.
    /// </summary>
    /// <returns>The SHA-256 of every byte that went across.</returns>
    public static byte[] Copy(Stream from, Stream to, IProgress<long>? progress, CancellationToken ct, int bufferSize = BufferSize) {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try {
            while (true) {
                ct.ThrowIfCancellationRequested();
                int read = from.Read(buffer, 0, bufferSize);
                if (read == 0) {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                to.Write(buffer, 0, read);
                progress?.Report(read);
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>The SHA-256 of the rest of <paramref name="from"/> - a copy read back.</summary>
    public static byte[] Hash(Stream from, CancellationToken ct, int bufferSize = BufferSize) {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try {
            while (true) {
                ct.ThrowIfCancellationRequested();
                int read = from.Read(buffer, 0, bufferSize);
                if (read == 0) {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hash.GetHashAndReset();
    }
}
