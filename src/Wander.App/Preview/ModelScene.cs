using System.Windows.Media;
using System.Windows.Media.Media3D;
using Wander.Core.Preview;

namespace Wander.App.Preview;

/// <summary>
/// One drawable of a model: the triangles of a single material, and the
/// colour they are painted in front and behind.
/// </summary>
public sealed record ModelPart(MeshGeometry3D Geometry, Brush Front, Brush Back);


/// <summary>
/// A model ready to be handed to a viewport: its parts, where the camera
/// has to look, how far back it has to stand, and the line the pane prints
/// under it.
/// </summary>
public sealed record ModelScene(
    IReadOnlyList<ModelPart> Parts,
    Point3D Center,
    double Radius,
    int Triangles,
    int Vertices);


/// <summary>
/// Turns Core's format-agnostic mesh into WPF geometry. Built off the UI
/// thread: a mesh of a million triangles assembled on the dispatcher is a
/// frozen window.
///
/// <para>
/// No normals are supplied: WPF computes per-face ones for a mesh that has
/// none, which is exactly the faceted shading a preview of an untextured
/// solid wants, and it halves what the readers have to get right.
/// </para>
/// </summary>
internal static class ModelBuilder {
    /// <summary>What a model with no stated colour is drawn in.</summary>
    private const float ModelGrey = 0.76f;


    /// <summary>
    /// The scene for a mesh, or null when there is nothing to draw — an
    /// empty mesh, or one whose bounds cannot be computed.
    /// </summary>
    public static ModelScene? Build(MeshData mesh, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();

        var points = new Point3DCollection(mesh.VertexCount);
        for (int i = 0; i + 2 < mesh.Positions.Length; i += 3) {
            points.Add(new Point3D(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2]));
        }
        // Frozen once and shared by every part: the parts differ in which
        // triangles they draw, not in where the points are, and a copy per
        // material would multiply a large model's memory by however many
        // materials it happens to have.
        points.Freeze();

        var parts = new List<ModelPart>(mesh.Parts.Count);
        foreach (var part in mesh.Parts) {
            ct.ThrowIfCancellationRequested();

            var geometry = new MeshGeometry3D {
                Positions = points,
                TriangleIndices = new Int32Collection(part.Indices),
            };
            geometry.Freeze();
            parts.Add(new ModelPart(geometry, Paint(part.Color), Paint(part.Color, back: true)));
        }

        if (parts.Count == 0 || mesh.Bounds() is not { } box) {
            return null;
        }

        return new ModelScene(
            parts,
            new Point3D(box.CenterX, box.CenterY, box.CenterZ),
            Radius(box),
            mesh.TriangleCount,
            mesh.VertexCount);
    }


    /// <summary>
    /// The bounding <em>sphere</em>, not half the longest side. The model
    /// spins under the mouse, and a box's diagonal is what swings into
    /// frame when it turns — framing against the side instead lets a cube
    /// grow past the edges of the pane as soon as it is rotated off-axis.
    /// The floor keeps a degenerate model (one flat face) from putting the
    /// camera inside itself.
    /// </summary>
    private static double Radius(MeshBounds box) {
        return Math.Max(
            Math.Sqrt(
                (box.SizeX * (double)box.SizeX)
                + (box.SizeY * (double)box.SizeY)
                + (box.SizeZ * (double)box.SizeZ)) / 2.0,
            0.0001);
    }


    /// <summary>
    /// The brush for one part.
    ///
    /// <para>
    /// Colours come from the file where it states one — <c>Kd</c> in an
    /// OBJ's material library, <c>baseColorFactor</c> in a glTF — and a
    /// model that states none stays the neutral grey it always was.
    /// Textures are still not read; this is the part of a material that
    /// costs nothing and takes a model from uniformly grey to
    /// recognisable, and the rest is a scope of its own (see BACKLOG.md).
    /// </para>
    ///
    /// <para>
    /// <paramref name="back"/> darkens it for the reverse of a face.
    /// Meshes with inconsistent winding are routine in exported OBJ and
    /// STL, and without a back material those faces are simply missing;
    /// with a darker one they read as the inside of a solid.
    /// </para>
    /// </summary>
    private static Brush Paint(MeshColor? colour, bool back = false) {
        const double BackFactor = 0.72;

        var (r, g, b) = colour is { } c
            ? (c.R, c.G, c.B)
            : (ModelGrey, ModelGrey, ModelGrey);

        double shade = back ? BackFactor : 1.0;
        var brush = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(r * 255 * shade),
            (byte)Math.Round(g * 255 * shade),
            (byte)Math.Round(b * 255 * shade)));
        brush.Freeze();

        return brush;
    }
}
