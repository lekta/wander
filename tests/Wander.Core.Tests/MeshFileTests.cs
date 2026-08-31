using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// The models here are built in the test, one cube or one triangle at a
/// time, because what is under test is index arithmetic: one-based and
/// negative OBJ indices, glTF's two stacked byte offsets and its
/// column-major matrices, and the size test that tells a binary STL from a
/// text one that also begins with the word "solid".
/// </summary>
public class MeshFileTests {
    // --- STL --------------------------------------------------------------

    [Fact]
    public void Stl_Binary_ReadsEveryTriangle() {
        string path = Temp("cube.stl", BinaryStl(Triangles(12)));

        var mesh = MeshFile.Read(path);

        Assert.NotNull(mesh);
        Assert.Equal(12, mesh!.TriangleCount);
        Assert.Equal(36, mesh.VertexCount);
    }

    [Fact]
    public void Stl_Ascii_ReadsEveryTriangle() {
        var text = new StringBuilder("solid test\n");
        foreach (var (a, b, c) in Triangles(4)) {
            text.Append("  facet normal 0 0 1\n    outer loop\n");
            foreach (var v in new[] { a, b, c }) {
                text.Append(CultureInfo.InvariantCulture, $"      vertex {v.X} {v.Y} {v.Z}\n");
            }
            text.Append("    endloop\n  endfacet\n");
        }
        text.Append("endsolid test\n");

        var mesh = MeshFile.Read(Temp("ascii.stl", Encoding.ASCII.GetBytes(text.ToString())));

        Assert.Equal(4, mesh!.TriangleCount);
    }

    [Fact]
    public void Stl_BinaryWhoseHeaderStartsWithSolid_IsStillReadAsBinary() {
        // The trap the format is famous for: exporters write their name
        // into the 80-byte header, and plenty of them start it with
        // "solid". Only the size arithmetic tells the two apart.
        byte[] binary = BinaryStl(Triangles(3));
        Encoding.ASCII.GetBytes("solid exported by something").CopyTo(binary, 0);

        var mesh = MeshFile.Read(Temp("tricky.stl", binary));

        Assert.Equal(3, mesh!.TriangleCount);
    }

    [Fact]
    public void Stl_TruncatedMidFacet_DropsThePartialTriangle() {
        string text = "solid t\nfacet normal 0 0 1\nouter loop\n"
            + "vertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\n"
            + "facet normal 0 0 1\nouter loop\nvertex 2 0 0\nvertex 3 0 0\n";

        var mesh = MeshFile.Read(Temp("cut.stl", Encoding.ASCII.GetBytes(text)));

        Assert.Equal(1, mesh!.TriangleCount);
    }

    [Fact]
    public void Stl_Garbage_IsNoPreviewRatherThanAThrow() {
        var noise = new byte[2048];
        new Random(11).NextBytes(noise);

        Assert.Null(Record.Exception(() => MeshFile.Read(Temp("noise.stl", noise))));
    }


    // --- OBJ --------------------------------------------------------------

