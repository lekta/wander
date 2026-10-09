using System.Numerics;

namespace Wander.Core.Preview;

/// <summary>
/// FBX 7 (binary or text, <see cref="FbxDocument"/>) as a mesh: every
/// <c>Geometry</c> of type Mesh, placed by each <c>Model</c> it is
/// connected to (<c>Connections</c>, "OO"), through the model's chain of
/// parents. A model's place is FBX's own formula - translation, rotation
/// offset and pivot, pre-rotation, rotation in its Euler order,
/// post-rotation, scaling offset and pivot, scaling - and the geometric
/// transform under it, which children do not inherit. Polygons are fanned
/// into triangles; colour is the diffuse of the material each polygon
/// names (<c>LayerElementMaterial</c>). The scene is turned to Y-up when
/// <c>GlobalSettings</c> says Z or X is up.
///
/// <para>
/// Not read: FBX 6 (geometry inside its models), NURBS and patches,
/// skinning and blend shapes - a skinned character shows in its bind pose -,
/// textures, animation.
/// </para>
/// </summary>
internal static class FbxReader {
    public static MeshData? Read(byte[] bytes) {
        if (FbxDocument.Parse(bytes) is not { } root || root.Child("Objects") is not { } objects) {
            return null;
        }

        var geometries = new Dictionary<long, FbxNode>();
        var models = new Dictionary<long, FbxNode>();
        var materials = new Dictionary<long, MeshColor?>();
        foreach (var node in objects.Children) {
            long id = node.Long(0);
            switch (node.Name) {
                case "Geometry" when node.String(2) == "Mesh":
                    geometries[id] = node;
                    break;

                case "Model":
                    models[id] = node;
                    break;

                case "Material":
                    materials[id] = Diffuse(node);
                    break;
            }
        }

        var parents = new Dictionary<long, long>();
        var placed = new List<(long Geometry, long Model)>();
        var modelMaterials = new Dictionary<long, List<long>>();
        foreach (var link in root.Child("Connections")?.All("C") ?? Enumerable.Empty<FbxNode>()) {
            if (link.String(0) != "OO") {
                continue;
            }

            long child = link.Long(1), parent = link.Long(2);
            if (geometries.ContainsKey(child) && models.ContainsKey(parent)) {
                placed.Add((child, parent));
            } else if (models.ContainsKey(child)) {
                parents[child] = parent;
            } else if (materials.ContainsKey(child) && models.ContainsKey(parent)) {
                if (!modelMaterials.TryGetValue(parent, out var list)) {
                    modelMaterials[parent] = list = new List<long>();
                }
                list.Add(child);
            }
        }

        var axis = UpAxis(root);
        var worlds = new Dictionary<long, Matrix4x4>();
        var mesh = new Builder();
        foreach (var (geometry, model) in placed) {
            var place = Geometric(models[model]) * World(model, models, parents, worlds, 0) * axis;
            var colours = modelMaterials.TryGetValue(model, out var list)
                ? list.Select(m => materials[m]).ToList()
                : new List<MeshColor?>();
            mesh.Add(geometries[geometry], place, colours);
        }

        return mesh.ToMesh();
    }


    /// <summary>Where a model stands: its own transform, then its parent's - row vectors, so local first.</summary>
    private static Matrix4x4 World(long id, Dictionary<long, FbxNode> models, Dictionary<long, long> parents, Dictionary<long, Matrix4x4> worlds, int depth) {
        if (worlds.TryGetValue(id, out var known)) {
            return known;
        }

        var local = Local(models[id]);
        var world = depth < 64 && parents.TryGetValue(id, out long parent) && models.ContainsKey(parent)
            ? local * World(parent, models, parents, worlds, depth + 1)
            : local;
        worlds[id] = world;

        return world;
    }


