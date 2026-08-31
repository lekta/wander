using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Wander.Core.Preview;

/// <summary>
/// STL, in both the shapes the format exists in.
///
/// <para>
/// The format stores loose triangles — three points each, repeated for
/// every face, with no index list and no shared vertices. So does what
/// comes out of here: welding them into an indexed mesh would be a
/// tolerance decision (how close is the same point?) that a preview has no
/// business making, and WPF is perfectly happy with a vertex per corner.
/// </para>
///
/// <para>
/// Telling the two flavours apart is the one place STL bites. The ASCII
/// form starts with the word <c>solid</c> — and so do plenty of binary
/// files, because exporters write their name into the 80-byte header. The
/// reliable test is arithmetic: a binary file's size is exactly
/// <c>84 + 50 × triangles</c>, and that is what is checked here before the
/// word is trusted.
/// </para>
/// </summary>
internal static class StlReader {
    private const int HeaderBytes = 80;
    private const int TriangleBytes = 50;


    public static MeshData? Read(byte[] bytes) {
        return LooksBinary(bytes) ? ReadBinary(bytes) : ReadAscii(bytes);
    }


    private static bool LooksBinary(byte[] bytes) {
        if (bytes.Length < HeaderBytes + 4) {
            return false;
        }

        uint triangles = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(HeaderBytes));

        // A trailing byte or two is common enough to allow for; a size that
        // does not fit the count at all means the file is text.
        long expected = HeaderBytes + 4L + (long)triangles * TriangleBytes;

        return triangles > 0
            && triangles <= MeshFile.MaxTriangles
            && bytes.Length >= expected
            && bytes.Length <= expected + 2;
    }


    private static MeshData? ReadBinary(byte[] bytes) {
        int triangles = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(HeaderBytes));

        var positions = new float[triangles * 9];
        var indices = new int[triangles * 3];

        int p = HeaderBytes + 4;
        for (int t = 0; t < triangles; t++) {
            p += 12;                                     // the face normal, which WPF recomputes anyway

            for (int corner = 0; corner < 3; corner++) {
                int at = (t * 9) + (corner * 3);
                positions[at] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(p));
                positions[at + 1] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(p + 4));
                positions[at + 2] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(p + 8));
                p += 12;
                indices[(t * 3) + corner] = (t * 3) + corner;
            }

            p += 2;                                      // attribute byte count
        }

        return MeshFile.Single(positions, indices);
    }


    private static MeshData? ReadAscii(byte[] bytes) {
        // Latin-1 rather than UTF-8: the numbers are ASCII either way, and
        // a stray high byte in a solid name must not abort the parse.
        string text = Encoding.Latin1.GetString(bytes);

        var positions = new List<float>();
        var indices = new List<int>();

        foreach (var line in text.AsSpan().EnumerateLines()) {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!TryVertex(trimmed[6..], out float x, out float y, out float z)) {
                continue;
            }

            indices.Add(positions.Count / 3);
            positions.Add(x);
            positions.Add(y);
            positions.Add(z);

            if (indices.Count > MeshFile.MaxTriangles * 3) {
                break;
            }
        }

        // Corners come in threes; a file truncated mid-face would otherwise
        // hand the renderer a partial triangle.
        int whole = indices.Count / 3 * 3;

        return whole < 3
            ? null
            : MeshFile.Single(positions.GetRange(0, whole * 3).ToArray(), indices.GetRange(0, whole).ToArray());
    }


    private static bool TryVertex(ReadOnlySpan<char> rest, out float x, out float y, out float z) {
        x = y = z = 0;

        return TryNext(ref rest, out x) && TryNext(ref rest, out y) && TryNext(ref rest, out z);
    }

    /// <summary>
    /// Next whitespace-separated number, invariant culture. Invariant is
    /// the whole point: on a machine where the decimal separator is a
    /// comma, parsing "0.5" with the current culture reads 5.
    /// </summary>
    private static bool TryNext(ref ReadOnlySpan<char> rest, out float value) {
        rest = rest.TrimStart();
        int end = rest.IndexOfAny(' ', '\t');
        var token = end < 0 ? rest : rest[..end];
        rest = end < 0 ? ReadOnlySpan<char>.Empty : rest[end..];

        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