    [Fact]
    public void Obj_ReadsVerticesAndFaces() {
        string obj = """
            # a square, as two triangles
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3
            f 1 3 4
            """;

        var mesh = MeshFile.Read(Temp("square.obj", Encoding.UTF8.GetBytes(obj)));

        Assert.Equal(4, mesh!.VertexCount);
        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, Assert.Single(mesh.Parts).Indices);
    }

    [Fact]
    public void Obj_QuadFace_IsFannedIntoTriangles() {
        string obj = "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n";

        var mesh = MeshFile.Read(Temp("quad.obj", Encoding.UTF8.GetBytes(obj)));

        Assert.Equal(2, mesh!.TriangleCount);
    }

    [Fact]
    public void Obj_NegativeIndices_CountBackFromTheNewestVertex() {
        // How streaming exporters write faces: -1 is the vertex just
        // emitted, not vertex one.
        string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf -3 -2 -1\n";

        var mesh = MeshFile.Read(Temp("negative.obj", Encoding.UTF8.GetBytes(obj)));

        Assert.Equal(new[] { 0, 1, 2 }, Assert.Single(mesh!.Parts).Indices);
    }

    [Fact]
    public void Obj_FaceWithTextureAndNormalIndices_UsesOnlyTheVertex() {
        string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\n"
            + "vt 0 0\nvn 0 0 1\n"
            + "f 1/1/1 2//1 3/1\n";

        var mesh = MeshFile.Read(Temp("slashes.obj", Encoding.UTF8.GetBytes(obj)));

        Assert.Equal(new[] { 0, 1, 2 }, Assert.Single(mesh!.Parts).Indices);
    }

    [Fact]
    public void Obj_FaceReferencingAVertexThatIsNotThere_IsSkipped() {
        string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 99\nf 1 2 3\n";

        var mesh = MeshFile.Read(Temp("bogus.obj", Encoding.UTF8.GetBytes(obj)));

        Assert.Equal(1, mesh!.TriangleCount);
    }


    // --- OBJ materials -----------------------------------------------------

    [Fact]
    public void Obj_SplitsByMaterialAndTakesTheDiffuseColour() {
        string dir = Scratch();
        File.WriteAllText(Path.Combine(dir, "paint.mtl"), """
            newmtl red
            Kd 1.0 0.0 0.0
            map_Kd not-loaded.png

            newmtl blue
            Kd 0.0 0.25 1.0
            """);
        string obj = "mtllib paint.mtl\n"
            + "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\n"
            + "usemtl red\nf 1 2 3\n"
            + "usemtl blue\nf 2 4 3\n";
        File.WriteAllText(Path.Combine(dir, "two.obj"), obj);

        var mesh = MeshFile.Read(Path.Combine(dir, "two.obj"));

        Assert.Equal(2, mesh!.Parts.Count);
        Assert.Equal(2, mesh.TriangleCount);
        // Positions are shared between the parts; only the index lists split.
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(new MeshColor(1f, 0f, 0f), mesh.Parts[0].Color);
        Assert.Equal(new MeshColor(0f, 0.25f, 1f), mesh.Parts[1].Color);
    }

    [Fact]
    public void Obj_MaterialNamesAndLibrariesMayContainSpaces() {
        // Which is not a corner case: Blender writes both this way.
        string dir = Scratch();
        File.WriteAllText(Path.Combine(dir, "ABDN 5.mtl"), "newmtl ABDN 5 CON\nKd 0.8 0.7 0.6\n");
        File.WriteAllText(
            Path.Combine(dir, "ABDN 5.obj"),
            "mtllib ABDN 5.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl ABDN 5 CON\nf 1 2 3\n");

        var mesh = MeshFile.Read(Path.Combine(dir, "ABDN 5.obj"));

        Assert.Equal(new MeshColor(0.8f, 0.7f, 0.6f), Assert.Single(mesh!.Parts).Color);
    }

    [Fact]
    public void Obj_WithoutAMaterialLibrary_IsOneUncolouredPart() {
        string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl missing\nf 1 2 3\n";

        var part = Assert.Single(MeshFile.Read(Temp("plain.obj", Encoding.UTF8.GetBytes(obj)))!.Parts);

        Assert.Null(part.Color);
    }

    [Fact]
    public void Obj_MaterialLibraryOutsideTheFolder_IsNotOpened() {
        string dir = Scratch();
        File.WriteAllText(
            Path.Combine(dir, "escape.obj"),
            "mtllib ../../paint.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl red\nf 1 2 3\n");

        var part = Assert.Single(MeshFile.Read(Path.Combine(dir, "escape.obj"))!.Parts);

        Assert.Null(part.Color);
    }

    [Fact]
    public void Obj_MaterialUsedTwice_KeepsOnePart() {
        // Exporters interleave groups; a material coming back must not
        // start a second drawable for the same colour.
        string dir = Scratch();
        File.WriteAllText(Path.Combine(dir, "p.mtl"), "newmtl a\nKd 1 1 0\nnewmtl b\nKd 0 1 1\n");
        File.WriteAllText(
            Path.Combine(dir, "interleaved.obj"),
            "mtllib p.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\n"
            + "usemtl a\nf 1 2 3\nusemtl b\nf 2 4 3\nusemtl a\nf 1 3 4\n");

        var mesh = MeshFile.Read(Path.Combine(dir, "interleaved.obj"));

        Assert.Equal(2, mesh!.Parts.Count);
        Assert.Equal(6, mesh.Parts[0].Indices.Length);      // both "a" faces in one part
        Assert.Equal(3, mesh.Parts[1].Indices.Length);
    }


    // --- glTF -------------------------------------------------------------

    [Fact]
    public void Glb_ReadsPositionsAndIndices() {
        var mesh = MeshFile.Read(Temp("tri.glb", Glb(Triangle(), translation: null)));

        Assert.Equal(1, mesh!.TriangleCount);
        Assert.Equal(3, mesh.VertexCount);
    }

    [Fact]
    public void Glb_AppliesTheNodeTransform() {
        // The reason the scene graph is walked at all: glTF stores a mesh
        // in local space, and a preview that ignores the node transform
        // stacks every part on the origin.
        var moved = MeshFile.Read(Temp("moved.glb", Glb(Triangle(), translation: new[] { 10f, 0f, 0f })));

        var bounds = moved!.Bounds()!.Value;
        Assert.Equal(10, bounds.MinX, tolerance: 0.001);
        Assert.Equal(11, bounds.MaxX, tolerance: 0.001);
    }

    [Fact]
    public void Gltf_WithAnEmbeddedDataUri_IsRead() {
        // The .gltf half of the format: same JSON, the blob inlined as
        // base64 instead of living in a chunk.
        byte[] glb = Glb(Triangle(), translation: null);
        (string json, byte[] bin) = SplitGlb(glb);

        // Hangs the blob off the buffer as a data: URI, leaving the rest of
        // the JSON exactly as the GLB had it.
        int at = json.IndexOf("\"buffers\":[{", StringComparison.Ordinal);
        int close = json.IndexOf('}', at);
        string embedded = json[..close]
            + ",\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String(bin) + "\""
            + json[close..];

        var mesh = MeshFile.Read(Temp("tri.gltf", Encoding.UTF8.GetBytes(embedded)));

        Assert.Equal(1, mesh!.TriangleCount);
    }

    [Fact]
    public void Glb_TakesTheMaterialBaseColour() {
        var mesh = MeshFile.Read(Temp("colour.glb", Glb(Triangle(), translation: null, baseColor: new[] { 0.2f, 0.4f, 0.6f })));

        var colour = Assert.Single(mesh!.Parts).Color;

        Assert.NotNull(colour);
        Assert.Equal(0.2f, colour!.Value.R, tolerance: 0.001f);
        Assert.Equal(0.6f, colour.Value.B, tolerance: 0.001f);
    }

    [Fact]
    public void Glb_WithoutAMaterial_IsUncoloured() {
        var mesh = MeshFile.Read(Temp("plain.glb", Glb(Triangle(), translation: null)));

        Assert.Null(Assert.Single(mesh!.Parts).Color);
    }

    [Fact]
    public void Gltf_NodeCycle_TerminatesInsteadOfRecursingForever() {
        // A hand-edited or broken file can point a child back at its
        // parent. In a preview pane that is a stack overflow, not a bad
        // picture.
        string json = """
            {"asset":{"version":"2.0"},
             "scenes":[{"nodes":[0]}],
             "nodes":[{"children":[1]},{"children":[0]}],
             "meshes":[]}
            """;

        Assert.Null(Record.Exception(() => MeshFile.Read(Temp("cycle.gltf", Encoding.UTF8.GetBytes(json)))));
    }

    [Fact]
    public void Gltf_BufferPointingOutsideItsFolder_IsNotFollowed() {
        string json = """
            {"asset":{"version":"2.0"},
             "buffers":[{"byteLength":36,"uri":"../../secrets.bin"}],
             "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36}],
             "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
             "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}]}
            """;

        Assert.Null(MeshFile.Read(Temp("escape.gltf", Encoding.UTF8.GetBytes(json))));
    }


    // --- routing ----------------------------------------------------------

    [Fact]
    public void OnlyTheFourExtensionsAreOurs() {
        Assert.True(MeshFile.IsMesh("thing.stl"));
        Assert.True(MeshFile.IsMesh("thing.OBJ"));
        Assert.True(MeshFile.IsMesh("thing.glb"));
        Assert.True(MeshFile.IsMesh("thing.gltf"));
        Assert.False(MeshFile.IsMesh("thing.fbx"));
        Assert.False(MeshFile.IsMesh("thing.blend"));
    }

    [Fact]
    public void EmptyFile_IsNoPreview() {
        Assert.Null(MeshFile.Read(Temp("empty.stl", Array.Empty<byte>())));
    }

    [Fact]
    public void Bounds_DescribeTheBoxTheModelSitsIn() {
        string obj = "v -1 -2 -3\nv 4 5 6\nv 0 0 0\nf 1 2 3\n";

        var bounds = MeshFile.Read(Temp("bounds.obj", Encoding.UTF8.GetBytes(obj)))!.Bounds()!.Value;

        Assert.Equal(-1, bounds.MinX);
        Assert.Equal(6, bounds.MaxZ);
        Assert.Equal(9, bounds.LongestSide);
        Assert.Equal(1.5, bounds.CenterX, tolerance: 0.001);
    }


    // --- fixtures ---------------------------------------------------------

    /// <summary>A folder of its own, for the cases where files have to sit beside each other.</summary>
    private static string Scratch() {
        string dir = Path.Combine(Path.GetTempPath(), "wander-mesh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        return dir;
    }

    private static string Temp(string name, byte[] bytes) {
        string dir = Path.Combine(Path.GetTempPath(), "wander-mesh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    private static List<((float X, float Y, float Z) A, (float X, float Y, float Z) B, (float X, float Y, float Z) C)>
        Triangles(int count) {
        var list = new List<((float, float, float), (float, float, float), (float, float, float))>();
        for (int i = 0; i < count; i++) {
            list.Add(((i, 0, 0), (i + 1f, 0, 0), (i, 1f, 0)));
        }

        return list;
    }

    private static byte[] BinaryStl(
        List<((float X, float Y, float Z) A, (float X, float Y, float Z) B, (float X, float Y, float Z) C)> triangles) {
        var bytes = new byte[80 + 4 + (triangles.Count * 50)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), (uint)triangles.Count);

        int p = 84;
        foreach (var (a, b, c) in triangles) {
            p += 12;                                    // normal, left zero
            foreach (var v in new[] { a, b, c }) {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p), v.X);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p + 4), v.Y);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(p + 8), v.Z);
                p += 12;
            }
            p += 2;
        }

        return bytes;
    }

    private static float[] Triangle() {
        return new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
    }

    /// <summary>
    /// A minimal but real GLB: header, JSON chunk, BIN chunk, positions as
    /// floats and indices as unsigned shorts.
    /// </summary>
    private static byte[] Glb(float[] positions, float[]? translation, float[]? baseColor = null) {
        var bin = new byte[(positions.Length * 4) + (3 * 2)];
        for (int i = 0; i < positions.Length; i++) {
            BinaryPrimitives.WriteSingleLittleEndian(bin.AsSpan(i * 4), positions[i]);
        }
        int indicesAt = positions.Length * 4;
        for (int i = 0; i < 3; i++) {
            BinaryPrimitives.WriteUInt16LittleEndian(bin.AsSpan(indicesAt + (i * 2)), (ushort)i);
        }

        string node = translation is null
            ? "{\"mesh\":0}"
            : "{\"mesh\":0,\"translation\":[" + string.Join(",",
                translation.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]}";

        string material = baseColor is null
            ? ""
            : ",\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorFactor\":["
                + string.Join(",", baseColor.Select(v => v.ToString(CultureInfo.InvariantCulture))) + ",1]}}]";
        string primitive = baseColor is null
            ? "{\"attributes\":{\"POSITION\":0},\"indices\":1}"
            : "{\"attributes\":{\"POSITION\":0},\"indices\":1,\"material\":0}";

        string json = "{\"asset\":{\"version\":\"2.0\"},"
            + "\"scene\":0,\"scenes\":[{\"nodes\":[0]}],"
            + "\"nodes\":[" + node + "]" + material + ","
            + "\"meshes\":[{\"primitives\":[" + primitive + "]}],"
            + "\"accessors\":["
            + "{\"bufferView\":0,\"componentType\":5126,\"count\":" + (positions.Length / 3) + ",\"type\":\"VEC3\"},"
            + "{\"bufferView\":1,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}],"
            + "\"bufferViews\":["
            + "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":" + indicesAt + "},"
            + "{\"buffer\":0,\"byteOffset\":" + indicesAt + ",\"byteLength\":6}],"
            + "\"buffers\":[{\"byteLength\":" + bin.Length + "}]}";

        byte[] jsonBytes = Pad(Encoding.UTF8.GetBytes(json), (byte)' ');
        byte[] binBytes = Pad(bin, 0);

        using var file = new MemoryStream();
        using var w = new BinaryWriter(file);
        w.Write(0x46546C67u);                            // "glTF"
        w.Write(2u);
        w.Write((uint)(12 + 8 + jsonBytes.Length + 8 + binBytes.Length));
        w.Write((uint)jsonBytes.Length);
        w.Write(0x4E4F534Au);                            // "JSON"
        w.Write(jsonBytes);
        w.Write((uint)binBytes.Length);
        w.Write(0x004E4942u);                            // "BIN\0"
        w.Write(binBytes);
        w.Flush();

        return file.ToArray();
    }

    /// <summary>Pulls the two chunks back out, so the .gltf test can reuse the same JSON.</summary>
    private static (string Json, byte[] Bin) SplitGlb(byte[] glb) {
        int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        string json = Encoding.UTF8.GetString(glb, 20, jsonLength).TrimEnd();
        int binAt = 20 + jsonLength;
        int binLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binAt));

        return (json, glb[(binAt + 8)..(binAt + 8 + binLength)]);
    }

    private static byte[] Pad(byte[] bytes, byte filler) {
        int padded = (bytes.Length + 3) & ~3;
        if (padded == bytes.Length) {
            return bytes;
        }

        var result = new byte[padded];
        bytes.CopyTo(result, 0);
        for (int i = bytes.Length; i < padded; i++) {
            result[i] = filler;
        }

        return result;
    }
}
