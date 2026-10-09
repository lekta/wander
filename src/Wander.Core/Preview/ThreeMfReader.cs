using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace Wander.Core.Preview;

/// <summary>
/// 3MF - the 3D-printing package slicers save projects in: a zip whose
/// model part (<c>3D/3dmodel.model</c>, or wherever the package's
/// relationships point) lists objects - a mesh of vertices and triangles,
/// or components placing other objects by a transform - and a build of
/// items placing objects on the plate. The production extension lets a
/// component name an object in another model part (<c>p:path</c>), which
/// is how Bambu Studio and Orca write every project; those parts are read
/// as they are referred to.
///
/// <para>
/// Colour comes from base materials (<c>basematerials</c>), by the
/// triangle's own property or its object's default. Textures, colour
/// groups and slicer paint are not read.
/// </para>
/// </summary>
internal static class ThreeMfReader {
    /// <summary>The relationship type of a package's start part.</summary>
    private const string ModelRelationship = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";

    private const string DefaultModel = "3D/3dmodel.model";

    /// <summary>Components inside components past this are a loop or a joke.</summary>
    private const int MaxDepth = 16;

    private static readonly XmlReaderSettings _xml = new() {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        CloseInput = true,
    };


    public static MeshData? Read(Stream stream) {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        string root = PartName(StartPart(zip) ?? DefaultModel);
        var models = new Dictionary<string, Model?>(StringComparer.OrdinalIgnoreCase);
        if (Load(zip, root, models) is not { } model) {
            return null;
        }

        var mesh = new Builder();
        foreach (var item in model.Build) {
            Emit(zip, models, item.Path ?? root, item.ObjectId, item.Transform, mesh, 0);
        }

        return mesh.ToMesh();
    }


    /// <summary>One object placed by <paramref name="transform"/>: its triangles, or its components in turn.</summary>
    private static void Emit(ZipArchive zip, Dictionary<string, Model?> models, string part, string id, double[] transform, Builder mesh, int depth) {
        if (depth > MaxDepth || Load(zip, part, models) is not { } model || !model.Objects.TryGetValue(id, out var obj)) {
            return;
        }

        if (obj.Positions.Count > 0) {
            mesh.Add(obj, transform);

            return;
        }
        foreach (var component in obj.Components) {
            Emit(zip, models, component.Path ?? part, component.ObjectId, Combine(component.Transform, transform), mesh, depth + 1);
        }
    }


    private static Model? Load(ZipArchive zip, string part, Dictionary<string, Model?> models) {
        if (models.TryGetValue(part, out var known)) {
            return known;
        }

        Model? model = zip.GetEntry(part) is { } entry ? Parse(entry) : null;
        models[part] = model;

        return model;
    }


    private static Model Parse(ZipArchiveEntry entry) {
        var model = new Model();
        var materials = new Dictionary<string, List<MeshColor>>(StringComparer.Ordinal);
        List<MeshColor>? group = null;
        ObjectData? current = null;
        using var reader = XmlReader.Create(entry.Open(), _xml);
        while (reader.Read()) {
            if (reader.NodeType == XmlNodeType.EndElement) {
                if (reader.LocalName == "object") {
                    current = null;
                } else if (reader.LocalName == "basematerials") {
                    group = null;
                }

                continue;
            }
            if (reader.NodeType != XmlNodeType.Element) {
                continue;
            }

            switch (reader.LocalName) {
                case "basematerials":
                    group = new List<MeshColor>();
                    materials[reader.GetAttribute("id") ?? ""] = group;
                    if (reader.IsEmptyElement) {
                        group = null;
                    }
                    break;

                case "base" when group is not null:
                    group.Add(Color(reader.GetAttribute("displaycolor")) ?? new MeshColor(0.76f, 0.76f, 0.76f));
                    break;

                case "object":
                    current = new ObjectData(Material(materials, reader.GetAttribute("pid"), reader.GetAttribute("pindex")));
                    model.Objects[reader.GetAttribute("id") ?? ""] = current;
                    if (reader.IsEmptyElement) {
                        current = null;
                    }
                    break;

                case "vertex" when current is not null:
                    current.Positions.Add(Float(reader, "x"));
                    current.Positions.Add(Float(reader, "y"));
                    current.Positions.Add(Float(reader, "z"));
                    break;

                case "triangle" when current is not null:
                    current.Indices.Add(Int(reader, "v1"));
                    current.Indices.Add(Int(reader, "v2"));
                    current.Indices.Add(Int(reader, "v3"));
                    current.Colors.Add(reader.GetAttribute("pid") is { } pid
                        ? Material(materials, pid, reader.GetAttribute("p1")) ?? current.Color
                        : current.Color);
                    if (current.Colors.Count > MeshFile.MaxTriangles) {
                        throw new FormatException("3MF object past the triangle limit");
                    }
                    break;

                case "component" when current is not null:
                    current.Components.Add(new Placement(
                        reader.GetAttribute("objectid") ?? "", PathAttribute(reader), Transform(reader.GetAttribute("transform"))));
                    break;

                case "item":
                    model.Build.Add(new Placement(
                        reader.GetAttribute("objectid") ?? "", PathAttribute(reader), Transform(reader.GetAttribute("transform"))));
                    break;
            }
        }

        return model;
    }


    /// <summary>The package's start part, by the relationship of the 3MF model type.</summary>
    private static string? StartPart(ZipArchive zip) {
        if (zip.GetEntry("_rels/.rels") is not { } rels) {
            return null;
        }

        using var reader = XmlReader.Create(rels.Open(), _xml);
        while (reader.Read()) {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship"
                && reader.GetAttribute("Type") == ModelRelationship) {
                return reader.GetAttribute("Target");
            }
        }

