using System.Numerics;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class MeshWireTests {
    // A unit square in the XY plane: two triangles sharing the diagonal 1-2.
    private static readonly float[] _square = [0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0];


    [Fact]
    public void Edges_SharedEdgeOnce() {
        var mesh = new MeshData(_square, [new MeshPart([0, 1, 2, 2, 1, 3], null)]);

        var edges = MeshWire.Edges(mesh);

        Assert.NotNull(edges);
        Assert.Equal(10, edges!.Length);
    }

    [Fact]
    public void Edges_VerticesAtOnePlace_AreOne() {
        // The second triangle's corners repeat the first's two shared
        // points under new indices - a UV seam.
        float[] positions = [.. _square, 1, 0, 0, 0, 1, 0];
        var mesh = new MeshData(positions, [new MeshPart([0, 1, 2, 5, 4, 3], null)]);

        var edges = MeshWire.Edges(mesh);

        Assert.Equal(10, edges!.Length);
    }

    [Fact]
    public void Edges_PastTheCap_None() {
        var indices = new int[(MeshWire.MaxEdges + 1) * 3];
        var mesh = new MeshData(_square, [new MeshPart(indices, null)]);

        Assert.Null(MeshWire.Edges(mesh));
    }

    [Fact]
    public void Strips_AcrossTheSight_WidthByDistance_LiftedToTheEye() {
        float[] positions = [0, 0, 0, 1, 0, 0];
        var corners = new float[12];

        MeshWire.Strips(positions, [0, 1], new Vector3(0, 0, 10), width: 0.01f, lift: 0.1f, corners);

        // Seen down Z, an edge along X widens along Y: 0.01 x distance 10.
        Assert.Equal(0.1f, Math.Abs(corners[4] - corners[1]), 4);
        Assert.Equal(0f, corners[0], 4);
        Assert.Equal(0f, corners[3], 4);
        // A tenth of the way to the eye.
        Assert.Equal(1f, corners[2], 4);
        Assert.Equal(1f, corners[5], 4);
    }

    [Fact]
    public void StripTriangles_TwoPerEdge() {
        Assert.Equal([0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7], MeshWire.StripTriangles(2));
    }
}
