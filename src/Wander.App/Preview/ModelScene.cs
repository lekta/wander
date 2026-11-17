using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.App.Preview;

/// <summary>
/// One drawable of a model: the triangles of a single material, and its
/// colour or picture - lit, flat or swapped for clay by <see cref="ModelLook"/>.
/// </summary>
public sealed record ModelPart(MeshGeometry3D Geometry, Brush Surface);


/// <summary>
/// The model's edges for the wireframe (<see cref="MeshWire"/>): the
/// mesh's own positions, untransformed, and pairs of indices into them.
/// </summary>
public sealed record ModelWire(float[] Positions, int[] Edges);


/// <summary>
/// A model ready to be handed to a viewport: its parts, where the camera
/// has to look, how far back it has to stand, and the line the pane prints
/// under it. <see cref="Wire"/> is null past <see cref="MeshWire.MaxEdges"/>;
/// <see cref="Dark"/> - its colours are dark, the wireframe goes light.
/// </summary>
public sealed record ModelScene(
    IReadOnlyList<ModelPart> Parts,
    Point3D Center,
    double Radius,
    int Triangles,
    int Vertices,
    ModelWire? Wire,
    bool Dark);


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
///
/// <para>
/// Pictures are decoded here too, before the model is handed over: the
/// pane shows the model once, already textured, rather than grey first
/// and repainted on screen.
/// </para>
/// </summary>
internal static class ModelBuilder {
    /// <summary>What a model with no stated colour is drawn in.</summary>
    private const float ModelGrey = 0.76f;

    /// <summary>A picture's colours are not looked at: taken as a middle grey.</summary>
    private const double PictureLightness = 0.5;

    /// <summary>Darker than this on average, the wireframe is drawn light.</summary>
    private const double DarkBelow = 0.35;

    /// <summary>A picture's longest side past this is decoded down to it: a preview pane, not a texture budget.</summary>
    private const int MaxTextureSide = 2048;


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

        PointCollection? texCoords = null;
        if (mesh.TexCoords is { } uv) {
            texCoords = new PointCollection(uv.Length / 2);
            for (int i = 0; i + 1 < uv.Length; i += 2) {
                texCoords.Add(new Point(uv[i], uv[i + 1]));
            }
            texCoords.Freeze();
        }

        var pictures = new Dictionary<MeshTexture, Brush?>();
        var parts = new List<ModelPart>(mesh.Parts.Count);
        double lightness = 0;
        foreach (var part in mesh.Parts) {
            ct.ThrowIfCancellationRequested();

            var geometry = new MeshGeometry3D {
                Positions = points,
                TriangleIndices = new Int32Collection(part.Indices),
            };
            if (texCoords is not null) {
                geometry.TextureCoordinates = texCoords;
            }
            geometry.Freeze();

            var picture = part.Texture is { } texture && texCoords is not null
                ? pictures.TryGetValue(texture, out var known) ? known : pictures[texture] = Picture(texture)
                : null;
            parts.Add(new ModelPart(geometry, picture ?? Paint(part.Color)));
            lightness += picture is not null ? PictureLightness : Lightness(part.Color);
        }

        if (parts.Count == 0 || mesh.Bounds() is not { } box) {
            return null;
        }

        return new ModelScene(
            parts,
            new Point3D(box.CenterX, box.CenterY, box.CenterZ),
            Radius(box),
            mesh.TriangleCount,
            mesh.VertexCount,
            MeshWire.Edges(mesh) is { } edges ? new ModelWire(mesh.Positions, edges) : null,
            lightness / parts.Count < DarkBelow);
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
    /// model that states none stays the neutral grey it always was. A part
    /// with a picture is painted with that instead (<see cref="Picture"/>).
    /// </para>
    ///
    /// </summary>
    private static Brush Paint(MeshColor? colour) {
        var (r, g, b) = colour is { } c
            ? (c.R, c.G, c.B)
            : (ModelGrey, ModelGrey, ModelGrey);

        var brush = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255)));
        brush.Freeze();

        return brush;
    }


    private static double Lightness(MeshColor? colour) {
        return colour is { } c ? (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B) : ModelGrey;
    }


    /// <summary>
    /// A part's picture as a brush laid by the mesh's UVs, or null when it
    /// cannot be decoded - the part then keeps its colour. The viewport is
    /// absolute and tiled: UVs are picture coordinates, 0...1 across, and
    /// past it the picture repeats, as every exporter assumes; a relative
    /// viewport would stretch the picture over the UVs' own bounds.
    /// </summary>
    private static Brush? Picture(MeshTexture texture) {
        BitmapSource? bitmap;
        try {
            byte[]? bytes = texture.Data ?? (texture.File is { } file ? SharedRead.ReadAllBytes(file) : null);
            bitmap = bytes is null ? null : Decode(bytes, texture.Name);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException) {
            bitmap = null;
        }
        if (bitmap is null) {
            return null;
        }

        var brush = new ImageBrush(bitmap) {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 1),
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
        };
        brush.Freeze();

        return brush;
    }


    /// <summary>
    /// Decoded no larger than <see cref="MaxTextureSide"/>: WIC formats
    /// through the decoder's own downscale, TGA (no codec, no signature -
    /// told by its name) by Core's decoder and a scale after.
    /// </summary>
    private static BitmapSource? Decode(byte[] bytes, string name) {
        if (Path.GetExtension(name).Equals(".tga", StringComparison.OrdinalIgnoreCase)) {
            if (ImageDecoder.Tga(bytes) is not { } tga) {
                return null;
            }

            int side = Math.Max(tga.PixelWidth, tga.PixelHeight);
            if (side <= MaxTextureSide) {
                return tga;
            }

            double scale = MaxTextureSide / (double)side;
            var scaled = new TransformedBitmap(tga, new ScaleTransform(scale, scale));
            scaled.Freeze();

            return scaled;
        }

        int? width = null;
        if (ImageDecoder.StoredSize(bytes) is { } size && Math.Max(size.Width, size.Height) > MaxTextureSide) {
            width = (int)Math.Round(size.Width * (MaxTextureSide / (double)Math.Max(size.Width, size.Height)));
        }

        return ImageDecoder.Stream(bytes, width);
    }
}
