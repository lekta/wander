using System.Text.RegularExpressions;
using Wander.Core.FileSystem;

namespace Wander.Core.Preview;

/// <summary>
/// Where one model's pictures are, when the model does not say. Files travel
/// without their <c>.mtl</c>, exporters leave the texture out of an FBX, and a
/// game project keeps the link in its own material - yet the picture usually
/// lies nearby under a telling name. Asked by material name
/// (<see cref="ForMaterial"/>): a Unity material of that name first - exact,
/// it names the picture by guid - then the best name among the pictures
/// around the model. A wrong picture is worse than none, so a name has to
/// match: nothing that shares no word with the material or the model is taken.
/// The folder is listed once per model, on the first question.
/// </summary>
internal sealed partial class ModelTextures {
    /// <summary>The most files looked at in one folder: a model in a dump of thousands is not worth the wait.</summary>
    private const int MaxFilesPerFolder = 2000;

    private const long MaxMaterialSize = 1024 * 1024;

    private static readonly IReadOnlySet<string> _pictureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tif", ".tiff", ".webp",
    };

    /// <summary>Words that say a picture is the colour, best first.</summary>
    private static readonly string[] _colourWords = {
        "basecolor", "basecolour", "basemap", "albedo", "diffuse", "color", "colour", "col", "diff", "alb", "bc", "d",
    };

    /// <summary>Words that say a picture is anything but the colour.</summary>
    private static readonly IReadOnlySet<string> _otherMapWords = new HashSet<string> {
        "normal", "normals", "nor", "nrm", "norm", "n", "bump", "height", "disp", "displacement",
        "rough", "roughness", "gloss", "glossiness", "metal", "metallic", "metalness", "spec", "specular",
        "ao", "ambient", "ambiant", "occlusion", "mask", "emissive", "emission", "opacity", "alpha", "transparency",
        "orm", "mra", "rma", "arm", "lightmap", "shadow", "curvature", "thickness", "cavity",
    };

    /// <summary>Words that tell nothing about which model a picture is for.</summary>
    private static readonly IReadOnlySet<string> _plainWords = new HashSet<string> {
        "base", "map", "maps", "tex", "texture", "textures", "material", "materials", "mat", "mtl", "default",
        "low", "high", "lp", "hp", "poly", "model", "mesh", "obj", "fbx", "final", "baked", "new", "the",
    };

    /// <summary>Unity's base colour slots: URP / HDRP, then the built-in pipeline.</summary>
    private static readonly string[] _unitySlots = { "_BaseMap", "_BaseColorMap", "_MainTex" };

    private readonly string? _directory;
    private readonly HashSet<string> _modelWords;
    private List<string>? _pictures;


    public ModelTextures(string modelPath) {
        _directory = Path.GetDirectoryName(modelPath);
        _modelWords = Words(Path.GetFileNameWithoutExtension(modelPath));
    }


    /// <summary>The picture the model names itself (<see cref="MeshFile.FindTexture"/>).</summary>
    public MeshTexture? Named(string reference) {
        return MeshFile.FindTexture(_directory, reference);
    }


    /// <summary>The picture for a material the model gives none for, or null.</summary>
    public MeshTexture? ForMaterial(string? material) {
        if (_directory is null) {
            return null;
        }

        try {
            string? file = (material is null ? null : FromUnityMaterial(material)) ?? BestByName(material);

            return file is null ? null : new MeshTexture(Path.GetFileName(file), file, null);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // The model is still a model without its picture.
            return null;
        }
    }


    /// <summary>Whether a file name reads as something other than a colour map - the normal map a shader node also holds.</summary>
    public static bool IsOtherMap(string fileName) {
        return Tokens(Path.GetFileNameWithoutExtension(fileName)).Any(_otherMapWords.Contains);
    }


    // --- Unity ---

    /// <summary>
    /// <c>Name.mat</c> beside the model or in a <c>Materials</c> folder: its
    /// base colour slot holds a guid, and the picture is the one whose
    /// <c>.meta</c> states that guid.
    /// </summary>
    private string? FromUnityMaterial(string material) {
        if (material.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
            return null;
        }

        foreach (string folder in new[] { "", "Materials", Path.Combine("..", "Materials") }) {
            var info = new FileInfo(Path.Combine(_directory!, folder, material + ".mat"));
            if (!info.Exists || info.Length > MaxMaterialSize) {
                continue;
            }

            string text = SharedRead.ReadAllText(info.FullName);
            foreach (string slot in _unitySlots) {
                var match = Regex.Match(text, Regex.Escape(slot) + @":\s*m_Texture:\s*\{[^}]*guid:\s*([0-9a-f]{32})");
                if (match.Success && PictureByGuid(match.Groups[1].Value) is { } found) {
                    return found;
                }
            }
        }

        return null;
    }


    private string? PictureByGuid(string guid) {
        foreach (string picture in Pictures()) {
            string meta = picture + ".meta";
            if (File.Exists(meta) && SharedRead.ReadLines(meta).Take(3).Any(l => l.Contains(guid, StringComparison.Ordinal))) {
                return picture;
            }
        }

        return null;
    }


    // --- Names ---

    /// <summary>
    /// The picture whose name fits best. Its words, without those of its role
    /// and the plain ones, are compared with the material's and the model's:
    /// the same words win (the material's over the model's), then at least half
    /// of them in common. A colour map of no other words counts only when it is
    /// the only one around: one folder, one model. A picture with no colour
    /// word has to match outright - it may be anything - and to lie with the
    /// model: in the folder above, <c>glove.png</c> beside <c>parts\Glove.blend</c>
    /// is as likely a reference sheet as a texture.
    /// </summary>
    private string? BestByName(string? material) {
        var materialWords = material is null ? new HashSet<string>() : Words(material);
        var known = new HashSet<string>(materialWords);
        known.UnionWith(_modelWords);
        string above = Path.GetFullPath(Path.Combine(_directory!, ".."));

        string? best = null, nameless = null;
        double bestScore = 0;
        int bestRole = int.MaxValue, namelessCount = 0;
        foreach (string picture in Pictures()) {
            var tokens = Tokens(Path.GetFileNameWithoutExtension(picture)).ToList();
            if (tokens.Any(_otherMapWords.Contains)) {
                continue;
            }

            int role = tokens.Select(t => Array.IndexOf(_colourWords, t)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
            if (role < 0 && string.Equals(Path.GetDirectoryName(picture), above, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var rest = new HashSet<string>(tokens.Where(t => !_colourWords.Contains(t) && Telling(t)));
            if (rest.Count == 0) {
                if (role >= 0) {
                    nameless = picture;
                    namelessCount++;
                }

                continue;
            }

            double score;
            if (rest.SetEquals(materialWords)) {
                score = 3;
            } else if (rest.SetEquals(_modelWords)) {
                score = 2;
            } else {
                int shared = rest.Count(known.Contains);
                score = role >= 0 && shared * 2 >= rest.Count ? (double)shared / rest.Count : 0;
            }
            if (score <= 0) {
                continue;
            }

            int rank = role < 0 ? _colourWords.Length : role;
            if (score > bestScore || (score == bestScore && rank < bestRole)) {
                (best, bestScore, bestRole) = (picture, score, rank);
            }
        }

        return best ?? (namelessCount == 1 ? nameless : null);
    }


    /// <summary>Every picture around the model: beside it, in its <c>textures</c> folder, in one beside its folder, in the folder above.</summary>
    private List<string> Pictures() {
        if (_pictures is not null) {
            return _pictures;
        }

        _pictures = new List<string>();
        foreach (string folder in new[] { "", "textures", Path.Combine("..", "textures"), ".." }) {
            try {
                string full = Path.GetFullPath(Path.Combine(_directory!, folder));
                if (Directory.Exists(full)) {
                    _pictures.AddRange(Directory.EnumerateFiles(full)
                        .Take(MaxFilesPerFolder)
                        .Where(f => _pictureExtensions.Contains(Path.GetExtension(f)))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                }
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
                // A folder we cannot list has nothing to offer; the others still may.
            }
        }

        return _pictures;
    }


    private static HashSet<string> Words(string name) {
        return new HashSet<string>(Tokens(name).Where(Telling));
    }


    private static bool Telling(string word) {
        return !_plainWords.Contains(word) && !char.IsDigit(word[0]);
    }


    /// <summary>A name's words, lower case: split at anything but letters and digits and where the case changes (<c>HeroHead_Albedo</c>).</summary>
    private static IEnumerable<string> Tokens(string name) {
        foreach (Match match in WordPattern().Matches(name)) {
            yield return match.Value.ToLowerInvariant();
        }
    }


    [GeneratedRegex(@"\p{Lu}+(?!\p{Ll})|\p{Lu}?\p{Ll}+|\d+")]
    private static partial Regex WordPattern();
}
