using System.Numerics;

namespace Wander.Core.Preview;

/// <summary>
/// A mesh's wireframe: its edges, and the strips that draw them on screen.
/// A 3D viewport has no lines of its own, so each edge is a thin quad turned
/// across the line of sight - a set width in pixels at any distance, hidden
/// by the model where the model is in front of it. The quads face the eye,
/// so they are laid again whenever the view turns or moves.
/// </summary>
public static class MeshWire {
    /// <summary>Past this many edges there is no wireframe: four points apiece, laid again on every turn.</summary>
    public const int MaxEdges = 150_000;


    /// <summary>
    /// Every edge once, as pairs of vertex indices, or null past
    /// <see cref="MaxEdges"/>. Vertices at the same place count as one: a
    /// textured mesh is split along its UV seams, and the seam would
    /// otherwise be drawn twice.
    /// </summary>
    public static int[]? Edges(MeshData mesh) {
        // Every edge belongs to some triangle, and a mesh has about one and
        // a half edges per triangle: past the cap, the count alone says no.
        if (mesh.TriangleCount > MaxEdges) {
            return null;
        }

        var p = mesh.Positions;
        var first = new Dictionary<(float, float, float), int>(mesh.VertexCount);
        var same = new int[mesh.VertexCount];
        for (int i = 0; i < same.Length; i++) {
            var key = (p[i * 3], p[(i * 3) + 1], p[(i * 3) + 2]);
            if (!first.TryGetValue(key, out int known)) {
                first[key] = known = i;
            }
            same[i] = known;
        }

        var seen = new HashSet<long>();
        var edges = new List<int>();
        foreach (var part in mesh.Parts) {
            var t = part.Indices;
            for (int i = 0; i + 2 < t.Length; i += 3) {
                for (int k = 0; k < 3; k++) {
                    int a = same[t[i + k]];
                    int b = same[t[i + ((k + 1) % 3)]];
                    if (a == b) {
                        continue;
                    }

                    long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                    if (!seen.Add(key)) {
                        continue;
                    }

                    if (edges.Count / 2 >= MaxEdges) {
                        return null;
                    }

                    edges.Add(a);
                    edges.Add(b);
                }
            }
        }

        return edges.ToArray();
    }


    /// <summary>
    /// The four corners of each edge's strip into <paramref name="corners"/>
    /// (twelve numbers per edge: start, start, end, end - one to each side).
    /// <paramref name="width"/> is the strip's width at distance one from
    /// <paramref name="eye"/>, so it keeps one width on screen; each end is
    /// moved toward the eye by <paramref name="lift"/> of its distance, so
    /// the strip lies over the faces it borders instead of through them.
    /// </summary>
    public static void Strips(float[] positions, int[] edges, Vector3 eye, float width, float lift, float[] corners) {
        for (int e = 0; e + 1 < edges.Length; e += 2) {
            var a = At(positions, edges[e]);
            var b = At(positions, edges[e + 1]);
            var along = b - a;
            int o = e / 2 * 12;
            Corners(a, along, eye, width, lift, corners, o);
            Corners(b, along, eye, width, lift, corners, o + 6);
        }
    }


    /// <summary>Two triangles per strip over the corners <see cref="Strips"/> lays.</summary>
    public static int[] StripTriangles(int edgeCount) {
        var t = new int[edgeCount * 6];
        for (int e = 0; e < edgeCount; e++) {
            int c = e * 4;
            int o = e * 6;
            t[o] = c;
            t[o + 1] = c + 1;
            t[o + 2] = c + 2;
            t[o + 3] = c + 2;
            t[o + 4] = c + 1;
            t[o + 5] = c + 3;
        }

        return t;
    }


    private static Vector3 At(float[] positions, int index) {
        return new Vector3(positions[index * 3], positions[(index * 3) + 1], positions[(index * 3) + 2]);
    }


    private static void Corners(Vector3 end, Vector3 along, Vector3 eye, float width, float lift, float[] corners, int o) {
        var toEye = eye - end;
        float distance = toEye.Length();
        var side = Vector3.Cross(along, toEye);
        float length = side.Length();
        var half = length > 0 ? side / length * (width * distance / 2) : Vector3.Zero;
        var middle = end + (toEye * lift);
        var left = middle - half;
        var right = middle + half;
        corners[o] = left.X;
        corners[o + 1] = left.Y;
        corners[o + 2] = left.Z;
        corners[o + 3] = right.X;
        corners[o + 4] = right.Y;
        corners[o + 5] = right.Z;
    }
}