        return null;
    }


    /// <summary>A part name as the zip holds it: no leading slash.</summary>
    private static string PartName(string target) {
        return target.TrimStart('/');
    }


    private static string? PathAttribute(XmlReader reader) {
        for (int i = 0; i < reader.AttributeCount; i++) {
            reader.MoveToAttribute(i);
            if (reader.LocalName == "path") {
                string value = PartName(reader.Value);
                reader.MoveToElement();

                return value;
            }
        }
        reader.MoveToElement();

        return null;
    }


    private static MeshColor? Material(Dictionary<string, List<MeshColor>> materials, string? pid, string? index) {
        return pid is not null && materials.TryGetValue(pid, out var group)
            && int.TryParse(index ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < group.Count
            ? group[i]
            : null;
    }


    /// <summary>"#RRGGBB" or "#RRGGBBAA".</summary>
    private static MeshColor? Color(string? value) {
        if (value is not { Length: 7 or 9 } || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) {
            return null;
        }

        return MeshColor.Clamped(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
    }


    private static float Float(XmlReader reader, string name) {
        return float.Parse(reader.GetAttribute(name) ?? throw new FormatException($"3MF {name} missing"), NumberStyles.Float, CultureInfo.InvariantCulture);
    }


    private static int Int(XmlReader reader, string name) {
        return int.Parse(reader.GetAttribute(name) ?? throw new FormatException($"3MF {name} missing"), NumberStyles.Integer, CultureInfo.InvariantCulture);
    }


    /// <summary>
    /// "m00 m01 m02 m10 m11 m12 m20 m21 m22 m30 m31 m32": rows of a 4x3
    /// matrix a point (as a row) is multiplied by; identity when absent.
    /// </summary>
    private static double[] Transform(string? value) {
        if (value is null) {
            return Identity();
        }

        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 12) {
            return Identity();
        }

        var m = new double[12];
        for (int i = 0; i < 12; i++) {
            m[i] = double.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return m;
    }


    private static double[] Identity() {
        return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
    }


    /// <summary><paramref name="first"/>, then <paramref name="then"/>: a component's place inside its parent's.</summary>
    private static double[] Combine(double[] first, double[] then) {
        var m = new double[12];
        for (int row = 0; row < 4; row++) {
            for (int col = 0; col < 3; col++) {
                double sum = row == 3 ? then[9 + col] : 0;
                for (int k = 0; k < 3; k++) {
                    sum += first[row * 3 + k] * then[k * 3 + col];
                }
                m[row * 3 + col] = sum;
            }
        }

        return m;
    }


    private sealed class Model {
        public Dictionary<string, ObjectData> Objects { get; } = new(StringComparer.Ordinal);

        public List<Placement> Build { get; } = new();
    }


    private sealed class ObjectData {
        public ObjectData(MeshColor? color) {
            Color = color;
        }


        public MeshColor? Color { get; }

        public List<float> Positions { get; } = new();

        public List<int> Indices { get; } = new();

        /// <summary>A colour per triangle, in step with <see cref="Indices"/> by threes.</summary>
        public List<MeshColor?> Colors { get; } = new();

        public List<Placement> Components { get; } = new();
    }


    private sealed record Placement(string ObjectId, string? Path, double[] Transform);


    /// <summary>The model being assembled: every placed object's points, triangles grouped by colour.</summary>
    private sealed class Builder {
        private readonly List<float> _positions = new();
        private readonly List<int> _plain = new();
        private readonly Dictionary<MeshColor, List<int>> _coloured = new();
        private int _triangles;


        public void Add(ObjectData obj, double[] m) {
            int vertices = obj.Positions.Count / 3;
            int baseIndex = _positions.Count / 3;
            for (int i = 0; i + 2 < obj.Positions.Count; i += 3) {
                double x = obj.Positions[i], y = obj.Positions[i + 1], z = obj.Positions[i + 2];
                _positions.Add((float)(x * m[0] + y * m[3] + z * m[6] + m[9]));
                _positions.Add((float)(x * m[1] + y * m[4] + z * m[7] + m[10]));
                _positions.Add((float)(x * m[2] + y * m[5] + z * m[8] + m[11]));
            }
            for (int t = 0; t * 3 + 2 < obj.Indices.Count; t++) {
                int a = obj.Indices[t * 3], b = obj.Indices[t * 3 + 1], c = obj.Indices[t * 3 + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices || b >= vertices || c >= vertices) {
                    continue;
                }

                var list = obj.Colors[t] is { } color
                    ? _coloured.TryGetValue(color, out var found) ? found : _coloured[color] = new List<int>()
                    : _plain;
                list.Add(baseIndex + a);
                list.Add(baseIndex + b);
                list.Add(baseIndex + c);
                if (++_triangles > MeshFile.MaxTriangles) {
                    throw new FormatException("3MF build past the triangle limit");
                }
            }
        }


        public MeshData? ToMesh() {
            var parts = new List<MeshPart>();
            if (_plain.Count > 0) {
                parts.Add(new MeshPart(_plain.ToArray(), null));
            }
            foreach (var (color, indices) in _coloured) {
                parts.Add(new MeshPart(indices.ToArray(), color));
            }

            return parts.Count > 0 ? new MeshData(_positions.ToArray(), parts) : null;
        }
    }
}
