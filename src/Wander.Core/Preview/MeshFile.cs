using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// A triangle mesh, in the plainest form a renderer can be handed: one flat
/// run of X-Y-Z coordinates, and one or more parts indexing into it.
///
/// <para>
/// Parts exist because a model is rarely one material. An OBJ splits itself
/// with <c>usemtl</c>, a glTF gives every primitive its own material, and
/// the difference between them is a colour or a picture - so the geometry
/// is shared and only the index lists are separate.
/// </para>
///
/// <para>
/// <paramref name="TexCoords"/> - two per vertex, image orientation (V
/// down, as a bitmap is stored) - only when some part has a
/// <see cref="MeshPart.Texture"/>; null otherwise. A vertex is then a
/// (position, UV) pair: a reader whose file indexes the two separately
/// (OBJ, FBX) splits its points along the seams, and only for a model that
/// has a texture to show, so an untextured one keeps its shared points and
/// its shading. Parts without a texture of a textured model carry zeros.
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
public sealed record MeshData(float[] Positions, IReadOnlyList<MeshPart> Parts, float[]? TexCoords = null) {
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
/// <paramref name="Texture"/> is the diffuse picture, drawn instead of the
/// colour where it decodes; the colour stays for the tile and for a
/// picture that does not.
/// </summary>
public sealed record MeshPart(int[] Indices, MeshColor? Color, MeshTexture? Texture = null);


/// <summary>
/// A part's diffuse picture: a file found on disk (<see cref="File"/>) or
/// bytes the model carries inside itself (<see cref="Data"/>, a GLB image or
/// FBX embedded media) - one of the two. <see cref="Name"/> is the name the
/// model gives it, what tells the format (a TGA has no signature).
/// </summary>
public sealed record MeshTexture(string Name, string? File, byte[]? Data);


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
/// Reads the model formats the preview pane understands, chosen for
/// being small, documented and self-contained: STL (both flavours), OBJ,
/// glTF in its binary and JSON forms, PLY (text and binary), the 3MF
/// package slicers save, FBX 7 - read here (<see cref="FbxReader"/>)
/// rather than by a native importer, fifteen megabytes of DLL per
/// architecture against a portable single-file executable (decision of
/// 2026-10-09) - and Blender 4.x (<see cref="BlendReader"/>). Why COLLADA and
/// DXF are not here - BACKLOG.md.
/// </para>
/// </summary>
public static class MeshFile {
    /// <summary>
    /// The most triangles a preview will build. Past this the pane would be
    /// spending seconds and hundreds of megabytes on a picture the size of
    /// a postcard; a model this large is one to open in a real viewer.
    /// </summary>
    public const int MaxTriangles = 2_000_000;

    /// <summary>How much of a text model to read. A 200 MB OBJ is not a preview.</summary>
    private const long MaxFileSize = 256L * 1024 * 1024;

    private const string BlendExtension = ".blend";

    /// <summary>The most backups Blender's "Save Versions" keeps.</summary>
    private const int MaxBlenderBackups = 32;

    /// <summary>A texture file past this is not read: decoded to 2048 px anyway, and the pane waits for it.</summary>
    private const long MaxTextureSize = 64L * 1024 * 1024;


    /// <summary>Extensions this reader understands.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(
        new[] { ".stl", ".obj", ".gltf", ".glb", ".ply", ".3mf", ".fbx", ".blend" }.Concat(BlenderBackups()),
        StringComparer.OrdinalIgnoreCase);


    public static bool IsMesh(string path) {
        return Extensions.Contains(Path.GetExtension(path));
    }


    /// <summary>
    /// A copy Blender keeps of an earlier save - <c>.blend1</c>, <c>.blend2</c>:
    /// a model like any other, but its tile says it is not the current one.
    /// </summary>
    public static bool IsBackup(string path) {
        string ext = Path.GetExtension(path);

        return ext.Length > BlendExtension.Length
            && ext.StartsWith(BlendExtension, StringComparison.OrdinalIgnoreCase)
            && Extensions.Contains(ext);
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
            var textures = new ModelTextures(path);
            MeshData? mesh = ext.ToLowerInvariant() switch {
                ".stl" => StlReader.Read(SharedRead.ReadAllBytes(path)),
                ".obj" => ObjReader.Read(path, textures),
                ".glb" or ".gltf" => GltfReader.Read(path, textures),
                ".ply" => PlyReader.Read(SharedRead.ReadAllBytes(path)),
                ".fbx" => FbxReader.Read(SharedRead.ReadAllBytes(path), textures),
                _ when ext.StartsWith(BlendExtension, StringComparison.OrdinalIgnoreCase) => BlendReader.Read(SharedRead.ReadAllBytes(path), textures),
                ".3mf" => ReadPackage(path),
                _ => null,
            };

            return mesh is not null && mesh.TriangleCount > 0 ? mesh : null;
        } catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or NotSupportedException
                or OutOfMemoryException or FormatException or ArgumentException
                or InvalidDataException or System.Xml.XmlException or OverflowException or IndexOutOfRangeException) {
            return null;
        }
    }


    private static IEnumerable<string> BlenderBackups() {
        return Enumerable.Range(1, MaxBlenderBackups).Select(i => BlendExtension + i);
    }


    private static MeshData? ReadPackage(string path) {
        using var stream = SharedRead.Open(path);

        return ThreeMfReader.Read(stream);
    }


    /// <summary>
    /// The picture a model names, found on disk, or null. Models travel
    /// without the folders they were made in - a path written on another
    /// machine (<c>G:\Shared drives\...\Textures\x.png</c>) is the rule - so
    /// the name is looked for where people put textures: the path as
    /// written relative to the model, then the bare name beside it, in its
    /// <c>textures</c> folder and in one beside the model's folder. An
    /// absolute path is not opened as written, and a network path or URL
    /// not at all: previewing a model must not reach a host it is not on.
    /// </summary>
    internal static MeshTexture? FindTexture(string? directory, string reference) {
        reference = reference.Trim().Trim('"');
        if (directory is null || reference.Length == 0 || reference.Contains("://")
            || reference.StartsWith(@"\\", StringComparison.Ordinal) || reference.StartsWith("//", StringComparison.Ordinal)) {
            return null;
        }

        try {
            string normal = reference.Replace('/', Path.DirectorySeparatorChar);
            string name = Path.GetFileName(normal);
            var candidates = new List<string>();
            if (!Path.IsPathRooted(normal)) {
                candidates.Add(Path.Combine(directory, normal));
            }
            if (name.Length > 0) {
                candidates.Add(Path.Combine(directory, name));
                candidates.Add(Path.Combine(directory, "textures", name));
                candidates.Add(Path.Combine(directory, "..", "textures", name));
            }

            foreach (string candidate in candidates) {
                var info = new FileInfo(Path.GetFullPath(candidate));
                if (info.Exists && info.Length > 0 && info.Length <= MaxTextureSize) {
                    return new MeshTexture(name, info.FullName, null);
                }
            }
        } catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException) {
            return null;
        }

        return null;
    }


    /// <summary>
    /// A model of one material — what STL always is, and what an OBJ
    /// without a material library is.
    /// </summary>
    internal static MeshData Single(float[] positions, int[] indices) {
        return new MeshData(positions, new[] { new MeshPart(indices, null) });
    }
}