    /// <summary>
    /// FBX's model transform, in column form
    /// <c>T Roff Rp Rpre R Rpost^-1 Rp^-1 Soff Sp S Sp^-1</c>, written here for
    /// row vectors - the same product read from the other end.
    /// </summary>
    private static Matrix4x4 Local(FbxNode model) {
        var scalingPivot = Matrix4x4.CreateTranslation(Vector(model, "ScalingPivot", Vector3.Zero));
        var rotationPivot = Matrix4x4.CreateTranslation(Vector(model, "RotationPivot", Vector3.Zero));
        Matrix4x4.Invert(scalingPivot, out var scalingPivotBack);
        Matrix4x4.Invert(rotationPivot, out var rotationPivotBack);
        Matrix4x4.Invert(Euler(Vector(model, "PostRotation", Vector3.Zero), 0), out var postBack);
        int order = Int(model, "RotationOrder", 0);

        return scalingPivotBack
            * Matrix4x4.CreateScale(Vector(model, "Lcl Scaling", Vector3.One))
            * scalingPivot
            * Matrix4x4.CreateTranslation(Vector(model, "ScalingOffset", Vector3.Zero))
            * rotationPivotBack
            * postBack
            * Euler(Vector(model, "Lcl Rotation", Vector3.Zero), order)
            * Euler(Vector(model, "PreRotation", Vector3.Zero), 0)
            * rotationPivot
            * Matrix4x4.CreateTranslation(Vector(model, "RotationOffset", Vector3.Zero))
            * Matrix4x4.CreateTranslation(Vector(model, "Lcl Translation", Vector3.Zero));
    }


    /// <summary>The geometric transform: the mesh's offset inside its model, not passed to children.</summary>
    private static Matrix4x4 Geometric(FbxNode model) {
        return Matrix4x4.CreateScale(Vector(model, "GeometricScaling", Vector3.One))
            * Euler(Vector(model, "GeometricRotation", Vector3.Zero), 0)
            * Matrix4x4.CreateTranslation(Vector(model, "GeometricTranslation", Vector3.Zero));
    }


    /// <summary>Euler angles in degrees, applied in FBX's order: 0 XYZ, 1 XZY, 2 YZX, 3 YXZ, 4 ZXY, 5 ZYX.</summary>
    private static Matrix4x4 Euler(Vector3 degrees, int order) {
        var x = Matrix4x4.CreateRotationX(degrees.X * MathF.PI / 180);
        var y = Matrix4x4.CreateRotationY(degrees.Y * MathF.PI / 180);
        var z = Matrix4x4.CreateRotationZ(degrees.Z * MathF.PI / 180);

        return order switch {
            1 => x * z * y,
            2 => y * z * x,
            3 => y * x * z,
            4 => z * x * y,
            5 => z * y * x,
            _ => x * y * z,
        };
    }


    /// <summary>The turn that brings the file's up axis to Y.</summary>
    private static Matrix4x4 UpAxis(FbxNode root) {
        if (root.Child("GlobalSettings") is not { } settings) {
            return Matrix4x4.Identity;
        }

        return Int(settings, "UpAxis", 1) switch {
            2 => Matrix4x4.CreateRotationX(-MathF.PI / 2),
            0 => Matrix4x4.CreateRotationZ(MathF.PI / 2),
            _ => Matrix4x4.Identity,
        };
    }


    /// <summary>A material's diffuse colour - <c>DiffuseColor</c>, or <c>Diffuse</c> in older files.</summary>
    private static MeshColor? Diffuse(FbxNode material) {
        var p = Property(material, "DiffuseColor") ?? Property(material, "Diffuse");

        return p is { Properties.Count: >= 7 } ? MeshColor.Clamped(p.Double(4), p.Double(5), p.Double(6)) : null;
    }


    private static FbxNode? Property(FbxNode node, string name) {
        return node.Child("Properties70")?.All("P").FirstOrDefault(p => p.String(0) == name);
    }

