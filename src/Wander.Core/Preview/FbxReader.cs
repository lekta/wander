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
/// names (<c>LayerElementMaterial</c>), picture - the <c>Texture</c> wired
/// to the material's <c>DiffuseColor</c> ("OP"), its <c>Video</c>'s
/// embedded bytes or the file it names (<see cref="MeshFile.FindTexture"/>),
/// else what is around the model by the material's name
/// (<see cref="ModelTextures"/>), laid by the first <c>LayerElementUV</c>.
/// The scene is turned to Y-up when <c>GlobalSettings</c> says Z or X is up.
///
/// <para>
/// Not read: FBX 6 (geometry inside its models), NURBS and patches,
/// skinning and blend shapes - a skinned character shows in its bind pose -,
/// maps other than diffuse, a text file's embedded media, animation.
/// </para>
/// </summary>
internal static class FbxReader {
    /// <param name="textures">Where the pictures are looked for.</param>
    public static MeshData? Read(byte[] bytes, ModelTextures textures) {
        if (FbxDocument.Parse(bytes) is not { } root || root.Child("Objects") is not { } objects) {
            return null;
        }

        var geometries = new Dictionary<long, FbxNode>();
        var models = new Dictionary<long, FbxNode>();
        var materials = new Dictionary<long, MeshColor?>();
        var textureNodes = new Dictionary<long, FbxNode>();
        var names = new Dictionary<long, string>();
        var videos = new Dictionary<long, FbxNode>();
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
                    names[id] = ObjectName(node);
                    break;

                case "Texture":
                    textureNodes[id] = node;
                    break;

                case "Video":
                    videos[id] = node;
                    break;
            }
        }

        var parents = new Dictionary<long, long>();
        var placed = new List<(long Geometry, long Model)>();
        var modelMaterials = new Dictionary<long, List<long>>();
        var materialTextures = new Dictionary<long, long>();
        var textureVideos = new Dictionary<long, long>();
        foreach (var link in root.Child("Connections")?.All("C") ?? Enumerable.Empty<FbxNode>()) {
            // The other kinds ("PP", "PO") start with a property's name, not an id.
            if (link.String(0) is not ("OO" or "OP")) {
                continue;
            }

            long child = link.Long(1), parent = link.Long(2);
            if (link.String(0) == "OP") {
                if (textureNodes.ContainsKey(child) && materials.ContainsKey(parent) && link.String(3) is "DiffuseColor" or "Diffuse") {
                    materialTextures[parent] = child;
                }

                continue;
            }

            if (videos.ContainsKey(child) && textureNodes.ContainsKey(parent)) {
                textureVideos[parent] = child;
            } else if (geometries.ContainsKey(child) && models.ContainsKey(parent)) {
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

        var pictures = new Dictionary<long, MeshTexture?>();
        MeshTexture? PictureOf(long material) {
            if (!pictures.TryGetValue(material, out var picture)) {
                if (materialTextures.TryGetValue(material, out long texture)) {
                    var video = textureVideos.TryGetValue(texture, out long v) ? videos[v] : null;
                    picture = Picture(textureNodes[texture], video, textures);
                }
                pictures[material] = picture ??= textures.ForMaterial(names[material]);
            }

            return picture;
        }

        var axis = UpAxis(root);
        var worlds = new Dictionary<long, Matrix4x4>();
        var mesh = new Builder();
        foreach (var (geometry, model) in placed) {
            var place = Geometric(models[model]) * World(model, models, parents, worlds, 0) * axis;
            var looks = modelMaterials.TryGetValue(model, out var list)
                ? list.Select(m => new Look(materials[m], PictureOf(m))).ToList()
                : new List<Look>();
            mesh.Add(geometries[geometry], place, looks);
        }

        return mesh.ToMesh();
    }


    /// <summary>
    /// A texture's picture: the bytes its video embeds (binary files keep
    /// them in <c>Content</c>), else the file it names - relative name
    /// first, then the absolute one, which is usually another machine's.
    /// </summary>
    private static MeshTexture? Picture(FbxNode texture, FbxNode? video, ModelTextures textures) {
        string relative = texture.Child("RelativeFilename")?.String(0) ?? "";
        string absolute = texture.Child("FileName")?.String(0) ?? "";
        string name = Path.GetFileName(relative.Length > 0 ? relative : absolute);
        if (video?.Child("Content")?.Properties.FirstOrDefault() is byte[] { Length: > 0 } content) {
            return new MeshTexture(name, null, content);
        }

        return (relative.Length > 0 ? textures.Named(relative) : null)
            ?? (absolute.Length > 0 ? textures.Named(absolute) : null);
    }


    /// <summary>An object's name: binary files write <c>Name\0\x01Class</c>, text ones <c>Class::Name</c>.</summary>
    private static string ObjectName(FbxNode node) {
        string full = node.String(1);
        int zero = full.IndexOf('\0');
        if (zero >= 0) {
            return full[..zero];
        }

        int colons = full.IndexOf("::", StringComparison.Ordinal);

        return colons >= 0 ? full[(colons + 2)..] : full;
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


    /// <summary>
    /// Every placed mesh's points, triangles grouped by colour and picture.
    /// A mesh with a picture and UVs gets a vertex per distinct (point, UV)
    /// its corners use, V flipped (FBX counts it from the bottom); the
    /// others keep their control points, at UV zero.
    /// </summary>
    private sealed class Builder {
        private readonly List<float> _positions = new();
        private readonly List<float> _uvs = new();
        private readonly List<int> _plain = new();
        private readonly Dictionary<Look, List<int>> _looks = new();
        private int _triangles;


        public void Add(FbxNode geometry, Matrix4x4 place, List<Look> looks) {
            double[] vertices = Doubles(geometry.Child("Vertices"));
            int[] polygons = Ints(geometry.Child("PolygonVertexIndex"));
            int count = vertices.Length / 3;
            var points = new Vector3[count];
            for (int i = 0; i < count; i++) {
                points[i] = Vector3.Transform(new Vector3((float)vertices[i * 3], (float)vertices[i * 3 + 1], (float)vertices[i * 3 + 2]), place);
            }

            var uvLayer = geometry.Child("LayerElementUV");
            double[] uv = Doubles(uvLayer?.Child("UV"));
            int[] uvIndex = Ints(uvLayer?.Child("UVIndex"));
            bool uvPerCorner = uvLayer?.Child("MappingInformationType")?.String(0) == "ByPolygonVertex";
            bool uvIndexed = uvLayer?.Child("ReferenceInformationType")?.String(0) is "IndexToDirect" or "Index";
            bool textured = uv.Length >= 2 && looks.Any(l => l.Texture is not null);

            // Untextured: the control points as they are. Textured: filled
            // corner by corner below.
            int baseIndex = _positions.Count / 3;
            var corners = new Dictionary<(int Point, int Uv), int>();
            if (!textured) {
                foreach (var p in points) {
                    AddVertex(p, -1, uv);
                }
            }

            var layer = geometry.Child("LayerElementMaterial");
            bool perPolygon = layer?.Child("MappingInformationType")?.String(0) == "ByPolygon";
            int[] assigned = Ints(layer?.Child("Materials"));

            var polygon = new List<int>();
            int index = 0;
            for (int corner = 0; corner < polygons.Length; corner++) {
                int raw = polygons[corner];
                // The last corner of a polygon is stored negated, minus one.
                int point = raw < 0 ? ~raw : raw;
                if ((uint)point >= (uint)count) {
                    polygon.Add(-1);
                } else if (textured) {
                    int key = uvPerCorner ? corner : point;
                    int at = uvIndexed ? (key < uvIndex.Length ? uvIndex[key] : -1) : key;
                    if (!corners.TryGetValue((point, at), out int vertex)) {
                        vertex = _positions.Count / 3;
                        corners[(point, at)] = vertex;
                        AddVertex(points[point], at, uv);
                    }
                    polygon.Add(vertex);
                } else {
                    polygon.Add(baseIndex + point);
                }
                if (raw >= 0) {
                    continue;
                }

                int slot = perPolygon ? (index < assigned.Length ? assigned[index] : 0) : (assigned.Length > 0 ? assigned[0] : 0);
                var look = slot >= 0 && slot < looks.Count ? looks[slot] : looks.Count > 0 ? looks[0] : default;
                if (!textured) {
                    look = look with { Texture = null };
                }
                var list = look.Color is null && look.Texture is null
                    ? _plain
                    : _looks.TryGetValue(look, out var found) ? found : _looks[look] = new List<int>();
                for (int k = 2; k < polygon.Count; k++) {
                    int a = polygon[0], b = polygon[k - 1], d = polygon[k];
                    if (a < 0 || b < 0 || d < 0) {
                        continue;
                    }
                    list.Add(a);
                    list.Add(b);
                    list.Add(d);
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
            foreach (var (look, indices) in _looks) {
                parts.Add(new MeshPart(indices.ToArray(), look.Color, look.Texture));
            }
            bool textured = parts.Any(p => p.Texture is not null);

            return parts.Count > 0 ? new MeshData(_positions.ToArray(), parts, textured ? _uvs.ToArray() : null) : null;
        }


        private void AddVertex(Vector3 p, int uvAt, double[] uv) {
            _positions.Add(p.X);
            _positions.Add(p.Y);
            _positions.Add(p.Z);
            bool known = uvAt >= 0 && uvAt * 2 + 1 < uv.Length;
            _uvs.Add(known ? (float)uv[uvAt * 2] : 0);
            _uvs.Add(known ? 1 - (float)uv[uvAt * 2 + 1] : 0);
        }
    }


    /// <summary>What a material paints with: its diffuse colour and picture.</summary>
    private readonly record struct Look(MeshColor? Color, MeshTexture? Texture);
}
