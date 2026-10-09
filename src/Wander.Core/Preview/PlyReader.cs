using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Wander.Core.Preview;

/// <summary>
/// PLY - the Stanford polygon format scanners and photogrammetry write: a
/// text header that declares elements and their properties, then the data
/// as text or as little- or big-endian binary. The vertices' <c>x</c>,
/// <c>y</c>, <c>z</c> and the faces' index lists are read, polygons fanned
/// into triangles; any other element (edges, materials) is stepped over by
/// the sizes its header declares.
///
/// <para>
/// A file of vertices without faces - a point cloud, a Gaussian splat - has
/// nothing to draw as a surface and reads as no mesh.
/// </para>
/// </summary>
internal static class PlyReader {
    public static MeshData? Read(byte[] bytes) {
        if (Header(bytes) is not { } header) {
            return null;
        }

        var (format, elements, dataStart) = header;
        var positions = new List<float>();
        var indices = new List<int>();
        int vertexCount = 0;
        var cursor = new Cursor(bytes, dataStart, format);
        foreach (var element in elements) {
            bool isVertex = element.Name == "vertex";
            bool isFace = element.Name == "face";
            int x = element.Properties.FindIndex(p => p.Name == "x");
            int y = element.Properties.FindIndex(p => p.Name == "y");
            int z = element.Properties.FindIndex(p => p.Name == "z");
            int list = element.Properties.FindIndex(p => p.Name is "vertex_indices" or "vertex_index" && p.ListCount is not null);
            if (isVertex && (x < 0 || y < 0 || z < 0)) {
                return null;
            }

            var values = new double[element.Properties.Count];
            var polygon = new List<int>();
            for (long i = 0; i < element.Count; i++) {
                for (int p = 0; p < element.Properties.Count; p++) {
                    var property = element.Properties[p];
                    if (property.ListCount is { } countType) {
                        int n = (int)cursor.Number(countType);
                        if (n < 0 || n > 1_000_000) {
                            return null;
                        }
                        polygon.Clear();
                        for (int k = 0; k < n; k++) {
                            polygon.Add((int)cursor.Number(property.Type));
                        }
                        if (isFace && p == list) {
                            for (int k = 2; k < n; k++) {
                                indices.Add(polygon[0]);
                                indices.Add(polygon[k - 1]);
                                indices.Add(polygon[k]);
                            }
                            if (indices.Count / 3 > MeshFile.MaxTriangles) {
                                return null;
                            }
                        }
                    } else {
                        values[p] = cursor.Number(property.Type);
                    }
                }
                if (isVertex) {
                    positions.Add((float)values[x]);
                    positions.Add((float)values[y]);
                    positions.Add((float)values[z]);
                }
            }
            if (isVertex) {
                vertexCount = (int)element.Count;
            }
        }

        // A face pointing past the vertices is a damaged file, not a mesh.
        foreach (int index in indices) {
            if (index < 0 || index >= vertexCount) {
                return null;
            }
        }

        return MeshFile.Single(positions.ToArray(), indices.ToArray());
    }


    private static (Format Format, List<Element> Elements, int DataStart)? Header(byte[] bytes) {
        const string End = "end_header";
        int limit = Math.Min(bytes.Length, 64 * 1024);
        string head = Encoding.ASCII.GetString(bytes, 0, limit);
        if (!head.StartsWith("ply", StringComparison.Ordinal)) {
            return null;
        }

        int end = head.IndexOf(End, StringComparison.Ordinal);
        if (end < 0) {
            return null;
        }
        int dataStart = head.IndexOf('\n', end);
        if (dataStart < 0) {
            return null;
        }
        dataStart++;

        Format? format = null;
        var elements = new List<Element>();
        foreach (string raw in head[..end].Split('\n')) {
            string[] words = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) {
                continue;
            }

            switch (words[0]) {
                case "format" when words.Length >= 2:
                    format = words[1] switch {
                        "ascii" => Format.Ascii,
                        "binary_little_endian" => Format.LittleEndian,
                        "binary_big_endian" => Format.BigEndian,
                        _ => null,
                    };
                    break;

                case "element" when words.Length >= 3 && long.TryParse(words[2], out long count) && count >= 0:
                    elements.Add(new Element(words[1], count, new List<Property>()));
                    break;

                case "property" when elements.Count > 0:
                    if (words.Length >= 5 && words[1] == "list") {
                        elements[^1].Properties.Add(new Property(words[4], words[3], words[2]));
                    } else if (words.Length >= 3) {
                        elements[^1].Properties.Add(new Property(words[2], words[1], null));
                    }
                    break;
            }
        }

        return format is { } f ? (f, elements, dataStart) : null;
    }


    private sealed record Element(string Name, long Count, List<Property> Properties);


    /// <param name="ListCount">The type of a list's length; null for a single value.</param>
    private sealed record Property(string Name, string Type, string? ListCount);


    /// <summary>Reads values one after another, whichever of the three encodings the data is in.</summary>
    private sealed class Cursor {
        private readonly byte[] _bytes;
        private readonly Format _format;
        private int _at;


        public Cursor(byte[] bytes, int start, Format format) {
            _bytes = bytes;
            _at = start;
            _format = format;
        }


        public double Number(string type) {
            if (_format == Format.Ascii) {
                return Word();
            }

            if (_at >= _bytes.Length) {
                throw new FormatException("PLY data ends early");
            }

            bool little = _format == Format.LittleEndian;
            var span = _bytes.AsSpan(_at);
            (double value, int size) = type switch {
                "char" or "int8" => ((sbyte)span[0], 1),
                "uchar" or "uint8" => (span[0], 1),
                "short" or "int16" => (little ? BinaryPrimitives.ReadInt16LittleEndian(span) : BinaryPrimitives.ReadInt16BigEndian(span), 2),
                "ushort" or "uint16" => (little ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt16BigEndian(span), 2),
                "int" or "int32" => (little ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span), 4),
                "uint" or "uint32" => (little ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span), 4),
                "float" or "float32" => (little ? BinaryPrimitives.ReadSingleLittleEndian(span) : BinaryPrimitives.ReadSingleBigEndian(span), 4),
                "double" or "float64" => (little ? BinaryPrimitives.ReadDoubleLittleEndian(span) : BinaryPrimitives.ReadDoubleBigEndian(span), 8),
                _ => throw new FormatException($"PLY type '{type}'"),
            };
            _at += size;

            return value;
        }


        private double Word() {
            while (_at < _bytes.Length && _bytes[_at] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') {
                _at++;
            }
            int start = _at;
            while (_at < _bytes.Length && _bytes[_at] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) {
                _at++;
            }
            if (_at == start) {
                throw new FormatException("PLY data ends early");
            }

            return double.Parse(_bytes.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }


    private enum Format {
        Ascii,
        LittleEndian,
        BigEndian,
    }
}
