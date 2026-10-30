using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ZstdSharp;

namespace Wander.Core.Preview;

/// <summary>
/// A Blender 4.x file as a mesh. A <c>.blend</c> is a memory dump: blocks that
/// carry the address they had, and in the block <c>DNA1</c> the layout of
/// every struct - so fields are found by name, never by a fixed offset, and
/// a pointer is followed through the block that had that address.
///
/// <para>
/// Every mesh object the viewport shows - by its view layers' bases, or by the
/// object's own flag in a file without any: <c>position</c>,
/// <c>.corner_vert</c>, <c>poly_offset_indices</c>, <c>material_index</c> and
/// the active UV layer, placed by its object's location, rotation and scale and
/// through its parents, Z-up turned to Y-up. Colour is the material's viewport
/// colour, picture - its first image node that is not another kind of map,
/// packed or by file (<see cref="MeshFile.FindTexture"/>), else what is
/// around the model (<see cref="ModelTextures"/>). A file saved with
/// "Compress" is zstd frames around the same bytes.
/// </para>
///
/// <para>
/// Not read: modifiers (a mirrored half stays a half, a subdivided cage a cage),
/// armature poses, delta rotations. Any version but 4.x, whose meshes are
/// stored differently, is no preview.
/// </para>
/// </summary>
internal static class BlendReader {
    private const int ObjectMesh = 1;
    private const int HiddenInViewport = 1;
    private const int BaseEnabledInViewport = 1 << 6;
    private const int BaseHidden = 1 << 8;
    private const int ShaderImageNode = 143;
    private const int LayerFloat2 = 49;

    /// <summary>What a compressed file may unpack to: past this it is not a preview, or not a file of ours.</summary>
    private const long MaxUnpackedSize = 1024L * 1024 * 1024;

    private const int ListLimit = 1_000_000;


