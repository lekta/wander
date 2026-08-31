using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Wander.Core.Preview;

/// <summary>
/// glTF 2.0, in both its packagings: <c>.glb</c>, where the JSON and the
/// binary blob are chunks of one file, and <c>.gltf</c>, where the JSON is
/// the file and the blob is either a <c>data:</c> URI inside it or a
/// <c>.bin</c> sitting next to it.
///
/// <para>
/// Only positions and indices are read. A preview draws an untextured
/// solid, so materials, textures, skins and animations are skipped — and
/// skipping them is most of why this reader is a few hundred lines rather
/// than a library.
/// </para>
///
/// <para>
/// The scene graph, on the other hand, is <em>not</em> skipped, and that is
/// the part worth the code. glTF stores a mesh in its own local space and
/// places it with a node transform; concatenating primitives without
/// applying those transforms gives a pile of parts stacked on the origin
/// rather than a model. So the nodes are walked from the scene roots with
/// the matrices multiplied down, and every position is transformed on the
/// way out.
/// </para>
/// </summary>
internal static class GltfReader {
    private const uint GlbMagic = 0x46546C67;         // "glTF"
    private const uint ChunkJson = 0x4E4F534A;        // "JSON"
    private const uint ChunkBin = 0x004E4942;         // "BIN\0"

    private const int ComponentByte = 5120;
    private const int ComponentUByte = 5121;
    private const int ComponentShort = 5122;
    private const int ComponentUShort = 5123;
    private const int ComponentUInt = 5125;
    private const int ComponentFloat = 5126;