    private static Vector3 Vector(FbxNode node, string name, Vector3 fallback) {
        return Property(node, name) is { Properties.Count: >= 7 } p
            ? new Vector3((float)p.Double(4), (float)p.Double(5), (float)p.Double(6))
            : fallback;
    }

    private static int Int(FbxNode node, string name, int fallback) {
        return Property(node, name) is { Properties.Count: >= 5 } p ? (int)p.Long(4) : fallback;
    }


    /// <summary>An array property as doubles, whichever type the file stored it in.</summary>
    private static double[] Doubles(FbxNode? node) {
        return node?.Properties.FirstOrDefault() switch {
            double[] d => d,
            int[] i => i.Select(v => (double)v).ToArray(),
            long[] l => l.Select(v => (double)v).ToArray(),
            _ => Array.Empty<double>(),
        };
    }

    private static int[] Ints(FbxNode? node) {
        return node?.Properties.FirstOrDefault() switch {
            int[] i => i,
            long[] l => l.Select(v => (int)v).ToArray(),
            double[] d => d.Select(v => (int)v).ToArray(),
            _ => Array.Empty<int>(),
        };
    }


    /// <summary>Every placed mesh's points, triangles grouped by colour.</summary>
    private sealed class Builder {
        private readonly List<float> _positions = new();
        private readonly List<int> _plain = new();
        private readonly Dictionary<MeshColor, List<int>> _coloured = new();
        private int _triangles;


        public void Add(FbxNode geometry, Matrix4x4 place, List<MeshColor?> colours) {
            double[] vertices = Doubles(geometry.Child("Vertices"));
            int[] polygons = Ints(geometry.Child("PolygonVertexIndex"));
            int count = vertices.Length / 3;
            int baseIndex = _positions.Count / 3;
            for (int i = 0; i < count; i++) {
                var p = Vector3.Transform(new Vector3((float)vertices[i * 3], (float)vertices[i * 3 + 1], (float)vertices[i * 3 + 2]), place);
                _positions.Add(p.X);
                _positions.Add(p.Y);
                _positions.Add(p.Z);
            }

            var layer = geometry.Child("LayerElementMaterial");
            bool perPolygon = layer?.Child("MappingInformationType")?.String(0) == "ByPolygon";
            int[] assigned = Ints(layer?.Child("Materials"));

            var polygon = new List<int>();
            int index = 0;
            foreach (int raw in polygons) {
                // The last corner of a polygon is stored negated, minus one.
                polygon.Add(raw < 0 ? ~raw : raw);
                if (raw >= 0) {
                    continue;
                }

                int slot = perPolygon ? (index < assigned.Length ? assigned[index] : 0) : (assigned.Length > 0 ? assigned[0] : 0);
                MeshColor? colour = slot >= 0 && slot < colours.Count ? colours[slot] : colours.Count > 0 ? colours[0] : null;
                var list = colour is { } c
                    ? _coloured.TryGetValue(c, out var found) ? found : _coloured[c] = new List<int>()
                    : _plain;
                for (int k = 2; k < polygon.Count; k++) {
                    int a = polygon[0], b = polygon[k - 1], d = polygon[k];
                    if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)d >= (uint)count) {
                        continue;
                    }
                    list.Add(baseIndex + a);
                    list.Add(baseIndex + b);
                    list.Add(baseIndex + d);
                    if (++_triangles > MeshFile.MaxTriangles) {
                        throw new FormatException("FBX past the triangle limit");
                    }
                }
                polygon.Clear();
                index++;
            }
        }


        public MeshData? ToMesh() {
            var parts = new List<MeshPart>();
            if (_plain.Count > 0) {
                parts.Add(new MeshPart(_plain.ToArray(), null));
            }
            foreach (var (colour, indices) in _coloured) {
                parts.Add(new MeshPart(indices.ToArray(), colour));
            }

            return parts.Count > 0 ? new MeshData(_positions.ToArray(), parts) : null;
        }
    }
}
