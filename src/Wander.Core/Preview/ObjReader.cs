using System.Globalization;

namespace Wander.Core.Preview;

/// <summary>
/// Wavefront OBJ, reduced to what a preview needs: where the points are,
/// which of them make triangles, and what colour each group of triangles
/// is.
///
/// <para>
/// Colour comes from the <c>.mtl</c> beside the file — the <c>Kd</c> line
/// of each material, and nothing else from it. Texture maps are read past
/// deliberately: a textured preview means splitting vertices by UV, loading
/// and downscaling image files named by relative paths, and one drawable
/// per map, which is a piece of work in its own right (see BACKLOG.md).
/// The diffuse colour is the part of that which costs nothing and takes a
/// model from uniformly grey to recognisable.
/// </para>
///
/// <para>
/// Two details in the face syntax are easy to get wrong and both are
/// checked by tests. Indices are <b>one-based</b>, and a negative index
/// counts backwards from the newest vertex — which is how exporters that
/// stream out geometry write it. And a face may have more than three
/// corners: quads are the common case, n-gons happen, and both are fanned
/// into triangles here rather than dropped, because dropping them leaves a
/// model full of holes.
/// </para>
/// </summary>
internal static class ObjReader {
    /// <summary>A material library past this is not a material library.</summary>
    private const long MaxMaterialFileSize = 8L * 1024 * 1024;

    private static int MaxTriangleIndices => MeshFile.MaxTriangles * 3;


    public static MeshData? Read(string path) {
        string text = File.ReadAllText(path);
        var positions = new List<float>();

        // One index list per material, in the order the materials are first
        // used, so a model without any is a single unnamed group and needs
        // no special case.
        var groups = new List<(string? Material, List<int> Indices)>();
        var byMaterial = new Dictionary<string, int>(StringComparer.Ordinal);
        var libraries = new List<string>();

        var current = new List<int>();
        groups.Add((null, current));

        var face = new List<int>();
        int total = 0;

        foreach (var line in text.AsSpan().EnumerateLines()) {
            var trimmed = line.Trim();
            if (trimmed.Length < 2 || trimmed[0] == '#') {
                continue;
            }

            if (Keyword(trimmed, "mtllib", out var library)) {
                libraries.Add(library.ToString().Trim());

                continue;
            }

            if (Keyword(trimmed, "usemtl", out var material)) {
                string name = material.ToString().Trim();
                if (!byMaterial.TryGetValue(name, out int index)) {
                    index = groups.Count;
                    byMaterial[name] = index;
                    groups.Add((name, new List<int>()));
                }
                current = groups[index].Indices;

                continue;
            }

            if (trimmed[0] == 'v' && (trimmed[1] == ' ' || trimmed[1] == '\t')) {
                var rest = trimmed[1..];
                if (TryNext(ref rest, out float x) && TryNext(ref rest, out float y) && TryNext(ref rest, out float z)) {
                    positions.Add(x);
                    positions.Add(y);
                    positions.Add(z);
                }

                continue;
            }

            if (trimmed[0] != 'f' || (trimmed[1] != ' ' && trimmed[1] != '\t')) {
                continue;
            }

            face.Clear();
            var corners = trimmed[1..];
            while (true) {
                corners = corners.TrimStart();
                if (corners.IsEmpty) {
                    break;
                }

                int end = corners.IndexOfAny(' ', '\t');
                var token = end < 0 ? corners : corners[..end];
                corners = end < 0 ? ReadOnlySpan<char>.Empty : corners[end..];

                // "12", "12/7", "12/7/3" and "12//3" all mean vertex 12.
                int slash = token.IndexOf('/');
                if (slash >= 0) {
                    token = token[..slash];
                }

                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) {
                    continue;
                }

                int vertexCount = positions.Count / 3;
                int resolved = index > 0 ? index - 1 : vertexCount + index;
                if (resolved >= 0 && resolved < vertexCount) {
                    face.Add(resolved);
                }
            }

            // Fan from the first corner: correct for a convex face, which
            // is what an exporter emits, and never worse than not drawing
            // the face at all for one that is not.
            for (int i = 2; i < face.Count; i++) {
                current.Add(face[0]);
                current.Add(face[i - 1]);
                current.Add(face[i]);
                total += 3;
            }

            if (total > MaxTriangleIndices) {
                break;
            }
        }

        var colours = ReadMaterials(Path.GetDirectoryName(path), libraries);

        var parts = new List<MeshPart>();
        foreach (var (material, indices) in groups) {
            if (indices.Count < 3) {
                continue;
            }

            MeshColor? colour = material is not null && colours.TryGetValue(material, out var found) ? found : null;
            parts.Add(new MeshPart(indices.ToArray(), colour));
        }

        return parts.Count == 0 ? null : new MeshData(positions.ToArray(), parts);
    }


    /// <summary>
    /// Diffuse colour per material name, out of the <c>.mtl</c> files the
    /// model names.
    ///
    /// <para>
    /// Only files beside the model are opened, and only by a plain relative
    /// name: a library that climbs out of the folder or names a host is a
    /// file we were not asked to read. Same rule as the glTF buffer.
    /// </para>
    /// </summary>
    private static Dictionary<string, MeshColor> ReadMaterials(string? directory, List<string> libraries) {
        var colours = new Dictionary<string, MeshColor>(StringComparer.Ordinal);
        if (directory is null) {
            return colours;
        }

        foreach (string library in libraries) {
            if (Beside(directory, library) is not { } file) {
                continue;
            }

            string? name = null;
            foreach (var line in File.ReadLines(file)) {
                var trimmed = line.AsSpan().Trim();
                if (Keyword(trimmed, "newmtl", out var declared)) {
                    name = declared.ToString().Trim();
                } else if (name is not null && Keyword(trimmed, "Kd", out var kd)) {
                    var rest = kd;
                    if (TryNext(ref rest, out float r) && TryNext(ref rest, out float g) && TryNext(ref rest, out float b)) {
                        colours[name] = MeshColor.Clamped(r, g, b);
                    }
                }
            }
        }

        return colours;
    }

    private static string? Beside(string directory, string name) {
        if (name.Length == 0 || name.Contains("..") || name.Contains("://")) {
            return null;
        }

        try {
            string full = Path.GetFullPath(Path.Combine(directory, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            return File.Exists(full) && new FileInfo(full).Length <= MaxMaterialFileSize ? full : null;
        } catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) {
            return null;
        }
    }


    /// <summary>
    /// Matches a leading keyword and hands back what follows it. Material
    /// names and file names may contain spaces, so the remainder is left
    /// whole rather than split.
    /// </summary>
    private static bool Keyword(ReadOnlySpan<char> line, string keyword, out ReadOnlySpan<char> rest) {
        rest = default;
        if (!line.StartsWith(keyword, StringComparison.Ordinal) || line.Length <= keyword.Length) {
            return false;
        }

        char next = line[keyword.Length];
        if (next != ' ' && next != '\t') {
            return false;
        }

        rest = line[(keyword.Length + 1)..];

        return true;
    }


    private static bool TryNext(ref ReadOnlySpan<char> rest, out float value) {
        rest = rest.TrimStart();
        int end = rest.IndexOfAny(' ', '\t');
        var token = end < 0 ? rest : rest[..end];
        rest = end < 0 ? ReadOnlySpan<char>.Empty : rest[end..];

        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
