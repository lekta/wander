namespace Wander.Core.Preview;

/// <summary>
/// A triangle mesh, in the plainest form a renderer can be handed: one flat
/// run of X-Y-Z coordinates, and one or more parts indexing into it.
///
/// <para>
/// Parts exist because a model is rarely one material. An OBJ splits itself
/// with <c>usemtl</c>, a glTF gives every primitive its own material, and
/// the difference between them is a colour — so the geometry is shared and
/// only the index lists are separate. That is also why this stops short of
/// textures: sharing positions works precisely because a vertex has one
/// position and no UV, and adding UVs would mean splitting vertices, not
/// just indices (see BACKLOG.md).
/// </para>
///
/// <para>
/// Deliberately not a WPF <c>MeshGeometry3D</c>. Core does not know what
/// draws this — the readers here are file-format arithmetic, which is
/// exactly the part worth testing, and turning three floats into a
/// <c>Point3D</c> is the view's business.
/// </para>
///
/// <para>
/// No normals: WPF computes per-face normals for a mesh that supplies
/// none, and a preview has nothing better to say about shading than the
/// geometry does. Not carrying them halves what every reader has to get
/// right and what every file costs to load.
/// </para>
/// </summary>
public sealed record MeshData(float[] Positions, IReadOnlyList<MeshPart> Parts) {
    public int VertexCount => Positions.Length / 3;

    public int TriangleCount {
        get {
            int total = 0;
            foreach (var part in Parts) {
                total += part.Indices.Length;
            }

            return total / 3;
        }
    }

    /// <summary>
    /// The box the model sits in — what a camera has to be placed against.
    /// Returns null for an empty mesh, which is the same answer as "there
    /// is nothing to look at".
    /// </summary>
    public MeshBounds? Bounds() {
        if (Positions.Length < 3) {
            return null;
        }

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        for (int i = 0; i + 2 < Positions.Length; i += 3) {
            float x = Positions[i], y = Positions[i + 1], z = Positions[i + 2];
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) {
                continue;
            }

            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }

        return minX > maxX ? null : new MeshBounds(minX, minY, minZ, maxX, maxY, maxZ);
    }
}


/// <summary>
/// One material's worth of a model: which triangles it covers, and what
/// colour they are. <paramref name="Color"/> is null when the file says
/// nothing about it, which is the normal case for STL and for an OBJ with
/// no material library beside it — the view then uses its own grey.
/// </summary>
public sealed record MeshPart(int[] Indices, MeshColor? Color);


/// <summary>A colour as the model files state it: three channels, 0…1.</summary>
public readonly record struct MeshColor(float R, float G, float B) {
    public static MeshColor Clamped(double r, double g, double b) {
        return new MeshColor(Channel(r), Channel(g), Channel(b));
    }

    private static float Channel(double value) {
        return (float)Math.Clamp(value, 0.0, 1.0);
    }
}


public readonly record struct MeshBounds(
    float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) {
    public float SizeX => MaxX - MinX;

    public float SizeY => MaxY - MinY;

    public float SizeZ => MaxZ - MinZ;

    public float CenterX => (MinX + MaxX) / 2;

    public float CenterY => (MinY + MaxY) / 2;

    public float CenterZ => (MinZ + MaxZ) / 2;

    /// <summary>Longest edge of the box — the number a camera distance is derived from.</summary>
    public float LongestSide => Math.Max(SizeX, Math.Max(SizeY, SizeZ));
}


/// <summary>
/// Reads the three model formats the preview pane understands, chosen for
/// being small, documented and self-contained: STL (both flavours), OBJ,
/// and glTF in its binary and JSON forms.
///
/// <para>
/// FBX is deliberately not among them. It is a versioned node tree with
/// deflate-compressed arrays and a geometry layer model on top, which is a
/// parser an order of magnitude larger than all three of these together;
/// the alternative, a native importer, is fifteen megabytes of DLL per
/// architecture against a portable single-file executable. The reasoning is
/// written down in BACKLOG.md rather than here, so it survives the next
/// time somebody asks — along with why COLLADA and DXF are not here either.
/// </para>
/// </summary>
public static class MeshFile {
    /// <summary>Extensions this reader understands.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".stl", ".obj", ".gltf", ".glb",
    };

    /// <summary>
    /// The most triangles a preview will build. Past this the pane would be
    /// spending seconds and hundreds of megabytes on a picture the size of
    /// a postcard; a model this large is one to open in a real viewer.
    /// </summary>
    public const int MaxTriangles = 2_000_000;

    /// <summary>How much of a text model to read. A 200 MB OBJ is not a preview.</summary>
    private const long MaxFileSize = 256L * 1024 * 1024;


    public static bool IsMesh(string path) {
        return Extensions.Contains(Path.GetExtension(path));
    }


    /// <summary>
    /// The model at <paramref name="path"/>, or null when the format is not
    /// one of ours, the file is unreadable, or there turned out to be no
    /// geometry in it. Never throws for a malformed file: a model we cannot
    /// parse previews as "no preview", which is what it did before this
    /// existed.
    /// </summary>
    public static MeshData? Read(string path) {
        try {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxFileSize) {
                return null;
            }

            string ext = Path.GetExtension(path);
            MeshData? mesh = ext.ToLowerInvariant() switch {
                ".stl" => StlReader.Read(File.ReadAllBytes(path)),
                ".obj" => ObjReader.Read(path),
                ".glb" or ".gltf" => GltfReader.Read(path),
                _ => null,
            };

            return mesh is not null && mesh.TriangleCount > 0 ? mesh : null;
        } catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException
                or OutOfMemoryException or FormatException or ArgumentException) {
            return null;
        }
    }


    /// <summary>
    /// A model of one material — what STL always is, and what an OBJ
    /// without a material library is.
    /// </summary>
    internal static MeshData Single(float[] positions, int[] indices) {
        return new MeshData(positions, new[] { new MeshPart(indices, null) });
    }
}
