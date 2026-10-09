using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// PLY and 3MF read through <see cref="MeshFile"/>, and a model drawn for
/// its tile. The files are built here, a square or a tetrahedron at a
/// time: what is tested is the header arithmetic of PLY, the transforms
/// and parts of a 3MF package, and where the drawing lands in its square.
/// </summary>
public class ModelFormatsTests {
    // --- PLY ----------------------------------------------------------------

    [Fact]
    public void Ply_Ascii_FansPolygonsIntoTriangles() {
        const string ply = "ply\nformat ascii 1.0\ncomment made by hand\n" +
            "element vertex 4\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\n" +
            "element face 1\nproperty list uchar int vertex_indices\nend_header\n" +
            "0 0 0 255\n1 0 0 0\n1 1 0 0\n0 1 0 0\n4 0 1 2 3\n";

        var mesh = MeshFile.Read(Temp("quad.ply", Encoding.ASCII.GetBytes(ply)));

        Assert.NotNull(mesh);
        Assert.Equal(2, mesh!.TriangleCount);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, mesh.Parts[0].Indices);
        Assert.Equal(1f, mesh.Positions[3]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ply_Binary_ReadsBothByteOrders_AndStepsOverOtherElements(bool little) {
        var data = new List<byte>();
        void Float(float f) {
            var b = new byte[4];
            if (little) {
                BinaryPrimitives.WriteSingleLittleEndian(b, f);
            } else {
                BinaryPrimitives.WriteSingleBigEndian(b, f);
            }
            data.AddRange(b);
        }
        void Int(int i) {
            var b = new byte[4];
            if (little) {
                BinaryPrimitives.WriteInt32LittleEndian(b, i);
            } else {
                BinaryPrimitives.WriteInt32BigEndian(b, i);
            }
            data.AddRange(b);
        }

        foreach (var (x, y) in new[] { (0f, 0f), (1f, 0f), (0f, 1f) }) {
            Float(x);
            Float(y);
            Float(2f);
        }
        // An element the reader does not know, with a list of its own.
        data.Add(2);
        Int(7);
        Int(8);
        data.Add(3);
        Int(0);
        Int(1);
        Int(2);
        string header = $"ply\nformat {(little ? "binary_little_endian" : "binary_big_endian")} 1.0\n" +
            "element vertex 3\nproperty float x\nproperty float y\nproperty float z\n" +
            "element edge 1\nproperty list uchar int ends\n" +
            "element face 1\nproperty list uchar int vertex_index\nend_header\n";

        var mesh = MeshFile.Read(Temp("tri.ply", Encoding.ASCII.GetBytes(header).Concat(data).ToArray()));

        Assert.NotNull(mesh);
        Assert.Equal(1, mesh!.TriangleCount);
        Assert.Equal(new[] { 0f, 0f, 2f, 1f, 0f, 2f, 0f, 1f, 2f }, mesh.Positions);
    }

    [Fact]
    public void Ply_PointCloud_IsNoMesh() {
        const string ply = "ply\nformat ascii 1.0\nelement vertex 2\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n1 1 1\n";

        Assert.Null(MeshFile.Read(Temp("cloud.ply", Encoding.ASCII.GetBytes(ply))));
    }

    [Fact]
    public void Ply_FaceBeyondTheVertices_IsNoMesh() {
        const string ply = "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n" +
            "element face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 9\n";

        Assert.Null(MeshFile.Read(Temp("bad.ply", Encoding.ASCII.GetBytes(ply))));
    }

    // --- 3MF ----------------------------------------------------------------

    [Fact]
    public void ThreeMf_PlacesObjectsByTheBuildAndTheirComponents() {
        // One triangle object, used by a component moved 10 along x, and
        // the component's object placed by a build item moved 5 along y.
        const string model = "<model xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" unit=\"millimeter\"><resources>" +
            "<basematerials id=\"9\"><base name=\"red\" displaycolor=\"#FF0000\"/></basematerials>" +
            "<object id=\"1\" type=\"model\" pid=\"9\" pindex=\"0\"><mesh><vertices>" +
            "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/>" +
            "</vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object>" +
            "<object id=\"2\" type=\"model\"><components><component objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 10 0 0\"/></components></object>" +
            "</resources><build><item objectid=\"2\" transform=\"1 0 0 0 1 0 0 0 1 0 5 0\"/></build></model>";

        var mesh = MeshFile.Read(Temp("plate.3mf", Package(("3D/3dmodel.model", model))));

        Assert.NotNull(mesh);
        var part = Assert.Single(mesh!.Parts);
        Assert.Equal(new MeshColor(1, 0, 0), part.Color);
        Assert.Equal(new[] { 10f, 5f, 0f, 11f, 5f, 0f, 10f, 6f, 0f }, mesh.Positions);
    }

    [Fact]
    public void ThreeMf_ObjectsInOtherParts_AreFollowed() {
        // How Bambu Studio and Orca write a project: the build's object is
        // a component naming an object in another model part.
        const string root = "<model xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" " +
            "xmlns:p=\"http://schemas.microsoft.com/3dmanufacturing/production/2015/06\"><resources>" +
            "<object id=\"2\"><components><component p:path=\"/3D/Objects/object_1.model\" objectid=\"1\"/></components></object>" +
            "</resources><build><item objectid=\"2\"/></build></model>";
        const string inner = "<model xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources>" +
            "<object id=\"1\"><mesh><vertices><vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"1\"/></vertices>" +
            "<triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object></resources><build/></model>";
        const string rels = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>";

        var mesh = MeshFile.Read(Temp("bambu.3mf", Package(("_rels/.rels", rels), ("3D/3dmodel.model", root), ("3D/Objects/object_1.model", inner))));

        Assert.Equal(1, mesh!.TriangleCount);
        Assert.Null(mesh.Parts[0].Color);
    }

    // --- Drawing ------------------------------------------------------------

    [Fact]
    public void Raster_DrawsTheModelInTheMiddle_OnTransparency() {
        var mesh = Tetrahedron();

        var image = MeshRaster.Render(mesh, 64);

        Assert.NotNull(image);
        Assert.Equal((64, 64), (image!.Width, image.Height));
        Assert.Equal(0, Alpha(image, 0, 0));
        Assert.Equal(0, Alpha(image, 63, 63));
        Assert.Equal(255, Alpha(image, 32, 32));
        // Lit, not black, and not past white.
        int at = (32 * 64 + 32) * 4;
        Assert.InRange(image.Pixels[at + 2], 40, 255);
    }

    [Fact]
    public void Raster_FillsItsSquare_WhateverTheModelsScale() {
        // A long thin bar: framed by its outline, it reaches both sides.
        var small = Box(0.001f, 0.0001f, 0.0001f);
        var image = MeshRaster.Render(small, 100)!;

        int left = Enumerable.Range(0, 100).First(x => Enumerable.Range(0, 100).Any(y => Alpha(image, x, y) > 0));
        int right = Enumerable.Range(0, 100).Last(x => Enumerable.Range(0, 100).Any(y => Alpha(image, x, y) > 0));

        Assert.InRange(left, 0, 10);
        Assert.InRange(right, 89, 99);
    }

    [Fact]
    public void Raster_TurnsTheModelByItsView() {
        // A bar along X: across the tile seen from the front, end-on
        // once turned a quarter round.
        var bar = Box(10, 1, 1);

        var front = Extent(MeshRaster.Render(bar, 100, new ModelView(0, 0))!);
        var endOn = Extent(MeshRaster.Render(bar, 100, new ModelView(90, 0))!);

        Assert.True(front.Width > front.Height * 3);
        Assert.True(endOn.Width < endOn.Height * 2);
    }

    [Theory]
    [InlineData(400, 10, 40, 10)]
    [InlineData(-190, 120, 170, 89)]
    [InlineData(180, -95, 180, -89)]
    public void View_IsNormalized(double spin, double tilt, double expectedSpin, double expectedTilt) {
        Assert.Equal(new ModelView(expectedSpin, expectedTilt), new ModelView(spin, tilt).Normalized());
    }

    [Fact]
    public void Raster_EmptyMesh_IsNull() {
        Assert.Null(MeshRaster.Render(new MeshData(Array.Empty<float>(), Array.Empty<MeshPart>()), 64));
    }


    // --- Builders -----------------------------------------------------------

    private static MeshData Tetrahedron() {
        float[] positions = { 0, 1, 0, -1, -1, 1, 1, -1, 1, 0, -1, -1 };
        int[] indices = { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 };

        return new MeshData(positions, new[] { new MeshPart(indices, null) });
    }

    private static MeshData Box(float x, float y, float z) {
        var positions = new List<float>();
        for (int i = 0; i < 8; i++) {
            positions.Add((i & 1) == 0 ? 0 : x);
            positions.Add((i & 2) == 0 ? 0 : y);
            positions.Add((i & 4) == 0 ? 0 : z);
        }
        int[] indices = {
            0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1,
            2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3,
        };

        return new MeshData(positions.ToArray(), new[] { new MeshPart(indices, null) });
    }

    /// <summary>The box the drawn pixels take.</summary>
    private static (int Width, int Height) Extent(Wander.Core.Imaging.BgraImage image) {
        var xs = new List<int>();
        var ys = new List<int>();
        for (int y = 0; y < image.Height; y++) {
            for (int x = 0; x < image.Width; x++) {
                if (Alpha(image, x, y) > 0) {
                    xs.Add(x);
                    ys.Add(y);
                }
            }
        }

        return (xs.Max() - xs.Min() + 1, ys.Max() - ys.Min() + 1);
    }

    private static int Alpha(Wander.Core.Imaging.BgraImage image, int x, int y) {
        return image.Pixels[(y * image.Width + x) * 4 + 3];
    }

    private static byte[] Package(params (string Name, string Text)[] entries) {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (name, text) in entries) {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static string Temp(string name, byte[] bytes) {
        string dir = Path.Combine(Path.GetTempPath(), "wander-mesh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }
}
