using System.Globalization;
using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// Wavefront OBJ, reduced to what a preview needs: where the points are,
/// which of them make triangles, and what colour each group of triangles
/// is.
///
/// <para>
/// Colour comes from the <c>.mtl</c> beside the file — the <c>Kd</c> line
/// of each material and its <c>map_Kd</c> picture
/// (<see cref="MeshFile.FindTexture"/>); the other maps are read past. A
/// material without a picture, or without a library at all, may still find
/// one around the model (<see cref="ModelTextures"/>). The UVs (<c>vt</c>,
/// <c>f v/vt</c>) are kept only when a used material has a picture: the
/// corners are then split into (position, UV) pairs, which an untextured
/// model has no reason to pay for.
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


    public static MeshData? Read(string path, ModelTextures textures) {
        string text = SharedRead.ReadAllText(path);
        var positions = new List<float>();
        var uvs = new List<float>();

        // One index list per material, in the order the materials are first
        // used, so a model without any is a single unnamed group and needs
        // no special case. Each corner's UV index (-1 for none) runs
        // alongside its position index.
        var groups = new List<(string? Material, List<int> Indices, List<int> Uvs)>();
        var byMaterial = new Dictionary<string, int>(StringComparer.Ordinal);
        var libraries = new List<string>();

        var current = groups.Count;
        groups.Add((null, new List<int>(), new List<int>()));

        var face = new List<(int Position, int Uv)>();
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
                    groups.Add((name, new List<int>(), new List<int>()));
                }
                current = index;

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

            if (Keyword(trimmed, "vt", out var coords)) {
                // A missing V is 0, as the format allows "vt u".
                var rest = coords;
                if (TryNext(ref rest, out float u)) {
                    uvs.Add(u);
                    uvs.Add(TryNext(ref rest, out float v) ? v : 0);
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

                // "12", "12/7", "12/7/3" and "12//3" all mean vertex 12;
                // the second field, when there is one, is the UV.
                var uvToken = ReadOnlySpan<char>.Empty;
                int slash = token.IndexOf('/');
                if (slash >= 0) {
                    uvToken = token[(slash + 1)..];
                    int second = uvToken.IndexOf('/');
                    if (second >= 0) {
                        uvToken = uvToken[..second];
                    }
                    token = token[..slash];
                }

                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) {
                    continue;
                }

                int resolved = Resolve(index, positions.Count / 3);
                if (resolved >= 0) {
                    int uv = int.TryParse(uvToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int uvIndex)
                        ? Resolve(uvIndex, uvs.Count / 2)
                        : -1;
                    face.Add((resolved, uv));
                }
            }

            // Fan from the first corner: correct for a convex face, which
            // is what an exporter emits, and never worse than not drawing
            // the face at all for one that is not.
            var (_, indices, uvIndices) = groups[current];
            for (int i = 2; i < face.Count; i++) {
                indices.Add(face[0].Position);
                indices.Add(face[i - 1].Position);
                indices.Add(face[i].Position);
                uvIndices.Add(face[0].Uv);
                uvIndices.Add(face[i - 1].Uv);
                uvIndices.Add(face[i].Uv);
                total += 3;
            }

            if (total > MaxTriangleIndices) {
                break;
            }
        }

        var materials = ReadMaterials(textures, Path.GetDirectoryName(path), libraries);
        ObjMaterial? MaterialOf(string? name) => name is not null && materials.TryGetValue(name, out var found) ? found : null;

        // A picture the library names, else one found by the material's name
        // around the model; asked only of a model with UVs to lay it by.
        var pictures = new Dictionary<int, MeshTexture?>();
        if (uvs.Count > 0) {
            for (int i = 0; i < groups.Count; i++) {
                if (groups[i].Indices.Count >= 3) {
                    pictures[i] = MaterialOf(groups[i].Material)?.Texture ?? textures.ForMaterial(groups[i].Material);
                }
            }
        }

        bool textured = pictures.Values.Any(p => p is not null);
        float[]? texCoords = null;
        float[] points = textured ? Split(groups, positions, uvs, out texCoords) : positions.ToArray();

        var parts = new List<MeshPart>();
        for (int i = 0; i < groups.Count; i++) {
            var (material, indices, _) = groups[i];
            if (indices.Count < 3) {
                continue;
            }

            parts.Add(new MeshPart(indices.ToArray(), MaterialOf(material)?.Color, pictures.GetValueOrDefault(i)));
        }

        return parts.Count == 0 ? null : new MeshData(points, parts, texCoords);
    }


    /// <summary>
    /// One vertex per distinct (position, UV) pair the faces use, the
    /// groups' index lists rewritten to them in place. V is flipped: OBJ
    /// counts it up from the bottom of the picture.
    /// </summary>
    private static float[] Split(
        List<(string? Material, List<int> Indices, List<int> Uvs)> groups,
        List<float> positions, List<float> uvs, out float[] texCoords) {
        var points = new List<float>();
        var coords = new List<float>();
        var known = new Dictionary<(int, int), int>();
        foreach (var (_, indices, uvIndices) in groups) {
            for (int i = 0; i < indices.Count; i++) {
                var key = (indices[i], uvIndices[i]);
                if (!known.TryGetValue(key, out int vertex)) {
                    vertex = points.Count / 3;
                    known[key] = vertex;
                    int p = key.Item1 * 3, t = key.Item2 * 2;
                    points.Add(positions[p]);
                    points.Add(positions[p + 1]);
                    points.Add(positions[p + 2]);
                    coords.Add(t >= 0 ? uvs[t] : 0);
                    coords.Add(t >= 0 ? 1 - uvs[t + 1] : 0);
                }
                indices[i] = vertex;
            }
        }
        texCoords = coords.ToArray();

        return points.ToArray();
    }


    /// <summary>A one-based index, or a negative one counted back from the newest element; -1 when out of range.</summary>
    private static int Resolve(int index, int count) {
        int resolved = index > 0 ? index - 1 : count + index;

        return resolved >= 0 && resolved < count ? resolved : -1;
    }


    /// <summary>
    /// Diffuse colour and picture per material name, out of the
    /// <c>.mtl</c> files the model names.
    ///
    /// <para>
    /// Only libraries beside the model are opened, and only by a plain
    /// relative name: a library that climbs out of the folder or names a
    /// host is a file we were not asked to read. Same rule as the glTF
    /// buffer. Pictures are looked for more widely (<see cref="MeshFile.FindTexture"/>).
    /// </para>
    /// </summary>
    private static Dictionary<string, ObjMaterial> ReadMaterials(ModelTextures textures, string? directory, List<string> libraries) {
        var materials = new Dictionary<string, ObjMaterial>(StringComparer.Ordinal);
        if (directory is null) {
            return materials;
        }

        foreach (string library in libraries) {
            if (Beside(directory, library) is not { } file) {
                continue;
            }

            string? name = null;
            foreach (var line in SharedRead.ReadLines(file)) {
                var trimmed = line.AsSpan().Trim();
                if (Keyword(trimmed, "newmtl", out var declared)) {
                    name = declared.ToString().Trim();
                    materials[name] = new ObjMaterial(null, null);
                } else if (name is null) {
                    continue;
                } else if (Keyword(trimmed, "Kd", out var kd)) {
                    var rest = kd;
                    if (TryNext(ref rest, out float r) && TryNext(ref rest, out float g) && TryNext(ref rest, out float b)) {
                        materials[name] = materials[name] with { Color = MeshColor.Clamped(r, g, b) };
                    }
                } else if (Keyword(trimmed, "map_Kd", out var map)) {
                    materials[name] = materials[name] with { Texture = textures.Named(MapFile(map)) };
                }
            }
        }

        return materials;
    }


    /// <summary>
    /// The file name of a <c>map_</c> line, past its options: <c>-bm 1.0</c>,
    /// <c>-s 1 1 1</c>, <c>-clamp on</c> take numbers or on/off, <c>-imfchan</c>
    /// and <c>-type</c> a word. The name is the rest, spaces and all.
    /// </summary>
    private static string MapFile(ReadOnlySpan<char> rest) {
        while (true) {
            rest = rest.TrimStart();
            if (rest.IsEmpty || rest[0] != '-') {
                return rest.TrimEnd().ToString();
            }

            var option = Token(ref rest);
            if (option.SequenceEqual("-imfchan") || option.SequenceEqual("-type")) {
                Token(ref rest);

                continue;
            }
            for (int i = 0; i < 3; i++) {
                var probe = rest;
                var argument = Token(ref probe);
                bool takes = float.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    || argument.Equals("on", StringComparison.OrdinalIgnoreCase)
                    || argument.Equals("off", StringComparison.OrdinalIgnoreCase);
                if (!takes) {
                    break;
                }
                rest = probe;
            }
        }
    }


    private static ReadOnlySpan<char> Token(ref ReadOnlySpan<char> rest) {
        rest = rest.TrimStart();
        int end = rest.IndexOfAny(' ', '\t');
        var token = end < 0 ? rest : rest[..end];
        rest = end < 0 ? ReadOnlySpan<char>.Empty : rest[end..];

        return token;
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


    private sealed record ObjMaterial(MeshColor? Color, MeshTexture? Texture);
}
