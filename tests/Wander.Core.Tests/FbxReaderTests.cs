using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// FBX files built record by record, binary and text: one quad (two
/// triangles), its model moved, the model's parent scaled, a material on
/// it. What is tested is the record layout of both versions, the
/// deflated arrays, the negated last corner of each polygon, and the
/// chain of transforms.
/// </summary>
public class FbxReaderTests {
    [Theory]
    [InlineData(7400, false)]
    [InlineData(7500, false)]
    [InlineData(7400, true)]
    public void Binary_PlacesTheGeometryThroughItsModels(int version, bool deflate) {
        var file = new BinaryFbx(version, deflate).Scene(upAxis: 1);

        var mesh = MeshFile.Read(Temp("quad.fbx", file));

        Assert.NotNull(mesh);
        Assert.Equal(2, mesh!.TriangleCount);
        var part = Assert.Single(mesh.Parts);
        Assert.Equal(new MeshColor(1, 0, 0), part.Color);
        // Corner (1, 1, 0): scaled by the parent's 2 after the child's +10 on X.
        Assert.Contains(Corner(mesh, 22, 2, 0), Corners(mesh));
        Assert.Contains(Corner(mesh, 20, 0, 0), Corners(mesh));
    }

    [Fact]
    public void Binary_ZUp_IsTurnedToYUp() {
        var mesh = MeshFile.Read(Temp("zup.fbx", new BinaryFbx(7400, false).Scene(upAxis: 2)))!;

        // (22, 2, 0) with Z up lands at (22, 0, -2).
        Assert.Contains(Corner(mesh, 22, 0, -2), Corners(mesh));
    }

    [Fact]
    public void Text_ReadsTheSameScene() {
        const string text = "; FBX 7.4.0 project file\n" +
            "FBXHeaderExtension:  {\n  FBXVersion: 7400\n}\n" +
            "Objects:  {\n" +
            "  Geometry: 100, \"Geometry::Quad\", \"Mesh\" {\n" +
            "    Vertices: *12 {\n      a: 0,0,0,1,0,0,1,1,0,0,1,0\n    }\n" +
            "    PolygonVertexIndex: *4 {\n      a: 0,1,2,-4\n    }\n" +
            "  }\n" +
            "  Model: 200, \"Model::Quad\", \"Mesh\" {\n" +
            "    Properties70:  {\n      P: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",10,0,0\n    }\n" +
            "  }\n" +
            "}\n" +
            "Connections:  {\n  C: \"OO\",100,200\n  C: \"OO\",200,0\n}\n";

        var mesh = MeshFile.Read(Temp("quad-text.fbx", Encoding.ASCII.GetBytes(text)));