    public static MeshData? Read(string path) {
        byte[] bytes = File.ReadAllBytes(path);

        return Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase)
            ? ReadGlb(bytes)
            : ReadJson(Encoding.UTF8.GetString(StripBom(bytes)), null, Path.GetDirectoryName(path));
    }


    /// <summary>
    /// A GLB is a twelve-byte header followed by length-prefixed chunks.
    /// Only the first JSON chunk and the first BIN chunk matter; anything
    /// else an exporter appended is stepped over by its own length.
    /// </summary>
    private static MeshData? ReadGlb(byte[] bytes) {
        if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != GlbMagic) {
            return null;
        }

        string? json = null;
        byte[]? bin = null;

        int p = 12;
        while (p + 8 <= bytes.Length) {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p + 4));
            p += 8;

            if (length > bytes.Length - p) {
                break;
            }

            if (type == ChunkJson && json is null) {
                json = Encoding.UTF8.GetString(bytes, p, (int)length);
            } else if (type == ChunkBin && bin is null) {
                bin = bytes[p..(p + (int)length)];
            }

            p += (int)length;
            p = (p + 3) & ~3;                          // chunks are four-byte aligned
        }

        return json is null ? null : ReadJson(json, bin, null);
    }


    private static MeshData? ReadJson(string json, byte[]? glbBuffer, string? baseDirectory) {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessors = ArrayOf(root, "accessors");
        var views = ArrayOf(root, "bufferViews");
        var buffers = LoadBuffers(root, glbBuffer, baseDirectory);
        var meshes = ArrayOf(root, "meshes");
        if (accessors is null || views is null || meshes is null) {
            return null;
        }

        var positions = new List<float>();
        var parts = new List<MeshPart>();
        var materials = ArrayOf(root, "materials");
        int total = 0;

        foreach (var (meshIndex, transform) in Placements(root)) {
            if (meshIndex < 0 || meshIndex >= meshes.Value.GetArrayLength()) {
                continue;
            }

            var primitives = ArrayOf(meshes.Value[meshIndex], "primitives");
            if (primitives is null) {
                continue;
            }

            foreach (var primitive in primitives.Value.EnumerateArray()) {
                // Mode 4 is TRIANGLES, and it is the default when absent.
                // Strips and fans exist but are rare out of an exporter;
                // drawing one as loose triangles would be visibly wrong, so
                // it is skipped instead.
                if (primitive.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4) {
                    continue;
                }
                if (!primitive.TryGetProperty("attributes", out var attributes)
                    || !attributes.TryGetProperty("POSITION", out var positionRef)) {
                    continue;
                }

                float[]? points = ReadFloats(accessors.Value, views.Value, buffers, positionRef.GetInt32());
                if (points is null || points.Length < 9) {
                    continue;
                }

                int baseVertex = positions.Count / 3;
                for (int i = 0; i + 2 < points.Length; i += 3) {
                    var (x, y, z) = transform.Apply(points[i], points[i + 1], points[i + 2]);
                    positions.Add(x);
                    positions.Add(y);
                    positions.Add(z);
                }

                var indices = new List<int>();
                int vertexCount = points.Length / 3;
                if (primitive.TryGetProperty("indices", out var indexRef)) {
                    int[]? read = ReadIndices(accessors.Value, views.Value, buffers, indexRef.GetInt32());
                    if (read is null) {
                        continue;
                    }
                    foreach (int index in read) {
                        if (index >= 0 && index < vertexCount) {
                            indices.Add(baseVertex + index);
                        }
                    }
                } else {
                    // No index buffer means the positions are already in
                    // drawing order.
                    for (int i = 0; i < vertexCount; i++) {
                        indices.Add(baseVertex + i);
                    }
                }

                int whole = indices.Count / 3 * 3;
                if (whole >= 3) {
                    parts.Add(new MeshPart(
                        indices.GetRange(0, whole).ToArray(),
                        BaseColor(materials, primitive)));
                }

                if (total + whole > MeshFile.MaxTriangles * 3) {
                    break;
                }
                total += whole;
            }
        }

        return parts.Count == 0 ? null : new MeshData(positions.ToArray(), parts);
    }


    /// <summary>
    /// The primitive's flat colour: <c>baseColorFactor</c> from its
    /// material, which is the one part of a glTF material a preview without
    /// textures can honour. Null when the primitive names no material or
    /// the material states no factor — glTF's own default there is white,
    /// and white is not what an untextured preview wants to draw.
    /// </summary>
    private static MeshColor? BaseColor(JsonElement? materials, JsonElement primitive) {
        if (materials is null || !primitive.TryGetProperty("material", out var reference)) {
            return null;
        }

        int index = reference.GetInt32();
        if (index < 0 || index >= materials.Value.GetArrayLength()) {
            return null;
        }

        var material = materials.Value[index];
        if (!material.TryGetProperty("pbrMetallicRoughness", out var pbr)
            || ArrayOf(pbr, "baseColorFactor") is not { } factor
            || factor.GetArrayLength() < 3) {
            return null;
        }

        return MeshColor.Clamped(factor[0].GetDouble(), factor[1].GetDouble(), factor[2].GetDouble());
    }


    // --- scene graph ----------------------------------------------------

    /// <summary>
    /// Every mesh the scene places, paired with the world transform it is
    /// placed by. Walks from the scene's roots; a file with no scene at all
    /// falls back to drawing its meshes untransformed, which is better than
    /// drawing nothing.
    /// </summary>
    private static List<(int Mesh, Matrix Transform)> Placements(JsonElement root) {
        var placements = new List<(int, Matrix)>();
        var nodes = ArrayOf(root, "nodes");

        if (nodes is null) {
            var meshes = ArrayOf(root, "meshes");
            for (int i = 0; meshes is not null && i < meshes.Value.GetArrayLength(); i++) {
                placements.Add((i, Matrix.Identity));
            }

            return placements;
        }

        var roots = SceneRoots(root, nodes.Value);
        var visited = new HashSet<int>();

        foreach (int index in roots) {
            Walk(nodes.Value, index, Matrix.Identity, placements, visited);
        }

        return placements;
    }

    private static IEnumerable<int> SceneRoots(JsonElement root, JsonElement nodes) {
        var scenes = ArrayOf(root, "scenes");
        int active = root.TryGetProperty("scene", out var scene) ? scene.GetInt32() : 0;

        if (scenes is not null && active >= 0 && active < scenes.Value.GetArrayLength()
            && ArrayOf(scenes.Value[active], "nodes") is { } list) {
            foreach (var node in list.EnumerateArray()) {
                yield return node.GetInt32();
            }

            yield break;
        }

        for (int i = 0; i < nodes.GetArrayLength(); i++) {
            yield return i;
        }
    }

    /// <summary>
    /// <paramref name="visited"/> is not an optimisation: glTF nodes are
    /// indices, and a malformed file can point a child back at its parent.
    /// Without it that file is an infinite recursion in a preview pane.
    /// </summary>
    private static void Walk(
        JsonElement nodes, int index, Matrix parent,
        List<(int, Matrix)> placements, HashSet<int> visited) {
        if (index < 0 || index >= nodes.GetArrayLength() || !visited.Add(index)) {
            return;
        }

        var node = nodes[index];
        Matrix world = parent.Multiply(LocalTransform(node));

        if (node.TryGetProperty("mesh", out var mesh)) {
            placements.Add((mesh.GetInt32(), world));
        }
        if (ArrayOf(node, "children") is { } children) {
            foreach (var child in children.EnumerateArray()) {
                Walk(nodes, child.GetInt32(), world, placements, visited);
            }
        }
    }

    /// <summary>
    /// A node states either a full matrix or a translation / rotation /
    /// scale triple — never both, per the specification.
    /// </summary>
    private static Matrix LocalTransform(JsonElement node) {
        if (ArrayOf(node, "matrix") is { } m && m.GetArrayLength() == 16) {
            var values = new float[16];
            int i = 0;
            foreach (var v in m.EnumerateArray()) {
                values[i++] = (float)v.GetDouble();
            }

            return Matrix.FromColumnMajor(values);
        }

        var result = Matrix.Identity;

        if (ArrayOf(node, "scale") is { } s && s.GetArrayLength() == 3) {
            result = result.Multiply(Matrix.Scale(
                (float)s[0].GetDouble(), (float)s[1].GetDouble(), (float)s[2].GetDouble()));
        }
        if (ArrayOf(node, "rotation") is { } r && r.GetArrayLength() == 4) {
            result = Matrix.Rotation(
                (float)r[0].GetDouble(), (float)r[1].GetDouble(),
                (float)r[2].GetDouble(), (float)r[3].GetDouble()).Multiply(result);
        }
        if (ArrayOf(node, "translation") is { } t && t.GetArrayLength() == 3) {
            result = Matrix.Translation(
                (float)t[0].GetDouble(), (float)t[1].GetDouble(), (float)t[2].GetDouble()).Multiply(result);
        }

        return result;
    }


    // --- buffers and accessors ------------------------------------------

    /// <summary>
    /// The blobs the accessors read from. A buffer with no URI is the GLB's
    /// own BIN chunk; a <c>data:</c> URI carries its bytes inline; anything
    /// else is a file beside the <c>.gltf</c>, and only a plain relative
    /// name is followed — a URI that climbs out of the folder or names a
    /// host is a file we were not asked to open.
    /// </summary>
    private static List<byte[]?> LoadBuffers(JsonElement root, byte[]? glbBuffer, string? baseDirectory) {
        var loaded = new List<byte[]?>();
        var buffers = ArrayOf(root, "buffers");
        if (buffers is null) {
            return loaded;
        }

        foreach (var buffer in buffers.Value.EnumerateArray()) {
            if (!buffer.TryGetProperty("uri", out var uriElement)) {
                loaded.Add(glbBuffer);

                continue;
            }

            string uri = uriElement.GetString() ?? "";
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) {
                int comma = uri.IndexOf(',');
                loaded.Add(comma > 0 && TryBase64(uri[(comma + 1)..], out byte[] data) ? data : null);

                continue;
            }

            loaded.Add(SideFile(uri, baseDirectory));
        }

        return loaded;
    }

    private static byte[]? SideFile(string uri, string? baseDirectory) {
        if (baseDirectory is null || uri.Length == 0 || uri.Contains("..") || uri.Contains("://")) {
            return null;
        }

        try {
            string name = Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(baseDirectory, name));
            if (!full.StartsWith(Path.GetFullPath(baseDirectory), StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        } catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) {
            return null;
        }
    }

    private static bool TryBase64(string text, out byte[] data) {
        try {
            data = Convert.FromBase64String(text);

            return true;
        } catch (FormatException) {
            data = Array.Empty<byte>();

            return false;
        }
    }


    /// <summary>
    /// Positions: always three floats each, per the specification, so the
    /// only thing to get right is the stride — an accessor may walk a
    /// buffer view in which each element is followed by other attributes.
    /// </summary>
    private static float[]? ReadFloats(
        JsonElement accessors, JsonElement views, List<byte[]?> buffers, int index) {
        if (!TryAccessor(accessors, views, buffers, index, out var a) || a.Component != ComponentFloat) {
            return null;
        }

        int stride = a.Stride > 0 ? a.Stride : 12;
        var result = new float[a.Count * 3];

        for (int i = 0; i < a.Count; i++) {
            int at = a.Offset + (i * stride);
            if (at + 12 > a.Buffer.Length) {
                return null;
            }

            result[i * 3] = BinaryPrimitives.ReadSingleLittleEndian(a.Buffer.AsSpan(at));
            result[(i * 3) + 1] = BinaryPrimitives.ReadSingleLittleEndian(a.Buffer.AsSpan(at + 4));
            result[(i * 3) + 2] = BinaryPrimitives.ReadSingleLittleEndian(a.Buffer.AsSpan(at + 8));
        }

        return result;
    }

    /// <summary>
    /// Indices come as one of three integer widths, chosen by how many
    /// vertices the primitive has. Reading the wrong width does not fail —
    /// it silently draws a different model, which is why the widths are
    /// spelled out rather than assumed.
    /// </summary>
    private static int[]? ReadIndices(
        JsonElement accessors, JsonElement views, List<byte[]?> buffers, int index) {
        if (!TryAccessor(accessors, views, buffers, index, out var a)) {
            return null;
        }

        int size = a.Component switch {
            ComponentByte or ComponentUByte => 1,
            ComponentShort or ComponentUShort => 2,
            ComponentUInt => 4,
            _ => 0,
        };
        if (size == 0) {
            return null;
        }

        int stride = a.Stride > 0 ? a.Stride : size;
        var result = new int[a.Count];

        for (int i = 0; i < a.Count; i++) {
            int at = a.Offset + (i * stride);
            if (at + size > a.Buffer.Length) {
                return null;
            }

            result[i] = size switch {
                1 => a.Buffer[at],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(a.Buffer.AsSpan(at)),
                _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(a.Buffer.AsSpan(at)),
            };
        }

        return result;
    }

    private record struct Accessor(byte[] Buffer, int Offset, int Count, int Component, int Stride);

    private static bool TryAccessor(
        JsonElement accessors, JsonElement views, List<byte[]?> buffers, int index, out Accessor accessor) {
        accessor = default;

        if (index < 0 || index >= accessors.GetArrayLength()) {
            return false;
        }

        var a = accessors[index];
        if (!a.TryGetProperty("bufferView", out var viewRef) || !a.TryGetProperty("count", out var countRef)) {
            return false;
        }

        int viewIndex = viewRef.GetInt32();
        if (viewIndex < 0 || viewIndex >= views.GetArrayLength()) {
            return false;
        }

        var view = views[viewIndex];
        int bufferIndex = view.TryGetProperty("buffer", out var b) ? b.GetInt32() : -1;
        if (bufferIndex < 0 || bufferIndex >= buffers.Count || buffers[bufferIndex] is not { } bytes) {
            return false;
        }

        // Two offsets, and both are real: the view's position in the buffer
        // and the accessor's position inside the view.
        int offset = (view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0)
            + (a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);

        int count = countRef.GetInt32();
        if (offset < 0 || count < 0 || count > MeshFile.MaxTriangles * 3) {
            return false;
        }

        accessor = new Accessor(
            bytes,
            offset,
            count,
            a.TryGetProperty("componentType", out var ct) ? ct.GetInt32() : 0,
            view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : 0);

        return true;
    }


    private static JsonElement? ArrayOf(JsonElement parent, string name) {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array
                ? value
                : null;
    }

    private static byte[] StripBom(byte[] bytes) {
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;
    }


    /// <summary>
    /// A 4×4 affine transform, kept here rather than taken from
    /// <c>System.Numerics</c> because only the three operations glTF needs
    /// are wanted and the storage order is the one glTF states
    /// (column-major), so there is no conversion to get backwards.
    /// </summary>
    private readonly struct Matrix {
        // Row-major internally: m[row, column].
        private readonly float[] _m;


        private Matrix(float[] m) {
            _m = m;
        }


        public static Matrix Identity => new(new float[] {
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
        });

        public static Matrix FromColumnMajor(float[] c) {
            var m = new float[16];
            for (int row = 0; row < 4; row++) {
                for (int col = 0; col < 4; col++) {
                    m[(row * 4) + col] = c[(col * 4) + row];
                }
            }

            return new Matrix(m);
        }

        public static Matrix Translation(float x, float y, float z) {
            var m = (float[])Identity._m.Clone();
            m[3] = x;
            m[7] = y;
            m[11] = z;

            return new Matrix(m);
        }

        public static Matrix Scale(float x, float y, float z) {
            var m = (float[])Identity._m.Clone();
            m[0] = x;
            m[5] = y;
            m[10] = z;

            return new Matrix(m);
        }

        /// <summary>Quaternion, in glTF's own order: x, y, z, w.</summary>
        public static Matrix Rotation(float x, float y, float z, float w) {
            float n = MathF.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
            if (n <= 0) {
                return Identity;
            }

            x /= n; y /= n; z /= n; w /= n;

            var m = (float[])Identity._m.Clone();
            m[0] = 1 - (2 * ((y * y) + (z * z)));
            m[1] = 2 * ((x * y) - (z * w));
            m[2] = 2 * ((x * z) + (y * w));
            m[4] = 2 * ((x * y) + (z * w));
            m[5] = 1 - (2 * ((x * x) + (z * z)));
            m[6] = 2 * ((y * z) - (x * w));
            m[8] = 2 * ((x * z) - (y * w));
            m[9] = 2 * ((y * z) + (x * w));
            m[10] = 1 - (2 * ((x * x) + (y * y)));

            return new Matrix(m);
        }

        public Matrix Multiply(Matrix other) {
            var m = new float[16];
            for (int row = 0; row < 4; row++) {
                for (int col = 0; col < 4; col++) {
                    float sum = 0;
                    for (int k = 0; k < 4; k++) {
                        sum += _m[(row * 4) + k] * other._m[(k * 4) + col];
                    }
                    m[(row * 4) + col] = sum;
                }
            }

            return new Matrix(m);
        }

        public (float X, float Y, float Z) Apply(float x, float y, float z) {
            return (
                (_m[0] * x) + (_m[1] * y) + (_m[2] * z) + _m[3],
                (_m[4] * x) + (_m[5] * y) + (_m[6] * z) + _m[7],
                (_m[8] * x) + (_m[9] * y) + (_m[10] * z) + _m[11]);
        }
    }
}