    public static MeshData? Read(byte[] bytes, ModelTextures textures) {
        if (bytes.Length > 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0xFD2FB528) {
            bytes = Unpacked(bytes);
        }

        // "BLENDER", pointer size '-' (8 bytes), endianness 'v' (little), version.
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 9) != "BLENDER-v" || bytes[9] != (byte)'4') {
            return null;
        }

        var file = new BlendFile(bytes);
        var shown = ShownObjects(file);
        var objects = new Dictionary<long, Matrix4x4>();
        var builder = new Builder();
        var axis = Matrix4x4.CreateRotationX(-MathF.PI / 2);
        var materials = new Dictionary<long, Look>();
        foreach (var block in file.Blocks.Where(b => b.Code == "OB\0\0")) {
            bool visible = shown?.Contains(block.Address)
                ?? (file.Short(block, "Object", "restrictflag") & HiddenInViewport) == 0;
            if (!visible
                || file.Short(block, "Object", "type") != ObjectMesh
                || file.Target(file.Pointer(block, "Object", "data")) is not { } mesh
                || mesh.Code != "ME\0\0") {
                continue;
            }

            var looks = Looks(file, block, mesh, materials, textures);
            builder.Add(file, mesh, World(file, block, objects, 0) * axis, looks);
        }

        return builder.ToMesh();
    }


    /// <summary>The file as Blender wrote it, out of its zstd frames. A broken stream reads as a malformed file.</summary>
    private static byte[] Unpacked(byte[] packed) {
        try {
            using var source = new DecompressionStream(new MemoryStream(packed));
            using var unpacked = new MemoryStream(packed.Length * 4);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = source.Read(buffer)) > 0) {
                if (unpacked.Length + read > MaxUnpackedSize) {
                    throw new FormatException("Blender file unpacks past the limit");
                }
                unpacked.Write(buffer, 0, read);
            }

            return unpacked.ToArray();
        } catch (ZstdException ex) {
            throw new FormatException("Blender file is not valid zstd", ex);
        }
    }


    /// <summary>
    /// The objects some view layer shows: enabled for the viewport (not in an
    /// excluded collection, not disabled) and not hidden with the eye. Null when
    /// the file has no bases at all - nothing here says what is shown.
    /// </summary>
    private static HashSet<long>? ShownObjects(BlendFile file) {
        HashSet<long>? shown = null;
        foreach (var scene in file.Blocks.Where(b => b.Code == "SC\0\0")) {
            var layer = file.Target(file.Pointer(scene, "Scene", "view_layers"));
            for (int i = 0; layer is not null && i < ListLimit; i++) {
                var entry = file.Target(file.Pointer(layer, "ViewLayer", "object_bases"));
                for (int k = 0; entry is not null && k < ListLimit; k++) {
                    shown ??= new HashSet<long>();
                    int flag = file.Short(entry, "Base", "flag");
                    if ((flag & BaseEnabledInViewport) != 0 && (flag & BaseHidden) == 0) {
                        shown.Add(file.Pointer(entry, "Base", "object"));
                    }
                    entry = file.Target(file.Pointer(entry, "Base", "next"));
                }
                layer = file.Target(file.Pointer(layer, "ViewLayer", "next"));
            }
        }

        return shown;
    }


    // --- Where an object stands ---

    /// <summary>Its own transform, then the inverse stored at parenting, then the parent's - row vectors, so local first.</summary>
    private static Matrix4x4 World(BlendFile file, Block block, Dictionary<long, Matrix4x4> worlds, int depth) {
        if (worlds.TryGetValue(block.Address, out var known)) {
            return known;
        }

        var world = Local(file, block);
        if (depth < 64 && file.Target(file.Pointer(block, "Object", "parent")) is { } parent) {
            world = world * Matrix(file.Floats(block, "Object", "parentinv", 16)) * World(file, parent, worlds, depth + 1);
        }
        worlds[block.Address] = world;

        return world;
    }


    private static Matrix4x4 Local(BlendFile file, Block block) {
        float[] loc = file.Floats(block, "Object", "loc", 3), moved = file.Floats(block, "Object", "dloc", 3);
        float[] scale = file.Floats(block, "Object", "size", 3), scaled = file.Floats(block, "Object", "dscale", 3);

        return Matrix4x4.CreateScale(scale[0] * scaled[0], scale[1] * scaled[1], scale[2] * scaled[2])
            * Rotation(file, block)
            * Matrix4x4.CreateTranslation(loc[0] + moved[0], loc[1] + moved[1], loc[2] + moved[2]);
    }


    /// <summary>By <c>rotmode</c>: 0 a quaternion (w first), 1-6 Euler XYZ, XZY, YXZ, YZX, ZXY, ZYX, -1 an axis and angle.</summary>
    private static Matrix4x4 Rotation(BlendFile file, Block block) {
        int mode = file.Short(block, "Object", "rotmode");
        if (mode == 0) {
            float[] q = file.Floats(block, "Object", "quat", 4);
            var quaternion = new Quaternion(q[1], q[2], q[3], q[0]);

            return quaternion.LengthSquared() > 0 ? Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(quaternion)) : Matrix4x4.Identity;
        }
        if (mode < 0) {
            float[] axis = file.Floats(block, "Object", "rotAxis", 3);
            var direction = new Vector3(axis[0], axis[1], axis[2]);

            return direction.LengthSquared() > 0
                ? Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(direction), file.Floats(block, "Object", "rotAngle", 1)[0])
                : Matrix4x4.Identity;
        }

        float[] r = file.Floats(block, "Object", "rot", 3);
        Matrix4x4 x = Matrix4x4.CreateRotationX(r[0]), y = Matrix4x4.CreateRotationY(r[1]), z = Matrix4x4.CreateRotationZ(r[2]);

        return mode switch {
            2 => x * z * y,
            3 => y * x * z,
            4 => y * z * x,
            5 => z * x * y,
            6 => z * y * x,
            _ => x * y * z,
        };
    }


    /// <summary>Sixteen floats as a matrix: Blender stores columns of column vectors, the same order as rows of row vectors.</summary>
    private static Matrix4x4 Matrix(float[] m) {
        return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
    }


    // --- Materials ---

    /// <summary>The object's slots: each from the object where <c>matbits</c> says so, else from the mesh.</summary>
    private static List<Look> Looks(BlendFile file, Block ob, Block mesh, Dictionary<long, Look> known, ModelTextures textures) {
        var looks = new List<Look>();
        int count = file.Short(mesh, "Mesh", "totcol");
        var bits = file.Target(file.Pointer(ob, "Object", "matbits"));
        var own = file.Target(file.Pointer(ob, "Object", "mat"));
        var shared = file.Target(file.Pointer(mesh, "Mesh", "mat"));
        for (int i = 0; i < count; i++) {
            bool onObject = bits is not null && own is not null && i < bits.Length && file.Bytes[bits.Start + i] != 0;
            var list = onObject ? own : shared;
            long address = list is not null && (i + 1) * 8 <= list.Length
                ? BinaryPrimitives.ReadInt64LittleEndian(file.Bytes.AsSpan(list.Start + (i * 8)))
                : 0;
            if (!known.TryGetValue(address, out var look)) {
                look = file.Target(address) is { Code: "MA\0\0" } material ? ReadLook(file, material, textures) : default;
                known[address] = look;
            }
            looks.Add(look);
        }

        return looks;
    }


    private static Look ReadLook(BlendFile file, Block material, ModelTextures textures) {
        float[] c = new[] { file.Float(material, "Material", "r"), file.Float(material, "Material", "g"), file.Float(material, "Material", "b") };
        // The block's name starts with its kind: "MA".
        string name = file.Text(material, "Material", "id", "ID", "name");
        name = name.Length > 2 ? name[2..] : name;

        return new Look(
            MeshColor.Clamped(c[0], c[1], c[2]),
            FromNodes(file, material, textures) ?? textures.ForMaterial(name));
    }


    /// <summary>The first image node of the material's node tree that holds a colour map, by its image.</summary>
    private static MeshTexture? FromNodes(BlendFile file, Block material, ModelTextures textures) {
        if (file.Target(file.Pointer(material, "Material", "nodetree")) is not { } tree) {
            return null;
        }

        var node = file.Target(file.Pointer(tree, "bNodeTree", "nodes"));
        for (int i = 0; node is not null && i < 4096; i++) {
            if (file.Short(node, "bNode", "type") == ShaderImageNode
                && file.Target(file.Pointer(node, "bNode", "id")) is { Code: "IM\0\0" } image
                && Picture(file, image, textures) is { } picture
                && !ModelTextures.IsOtherMap(picture.Name)) {
                return picture;
            }
            node = file.Target(file.Pointer(node, "bNode", "next"));
        }

        return null;
    }


    /// <summary>An image's bytes when it is packed into the file, else the file it names: <c>//</c> starts a path relative to the model.</summary>
    private static MeshTexture? Picture(BlendFile file, Block image, ModelTextures textures) {
        string path = file.Text(image, "Image", "name");
        if (path.StartsWith("//", StringComparison.Ordinal)) {
            path = path[2..];
        }

        if (file.Target(file.Pointer(image, "Image", "packedfile")) is { } packed
            && file.Target(file.Pointer(packed, "PackedFile", "data")) is { Length: > 0 } data) {
            return new MeshTexture(Path.GetFileName(path), null, file.Bytes[data.Start..(data.Start + data.Length)]);
        }

        return textures.Named(path);
    }


    /// <summary>What a material paints with.</summary>
    private readonly record struct Look(MeshColor? Color, MeshTexture? Texture);


    /// <summary>The objects' points and triangles, grouped by what paints them.</summary>
    private sealed class Builder {
        private readonly List<float> _positions = new();
        private readonly List<float> _uvs = new();
        private readonly Dictionary<Look, List<int>> _parts = new();
        private int _triangles;


        public void Add(BlendFile file, Block mesh, Matrix4x4 place, List<Look> looks) {
            int vertices = file.Int(mesh, "Mesh", "totvert");
            int corners = file.Int(mesh, "Mesh", "totloop");
            int polygons = file.Int(mesh, "Mesh", "totpoly");
            if (vertices <= 0 || corners <= 0 || polygons <= 0
                || file.Layer(mesh, "vdata", "position") is not { } positions
                || file.Layer(mesh, "ldata", ".corner_vert") is not { } cornerVert
                || file.Target(file.Pointer(mesh, "Mesh", "poly_offset_indices")) is not { } offsets
                || positions.Length < vertices * 12 || cornerVert.Length < corners * 4 || offsets.Length < (polygons + 1) * 4) {
                return;
            }

            var bytes = file.Bytes;
            var faceMaterials = file.Layer(mesh, "pdata", "material_index");
            var uv = looks.Any(l => l.Texture is not null) ? file.ActiveUvLayer(mesh) : null;
            if (uv is not null && uv.Length < corners * 8) {
                uv = null;
            }

            var points = new Vector3[vertices];
            for (int i = 0; i < vertices; i++) {
                var at = bytes.AsSpan(positions.Start + (i * 12));
                points[i] = Vector3.Transform(
                    new Vector3(BinaryPrimitives.ReadSingleLittleEndian(at), BinaryPrimitives.ReadSingleLittleEndian(at[4..]), BinaryPrimitives.ReadSingleLittleEndian(at[8..])),
                    place);
            }

            // Without a picture the points are the object's own and shared;
            // with one a vertex is a (point, UV) pair, V turned to count from the top.
            int baseIndex = _positions.Count / 3;
            var pairs = new Dictionary<(int, float, float), int>();
            if (uv is null) {
                foreach (var p in points) {
                    AddVertex(p, 0, 0);
                }
            }
            int Vertex(int corner) {
                int point = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(cornerVert.Start + (corner * 4)));
                if ((uint)point >= (uint)vertices) {
                    throw new FormatException("Blender corner beyond the vertices");
                }
                if (uv is null) {
                    return baseIndex + point;
                }

                var at = bytes.AsSpan(uv.Start + (corner * 8));
                float u = BinaryPrimitives.ReadSingleLittleEndian(at), v = 1 - BinaryPrimitives.ReadSingleLittleEndian(at[4..]);
                if (!pairs.TryGetValue((point, u, v), out int vertex)) {
                    vertex = _positions.Count / 3;
                    pairs[(point, u, v)] = vertex;
                    AddVertex(points[point], u, v);
                }

                return vertex;
            }

            for (int face = 0; face < polygons; face++) {
                int start = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offsets.Start + (face * 4)));
                int end = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offsets.Start + ((face + 1) * 4)));
                if (start < 0 || end > corners || end - start < 3) {
                    continue;
                }

                int slot = faceMaterials is not null && (face + 1) * 4 <= faceMaterials.Length
                    ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(faceMaterials.Start + (face * 4)))
                    : 0;
                var look = slot >= 0 && slot < looks.Count ? looks[slot] : looks.Count > 0 ? looks[0] : default;
                if (uv is null) {
                    look = look with { Texture = null };
                }
                if (!_parts.TryGetValue(look, out var list)) {
                    _parts[look] = list = new List<int>();
                }

                int first = Vertex(start);
                for (int k = start + 2; k < end; k++) {
                    list.Add(first);
                    list.Add(Vertex(k - 1));
                    list.Add(Vertex(k));
                    if (++_triangles > MeshFile.MaxTriangles) {
                        throw new FormatException("Blender file past the triangle limit");
                    }
                }
            }
        }


        public MeshData? ToMesh() {
            var parts = _parts.Select(p => new MeshPart(p.Value.ToArray(), p.Key.Color, p.Key.Texture)).ToList();
            bool textured = parts.Any(p => p.Texture is not null);

            return parts.Count > 0 ? new MeshData(_positions.ToArray(), parts, textured ? _uvs.ToArray() : null) : null;
        }


        private void AddVertex(Vector3 p, float u, float v) {
            _positions.Add(p.X);
            _positions.Add(p.Y);
            _positions.Add(p.Z);
            _uvs.Add(u);
            _uvs.Add(v);
        }
    }


    /// <summary>A block: its code, where its contents are, and the address it had when saved.</summary>
    private sealed record Block(string Code, int Start, int Length, long Address);


    /// <summary>The file's blocks and struct layouts, with fields read by name.</summary>
    private sealed class BlendFile {
        private const int HeaderSize = 12;
        private const int BlockHeadSize = 24;

        private readonly Dictionary<long, Block> _byAddress = new();
        private readonly Dictionary<string, Dictionary<string, (int Offset, int Size)>> _structs = new();
        private readonly Dictionary<string, int> _sizes = new();


        public BlendFile(byte[] bytes) {
            Bytes = bytes;
            int p = HeaderSize;
            Block? dna = null;
            while (p + BlockHeadSize <= bytes.Length) {
                string code = Encoding.ASCII.GetString(bytes, p, 4);
                int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p + 4));
                long address = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(p + 8));
                if (code == "ENDB") {
                    break;
                }
                if (length < 0 || length > bytes.Length - p - BlockHeadSize) {
                    throw new FormatException("Blender block out of bounds");
                }

                var block = new Block(code, p + BlockHeadSize, length, address);
                Blocks.Add(block);
                _byAddress[address] = block;
                if (code == "DNA1") {
                    dna = block;
                }
                p += BlockHeadSize + length;
            }
            if (dna is null) {
                throw new FormatException("Blender file without DNA");
            }
            ReadDna(dna);
        }


        public byte[] Bytes { get; }

        public List<Block> Blocks { get; } = new();


        public Block? Target(long address) {
            return address != 0 && _byAddress.TryGetValue(address, out var block) ? block : null;
        }

        public long Pointer(Block block, string type, string field) {
            return BinaryPrimitives.ReadInt64LittleEndian(Bytes.AsSpan(Offset(block, type, field)));
        }

        public int Int(Block block, string type, string field) {
            return BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(Offset(block, type, field)));
        }

        public short Short(Block block, string type, string field) {
            return BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(Offset(block, type, field)));
        }

        public float Float(Block block, string type, string field) {
            return BinaryPrimitives.ReadSingleLittleEndian(Bytes.AsSpan(Offset(block, type, field)));
        }

        public float[] Floats(Block block, string type, string field, int count) {
            int at = Offset(block, type, field);
            var result = new float[count];
            for (int i = 0; i < count; i++) {
                result[i] = BinaryPrimitives.ReadSingleLittleEndian(Bytes.AsSpan(at + (i * 4)));
            }

            return result;
        }

        /// <summary>A zero-ended text field.</summary>
        public string Text(Block block, string type, string field) {
            return ReadText(Offset(block, type, field), Field(type, field).Size);
        }

        /// <summary>A text field of a struct held in place inside another: <c>Material.id.name</c>.</summary>
        public string Text(Block block, string type, string inner, string innerType, string field) {
            return ReadText(Offset(block, type, inner) + Field(innerType, field).Offset, Field(innerType, field).Size);
        }


        /// <summary>The layer of this name in a mesh's <c>CustomData</c>: the block its data is in.</summary>
        public Block? Layer(Block mesh, string data, string name) {
            foreach (int layer in Layers(mesh, data)) {
                if (ReadText(layer + Field("CustomDataLayer", "name").Offset, Field("CustomDataLayer", "name").Size) == name) {
                    return LayerData(layer);
                }
            }

            return null;
        }


        /// <summary>
        /// The UV map the viewport uses: two-float corner layers keep the
        /// active one's number among them, and a bake usually lands on a
        /// second map. Names starting with a dot are internal - not a map.
        /// </summary>
        public Block? ActiveUvLayer(Block mesh) {
            var maps = Layers(mesh, "ldata")
                .Where(layer => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(layer + Field("CustomDataLayer", "type").Offset)) == LayerFloat2)
                .ToList();
            if (maps.Count == 0) {
                return null;
            }

            int active = BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(maps[0] + Field("CustomDataLayer", "active").Offset));
            int chosen = active >= 0 && active < maps.Count ? maps[active] : maps[0];
            string name = ReadText(chosen + Field("CustomDataLayer", "name").Offset, Field("CustomDataLayer", "name").Size);

            return name.StartsWith('.') ? null : LayerData(chosen);
        }


        private IEnumerable<int> Layers(Block mesh, string data) {
            int custom = Offset(mesh, "Mesh", data);
            long layers = BinaryPrimitives.ReadInt64LittleEndian(Bytes.AsSpan(custom + Field("CustomData", "layers").Offset));
            int count = BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(custom + Field("CustomData", "totlayer").Offset));
            if (Target(layers) is not { } block) {
                yield break;
            }

            if (!_sizes.TryGetValue("CustomDataLayer", out int size) || size <= 0) {
                throw new FormatException("Blender file without CustomDataLayer");
            }
            for (int i = 0; i < count && (i + 1) * size <= block.Length; i++) {
                yield return block.Start + (i * size);
            }
        }


        private Block? LayerData(int layer) {
            return Target(BinaryPrimitives.ReadInt64LittleEndian(Bytes.AsSpan(layer + Field("CustomDataLayer", "data").Offset)));
        }


        private int Offset(Block block, string type, string field) {
            var found = Field(type, field);
            if (found.Offset + found.Size > block.Length) {
                throw new FormatException($"Blender block too short for {type}.{field}");
            }

            return block.Start + found.Offset;
        }


        private (int Offset, int Size) Field(string type, string field) {
            return _structs.TryGetValue(type, out var fields) && fields.TryGetValue(field, out var found)
                ? found
                : throw new FormatException($"Blender file without {type}.{field}");
        }


        private string ReadText(int at, int size) {
            var span = Bytes.AsSpan(at, size);
            int end = span.IndexOf((byte)0);

            return Encoding.UTF8.GetString(end >= 0 ? span[..end] : span);
        }


        /// <summary>
        /// <c>SDNA</c>: <c>NAME</c> field names, <c>TYPE</c> type names,
        /// <c>TLEN</c> their sizes, <c>STRC</c> each struct as pairs (type,
        /// name) in order - an offset is the sum of the sizes before it. A
        /// field name carries its stars and sizes: <c>*next</c>, <c>loc[3]</c>.
        /// </summary>
        private void ReadDna(Block dna) {
            int p = dna.Start + 4;
            string[] names = Strings("NAME", ref p, dna.Start);
            string[] types = Strings("TYPE", ref p, dna.Start);

            Expect("TLEN", ref p);
            var sizes = new short[types.Length];
            for (int i = 0; i < sizes.Length; i++) {
                sizes[i] = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(p));
                p += 2;
            }
            p = Aligned(p, dna.Start);

            Expect("STRC", ref p);
            int structs = BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(p));
            p += 4;
            for (int i = 0; i < structs; i++) {
                int type = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(p));
                int count = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(p + 2));
                p += 4;
                var fields = new Dictionary<string, (int, int)>();
                int offset = 0;
                for (int k = 0; k < count; k++) {
                    int fieldType = BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(p));
                    string name = names[BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(p + 2))];
                    p += 4;
                    int size = FieldSize(name, sizes[fieldType]);
                    fields[Bare(name)] = (offset, size);
                    offset += size;
                }
                _structs[types[type]] = fields;
                _sizes[types[type]] = sizes[type];
            }
        }


        private string[] Strings(string tag, ref int p, int start) {
            Expect(tag, ref p);
            int count = BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(p));
            p += 4;
            var result = new string[count];
            for (int i = 0; i < count; i++) {
                int end = Array.IndexOf(Bytes, (byte)0, p);
                if (end < 0) {
                    throw new FormatException("Blender DNA string out of bounds");
                }
                result[i] = Encoding.ASCII.GetString(Bytes, p, end - p);
                p = end + 1;
            }
            p = Aligned(p, start);

            return result;
        }


        private void Expect(string tag, ref int p) {
            if (Encoding.ASCII.GetString(Bytes, p, 4) != tag) {
                throw new FormatException($"Blender DNA without {tag}");
            }
            p += 4;
        }


        /// <summary>DNA sections start on four bytes, counted from the block, not from the file.</summary>
        private static int Aligned(int p, int start) {
            return start + ((p - start + 3) & ~3);
        }


        private static int FieldSize(string name, int typeSize) {
            int size = name.StartsWith('*') || name.StartsWith("(*", StringComparison.Ordinal) ? 8 : typeSize;
            int at = name.IndexOf('[');
            while (at >= 0) {
                int end = name.IndexOf(']', at);
                size *= int.Parse(name.AsSpan(at + 1, end - at - 1));
                at = name.IndexOf('[', end);
            }

            return size;
        }


        /// <summary>A field's name without its stars and sizes.</summary>
        private static string Bare(string name) {
            int at = name.IndexOf('[');
            string plain = at >= 0 ? name[..at] : name;

            return plain.Trim('*', '(', ')');
        }
    }
}