        Assert.NotNull(mesh);
        Assert.Equal(2, mesh!.TriangleCount);
        Assert.Contains(Corner(mesh, 11, 1, 0), Corners(mesh));
    }

    [Fact]
    public void CutShort_NeverThrows() {
        var whole = new BinaryFbx(7400, true).Scene(upAxis: 1);
        for (int cut = 0; cut < whole.Length; cut += 7) {
            string path = Temp("cut.fbx", whole[..cut]);

            Assert.Null(Record.Exception(() => MeshFile.Read(path)));
        }
    }


    private static (float, float, float) Corner(MeshData mesh, float x, float y, float z) {
        return (x, y, z);
    }

    private static List<(float, float, float)> Corners(MeshData mesh) {
        var corners = new List<(float, float, float)>();
        for (int i = 0; i + 2 < mesh.Positions.Length; i += 3) {
            corners.Add((MathF.Round(mesh.Positions[i], 3), MathF.Round(mesh.Positions[i + 1], 3), MathF.Round(mesh.Positions[i + 2], 3) + 0f));
        }

        return corners;
    }

    private static string Temp(string name, byte[] bytes) {
        string dir = Path.Combine(Path.GetTempPath(), "wander-fbx-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }


    /// <summary>Binary FBX records: 32-bit offsets before 7500, 64-bit from it.</summary>
    private sealed class BinaryFbx {
        private readonly int _version;
        private readonly bool _deflate;
        private readonly List<byte> _out = new();


        public BinaryFbx(int version, bool deflate) {
            _version = version;
            _deflate = deflate;
        }


        private bool Wide => _version >= 7500;


        /// <summary>
        /// A quad on geometry 100, model 200 moved +10 on X, its parent
        /// model 300 scaled by 2, material 400 red on the child.
        /// </summary>
        public byte[] Scene(int upAxis) {
            _out.AddRange(Encoding.ASCII.GetBytes("Kaydara FBX Binary  "));
            _out.Add(0);
            _out.Add(0x1A);
            _out.Add(0);
            _out.AddRange(BitConverter.GetBytes((uint)_version));

            Node("GlobalSettings", Array.Empty<object>(), () => Node("Properties70", Array.Empty<object>(), () => {
                Node("P", new object[] { "UpAxis", "int", "Integer", "", upAxis });
            }));
            Node("Objects", Array.Empty<object>(), () => {
                Node("Geometry", new object[] { 100L, "Quad\0\u0001Geometry", "Mesh" }, () => {
                    Node("Vertices", new object[] { new double[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0 } });
                    Node("PolygonVertexIndex", new object[] { new[] { 0, 1, 2, -4 } });
                    Node("LayerElementMaterial", new object[] { 0 }, () => {
                        Node("MappingInformationType", new object[] { "AllSame" });
                        Node("Materials", new object[] { new[] { 0 } });
                    });
                });
                Node("Model", new object[] { 200L, "Child\0\u0001Model", "Mesh" }, () => Node("Properties70", Array.Empty<object>(), () => {
                    Node("P", new object[] { "Lcl Translation", "Lcl Translation", "", "A", 10.0, 0.0, 0.0 });
                }));
                Node("Model", new object[] { 300L, "Parent\0\u0001Model", "Null" }, () => Node("Properties70", Array.Empty<object>(), () => {
                    Node("P", new object[] { "Lcl Scaling", "Lcl Scaling", "", "A", 2.0, 2.0, 2.0 });
                }));
                Node("Material", new object[] { 400L, "Red\0\u0001Material", "" }, () => Node("Properties70", Array.Empty<object>(), () => {
                    Node("P", new object[] { "DiffuseColor", "Color", "", "A", 1.0, 0.0, 0.0 });
                }));
            });
            Node("Connections", Array.Empty<object>(), () => {
                Node("C", new object[] { "OO", 100L, 200L });
                Node("C", new object[] { "OO", 200L, 300L });
                Node("C", new object[] { "OO", 300L, 0L });
                Node("C", new object[] { "OO", 400L, 200L });
            });
            End();
            // The footer a real file ends with: never read.
            _out.AddRange(new byte[160]);

            return _out.ToArray();
        }


        private void Node(string name, object[] properties, Action? children = null) {
            int start = _out.Count;
            int head = Wide ? 25 : 13;
            _out.AddRange(new byte[head]);
            _out.AddRange(Encoding.ASCII.GetBytes(name));
            int propertiesStart = _out.Count;
            foreach (var p in properties) {
                Property(p);
            }
            int propertiesLength = _out.Count - propertiesStart;
            if (children is not null) {
                children();
                End();
            }

            var span = new byte[head];
            if (Wide) {
                BinaryPrimitives.WriteUInt64LittleEndian(span, (ulong)_out.Count);
                BinaryPrimitives.WriteUInt64LittleEndian(span.AsSpan(8), (ulong)properties.Length);
                BinaryPrimitives.WriteUInt64LittleEndian(span.AsSpan(16), (ulong)propertiesLength);
                span[24] = (byte)name.Length;
            } else {
                BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)_out.Count);
                BinaryPrimitives.WriteUInt32LittleEndian(span.AsSpan(4), (uint)properties.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(span.AsSpan(8), (uint)propertiesLength);
                span[12] = (byte)name.Length;
            }
            for (int i = 0; i < head; i++) {
                _out[start + i] = span[i];
            }
        }

        private void End() {
            _out.AddRange(new byte[Wide ? 25 : 13]);
        }

        private void Property(object value) {
            switch (value) {
                case string s:
                    _out.Add((byte)'S');
                    _out.AddRange(BitConverter.GetBytes(Encoding.UTF8.GetByteCount(s)));
                    _out.AddRange(Encoding.UTF8.GetBytes(s));
                    break;

                case long l:
                    _out.Add((byte)'L');
                    _out.AddRange(BitConverter.GetBytes(l));
                    break;

                case int i:
                    _out.Add((byte)'I');
                    _out.AddRange(BitConverter.GetBytes(i));
                    break;

                case double d:
                    _out.Add((byte)'D');
                    _out.AddRange(BitConverter.GetBytes(d));
                    break;

                case double[] array:
                    Packed('d', array.Length, array.SelectMany(BitConverter.GetBytes).ToArray());
                    break;

                case int[] array:
                    Packed('i', array.Length, array.SelectMany(BitConverter.GetBytes).ToArray());
                    break;

                default:
                    throw new ArgumentException(value.GetType().Name);
            }
        }

        private void Packed(char type, int length, byte[] raw) {
            byte[] stored = raw;
            if (_deflate) {
                var packed = new MemoryStream();
                using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) {
                    zlib.Write(raw);
                }
                stored = packed.ToArray();
            }
            _out.Add((byte)type);
            _out.AddRange(BitConverter.GetBytes(length));
            _out.AddRange(BitConverter.GetBytes(_deflate ? 1 : 0));
            _out.AddRange(BitConverter.GetBytes(stored.Length));
            _out.AddRange(stored);
        }
    }
}
