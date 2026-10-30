using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// Blender files built block by block: the header, the blocks with the
/// addresses they "had", and a DNA that states only the fields read here -
/// so what is tested is that fields are found by name, not by offset. One
/// quad on an object moved along Blender's Z, which is up there and Y here.
/// </summary>
public class BlendReaderTests {
    [Fact]
    public void Quad_IsPlacedByItsObject_AndTurnedToYUp() {
        var mesh = MeshFile.Read(Temp("quad.blend", new BlendFile().Quad().ToArray()))!;

        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal(4, mesh.VertexCount);
        // Point (1, 1, 0) with the object at Z 5: (1, 5, -1) with Y up.
        Assert.Contains((1f, 5f, -1f), Points(mesh));
        var part = Assert.Single(mesh.Parts);
        Assert.Equal(new MeshColor(1, 0, 0), part.Color);
        Assert.Null(part.Texture);
        Assert.Null(mesh.TexCoords);
    }

    [Fact]
    public void Picture_FoundByTheMaterialsName_IsLaidByTheUvLayer_VFromTheTop() {
        string path = Temp("quad.blend", new BlendFile().Quad().ToArray());
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path)!, "Wood_Albedo.png"), new byte[] { 1 });

        var mesh = MeshFile.Read(path)!;

        Assert.Equal("Wood_Albedo.png", Assert.Single(mesh.Parts).Texture!.Name);
        Assert.Equal(mesh.VertexCount * 2, mesh.TexCoords!.Length);
        // Corner 2, point (1, 1, 0), has UV (1, 0.25).
        int vertex = Points(mesh).IndexOf((1f, 5f, -1f));
        Assert.Equal((1f, 0.75f), (mesh.TexCoords[vertex * 2], mesh.TexCoords[(vertex * 2) + 1]));
    }

    [Fact]
    public void Picture_IsLaidByTheActiveUvLayer_NotTheFirst() {
        string path = Temp("quad.blend", new BlendFile().Quad(activeUv: 1).ToArray());
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path)!, "Wood_Albedo.png"), new byte[] { 1 });

        var mesh = MeshFile.Read(path)!;

        // Corner 2 on the second map ("Atlas") has UV (0.5, 0.5).
        int vertex = Points(mesh).IndexOf((1f, 5f, -1f));
        Assert.Equal((0.5f, 0.5f), (mesh.TexCoords![vertex * 2], mesh.TexCoords[(vertex * 2) + 1]));
    }

    [Fact]
    public void ObjectHiddenInTheViewport_IsNotDrawn() {
        Assert.Null(MeshFile.Read(Temp("hidden.blend", new BlendFile().Quad(restrictFlag: 1).ToArray())));
    }

    [Fact]
    public void DnaIsAlignedFromItsBlock_NotFromTheFile() {
        // An odd-sized block before the DNA is what real files have about half the time.
        var mesh = MeshFile.Read(Temp("odd.blend", new BlendFile().Quad(padding: 3).ToArray()));

        Assert.Equal(2, mesh!.TriangleCount);
    }

    [Theory]
    [InlineData("BLENDER-v305")]    // meshes stored differently
    [InlineData("BLENDER-V405")]    // big-endian
    [InlineData("BLENDER_v405")]    // 32-bit pointers
    public void OtherFlavours_AreNoPreview(string header) {
        byte[] file = new BlendFile().Quad().ToArray();
        Encoding.ASCII.GetBytes(header).CopyTo(file, 0);

        Assert.Null(MeshFile.Read(Temp("old.blend", file)));
    }

    [Fact]
    public void Compressed_ReadsTheSame() {
        // Blender writes several frames: the reader has to go on past the first.
        byte[] whole = new BlendFile().Quad().ToArray();
        using var compressor = new ZstdSharp.Compressor();
        byte[] packed = compressor.Wrap(whole[..100]).ToArray().Concat(compressor.Wrap(whole[100..]).ToArray()).ToArray();

        Assert.Equal(2, MeshFile.Read(Temp("zstd.blend", packed))!.TriangleCount);
    }

    [Fact]
    public void BrokenCompression_IsNoPreview() {
        Assert.Null(MeshFile.Read(Temp("zstd.blend", new byte[] { 0x28, 0xB5, 0x2F, 0xFD, 0x60, 0, 0, 0, 0, 0, 0, 0, 0 })));
    }

    [Theory]
    [InlineData(1 << 6, true)]              // enabled for the viewport
    [InlineData((1 << 6) | (1 << 8), false)]    // enabled, hidden with the eye
    [InlineData(0, false)]                  // in a disabled collection
    public void ViewLayer_SaysWhatIsShown_OverTheObjectsOwnFlag(int baseFlag, bool shown) {
        var mesh = MeshFile.Read(Temp("layers.blend", new BlendFile().Quad(baseFlag: (short)baseFlag).ToArray()));

        Assert.Equal(shown, mesh is not null);
    }

    [Fact]
    public void CutShort_NeverThrows() {
        byte[] whole = new BlendFile().Quad().ToArray();
        for (int cut = 0; cut < whole.Length; cut += 11) {
            string path = Temp("cut.blend", whole[..cut]);

            Assert.Null(Record.Exception(() => MeshFile.Read(path)));
        }
    }


    private static List<(float, float, float)> Points(MeshData mesh) {
        var points = new List<(float, float, float)>();
        for (int i = 0; i + 2 < mesh.Positions.Length; i += 3) {
            points.Add((MathF.Round(mesh.Positions[i], 3), MathF.Round(mesh.Positions[i + 1], 3), MathF.Round(mesh.Positions[i + 2], 3) + 0f));
        }

        return points;
    }

    private static string Temp(string name, byte[] bytes) {
        string dir = Path.Combine(Path.GetTempPath(), "wander-blend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }


    /// <summary>A file as Blender 4.5 lays it out, with the structs cut down to what the reader asks for.</summary>
    private sealed class BlendFile {
        private const long ObjectAddress = 0x1000, MeshAddress = 0x2000, MaterialAddress = 0x3000, SlotsAddress = 0x3100;
        private const long OffsetsAddress = 0x4000, VertexLayers = 0x5000, CornerLayers = 0x5100;
        private const long PositionsAddress = 0x6000, CornersAddress = 0x6100, UvAddress = 0x6200, AtlasAddress = 0x6300;

        /// <summary>Every struct the reader looks into: (type, field name), sizes from <see cref="TypeSize"/>.</summary>
        private static readonly (string Name, (string Type, string Field)[] Fields)[] _structs = {
            ("ID", new[] { ("char", "name[66]") }),
            ("ListBase", new[] { ("void", "*first"), ("void", "*last") }),
            ("CustomData", new[] { ("CustomDataLayer", "*layers"), ("int", "totlayer") }),
            ("CustomDataLayer", new[] { ("int", "type"), ("int", "active"), ("char", "name[68]"), ("void", "*data") }),
            ("Object", new[] {
                ("ID", "id"), ("short", "type"), ("short", "restrictflag"), ("short", "rotmode"), ("void", "*data"), ("Object", "*parent"),
                ("Material", "**mat"), ("char", "*matbits"), ("float", "loc[3]"), ("float", "dloc[3]"), ("float", "size[3]"), ("float", "dscale[3]"),
                ("float", "rot[3]"), ("float", "quat[4]"), ("float", "rotAxis[3]"), ("float", "rotAngle"), ("float", "parentinv[4][4]"),
            }),
            ("Mesh", new[] {
                ("Material", "**mat"), ("short", "totcol"), ("int", "totvert"), ("int", "totloop"), ("int", "totpoly"), ("int", "*poly_offset_indices"),
                ("CustomData", "vdata"), ("CustomData", "ldata"), ("CustomData", "pdata"),
            }),
            ("Material", new[] { ("ID", "id"), ("float", "r"), ("float", "g"), ("float", "b"), ("void", "*nodetree") }),
            ("Scene", new[] { ("ID", "id"), ("ListBase", "view_layers") }),
            ("ViewLayer", new[] { ("ViewLayer", "*next"), ("ListBase", "object_bases") }),
            ("Base", new[] { ("Base", "*next"), ("Object", "*object"), ("short", "flag") }),
        };

        private readonly List<byte> _file = new(Encoding.ASCII.GetBytes("BLENDER-v405"));


        /// <param name="restrictFlag">The object's <c>restrictflag</c>.</param>
        /// <param name="padding">Bytes of an unrelated block written before the DNA.</param>
        /// <param name="baseFlag">With it, a scene whose view layer holds the object with this <c>Base.flag</c>; without, no scene.</param>
        /// <param name="activeUv">Which of the two UV maps is active: 0 "Source", 1 "Atlas".</param>
        public BlendFile Quad(short restrictFlag = 0, int padding = 0, short? baseFlag = null, int activeUv = 0) {
            var ob = Struct("Object");
            ob.Text("id", "OBQuad");
            ob.Short("type", 1);
            ob.Short("restrictflag", restrictFlag);
            ob.Short("rotmode", 1);
            ob.Pointer("data", MeshAddress);
            ob.Floats("loc", 0, 0, 5);
            ob.Floats("size", 1, 1, 1);
            ob.Floats("dscale", 1, 1, 1);
            Block("OB\0\0", ObjectAddress, ob.Bytes);

            var me = Struct("Mesh");
            me.Pointer("mat", SlotsAddress);
            me.Short("totcol", 1);
            me.Int("totvert", 4);
            me.Int("totloop", 4);
            me.Int("totpoly", 1);
            me.Pointer("poly_offset_indices", OffsetsAddress);
            me.Pointer("vdata", VertexLayers);
            me.Int("vdata", 1, Offset("CustomData", "totlayer"));
            me.Pointer("ldata", CornerLayers);
            me.Int("ldata", 3, Offset("CustomData", "totlayer"));
            Block("ME\0\0", MeshAddress, me.Bytes);

            var material = Struct("Material");
            material.Text("id", "MAWood");
            material.Floats("r", 1, 0, 0);
            Block("MA\0\0", MaterialAddress, material.Bytes);
            Block("DATA", SlotsAddress, BitConverter.GetBytes(MaterialAddress));

            Block("DATA", OffsetsAddress, Ints(0, 4));
            Block("DATA", VertexLayers, Layer(48, "position", PositionsAddress));
            Block("DATA", CornerLayers, Layer(11, ".corner_vert", CornersAddress)
                .Concat(Layer(49, "Source", UvAddress, activeUv))
                .Concat(Layer(49, "Atlas", AtlasAddress, activeUv))
                .ToArray());
            Block("DATA", PositionsAddress, Floats(0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0));
            Block("DATA", CornersAddress, Ints(0, 1, 2, 3));
            Block("DATA", UvAddress, Floats(0, 0, 1, 0, 1, 0.25f, 0, 1));
            Block("DATA", AtlasAddress, Floats(0, 0, 0.5f, 0, 0.5f, 0.5f, 0, 0.5f));
            if (padding > 0) {
                Block("DATA", 0x7000, new byte[padding]);
            }
            if (baseFlag is { } flag) {
                var scene = Struct("Scene");
                scene.Pointer("view_layers", 0x7100);
                Block("SC\0\0", 0x7200, scene.Bytes);
                var layer = Struct("ViewLayer");
                layer.Pointer("object_bases", 0x7300);
                Block("DATA", 0x7100, layer.Bytes);
                var entry = Struct("Base");
                entry.Pointer("object", ObjectAddress);
                entry.Short("flag", flag);
                Block("DATA", 0x7300, entry.Bytes);
            }

            Block("DNA1", 0x8000, Dna());
            Block("ENDB", 0, Array.Empty<byte>());

            return this;
        }


        public byte[] ToArray() {
            return _file.ToArray();
        }


        private void Block(string code, long address, byte[] contents) {
            _file.AddRange(Encoding.ASCII.GetBytes(code));
            _file.AddRange(BitConverter.GetBytes(contents.Length));
            _file.AddRange(BitConverter.GetBytes(address));
            _file.AddRange(BitConverter.GetBytes(0));    // the block's struct: the reader goes by what points at it
            _file.AddRange(BitConverter.GetBytes(1));
            _file.AddRange(contents);
        }


        private static byte[] Layer(int type, string name, long data, int active = 0) {
            var layer = Struct("CustomDataLayer");
            layer.Int("type", type);
            layer.Int("active", active);
            layer.Text("name", name);
            layer.Pointer("data", data);

            return layer.Bytes;
        }


        private static byte[] Ints(params int[] values) {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }

        private static byte[] Floats(params float[] values) {
            return values.SelectMany(BitConverter.GetBytes).ToArray();
        }


        private static byte[] Dna() {
            var names = new List<string>();
            var types = new List<string> { "char", "short", "int", "float", "void" };
            foreach (var (name, fields) in _structs) {
                types.Add(name);
                names.AddRange(fields.Select(f => f.Field));
            }
            names = names.Distinct().ToList();

            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(Encoding.ASCII.GetBytes("SDNA"));
            Section(w, "NAME", names);
            Section(w, "TYPE", types);
            w.Write(Encoding.ASCII.GetBytes("TLEN"));
            foreach (string type in types) {
                w.Write((short)TypeSize(type));
            }
            Align(w);
            w.Write(Encoding.ASCII.GetBytes("STRC"));
            w.Write(_structs.Length);
            foreach (var (name, fields) in _structs) {
                w.Write((short)types.IndexOf(name));
                w.Write((short)fields.Length);
                foreach (var (type, field) in fields) {
                    w.Write((short)types.IndexOf(type));
                    w.Write((short)names.IndexOf(field));
                }
            }
            w.Flush();

            return stream.ToArray();
        }


        private static void Section(BinaryWriter w, string tag, List<string> strings) {
            w.Write(Encoding.ASCII.GetBytes(tag));
            w.Write(strings.Count);
            foreach (string s in strings) {
                w.Write(Encoding.ASCII.GetBytes(s));
                w.Write((byte)0);
            }
            Align(w);
        }


        private static void Align(BinaryWriter w) {
            w.Flush();
            while (w.BaseStream.Position % 4 != 0) {
                w.Write((byte)0);
            }
        }


        private static int TypeSize(string type) {
            return type switch {
                "char" => 1,
                "short" => 2,
                "int" or "float" => 4,
                "void" => 0,
                _ => _structs.First(s => s.Name == type).Fields.Sum(f => FieldSize(f.Type, f.Field)),
            };
        }


        private static int FieldSize(string type, string field) {
            int size = field.StartsWith('*') ? 8 : TypeSize(type);
            foreach (string part in field.Split('[').Skip(1)) {
                size *= int.Parse(part.TrimEnd(']'));
            }

            return size;
        }


        private static int Offset(string type, string field) {
            int offset = 0;
            foreach (var (fieldType, name) in _structs.First(s => s.Name == type).Fields) {
                if (name.Trim('*').Split('[')[0] == field) {
                    return offset;
                }
                offset += FieldSize(fieldType, name);
            }

            throw new ArgumentException(field);
        }


        private static Filled Struct(string type) {
            return new Filled(type, new byte[TypeSize(type)]);
        }


        /// <summary>A struct's bytes, written field by field.</summary>
        private sealed record Filled(string Type, byte[] Bytes) {
            public void Short(string field, short value) {
                BitConverter.GetBytes(value).CopyTo(Bytes, Offset(Type, field));
            }

            public void Int(string field, int value, int within = 0) {
                BitConverter.GetBytes(value).CopyTo(Bytes, Offset(Type, field) + within);
            }

            public void Pointer(string field, long value) {
                BitConverter.GetBytes(value).CopyTo(Bytes, Offset(Type, field));
            }

            public void Floats(string first, params float[] values) {
                BlendFile.Floats(values).CopyTo(Bytes, Offset(Type, first));
            }

            public void Text(string field, string value) {
                Encoding.ASCII.GetBytes(value).CopyTo(Bytes, Offset(Type, field));
            }
        }
    }
}
