using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Wander.Harness.Sandbox;

/// <summary>
/// The two halves of the <c>media</c> profile that need no encoder: a WAV,
/// which is a header and some arithmetic, and one cube in three mesh
/// formats. Everything else - mp3, flac, video, animated GIF - needs a
/// real encoder and comes from <see cref="FixtureLibrary"/> instead.
///
/// <para>
/// The cube is deliberately the same cube three times. The preview pane
/// draws all three through one <c>MeshFile</c>, so three files that should
/// look identical on screen turn "does the STL reader work" into a
/// comparison rather than a judgement.
/// </para>
/// </summary>
public static class MediaFactory {
    /// <summary>Writes the generated part of the media profile into <paramref name="dir"/>.</summary>
    public static void WriteAll(string dir) {
        Wav(Path.Combine(dir, "tone.wav"), seconds: 2, hertz: 440);
        Stl(Path.Combine(dir, "cube.stl"));
        Obj(Path.Combine(dir, "cube.obj"));
        Gltf(Path.Combine(dir, "cube.gltf"));
    }


    /// <summary>
    /// A sine tone with a LIST/INFO block, which is where a WAV keeps the
    /// tags the track card shows. Written in Windows-1251 on purpose: the
    /// specification says Latin-1, every recorder writes the machine's own
    /// codepage, and guessing between them is exactly what the reader does.
    /// </summary>
    private static void Wav(string path, int seconds, double hertz) {
        const int rate = 44100;
        const int channels = 2;
        const int bits = 16;

        int frames = rate * seconds;
        var samples = new byte[frames * channels * (bits / 8)];
        for (int i = 0; i < frames; i++) {
            short value = (short)(Math.Sin(2 * Math.PI * hertz * i / rate) * 12000);
            for (int channel = 0; channel < channels; channel++) {
                BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(((i * channels) + channel) * 2), value);
            }
        }

        byte[] info = Info(
            ("INAM", "Тональная посылка"),
            ("IART", "Wander Harness"),
            ("IPRD", "Sandbox"),
            ("ICRD", "2026"),
            ("ITRK", "1"));

        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(4 + 8 + 16 + 8 + info.Length + 8 + samples.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));

        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);                                          // PCM
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * (bits / 8));                      // bytes per second
        w.Write((short)(channels * (bits / 8)));                    // block align
        w.Write((short)bits);

        w.Write(Encoding.ASCII.GetBytes("LIST"));
        w.Write(info.Length);
        w.Write(info);

        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(samples.Length);
        w.Write(samples);
    }

    /// <summary>An INFO block: the type, then four-character fields with word-aligned, null-terminated values.</summary>
    private static byte[] Info(params (string Id, string Value)[] fields) {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        w.Write(Encoding.ASCII.GetBytes("INFO"));
        foreach (var (id, value) in fields) {
            byte[] bytes = CyrillicEncoding.Encode(value + "\0", CyrillicEncoding.Windows1251);
            w.Write(Encoding.ASCII.GetBytes(id));
            w.Write(bytes.Length);
            w.Write(bytes);
            if (bytes.Length % 2 != 0) {
                w.Write((byte)0);
            }
        }

        return buffer.ToArray();
    }


    // --- One cube, three formats ---------------------------------------

    /// <summary>Binary STL: an eighty-byte header, a triangle count, then fifty bytes per face.</summary>
    private static void Stl(string path) {
        var triangles = Cube();
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write(new byte[80]);
        w.Write(triangles.Count);
        foreach (var t in triangles) {
            w.Write(0f);
            w.Write(0f);
            w.Write(0f);                                            // the face normal, which readers recompute
            foreach (var (x, y, z) in new[] { t.A, t.B, t.C }) {
                w.Write(x);
                w.Write(y);
                w.Write(z);
            }
            w.Write((short)0);                                      // attribute byte count
        }
    }

    /// <summary>Wavefront OBJ: eight vertices and twelve faces, one-based like the format wants.</summary>
    private static void Obj(string path) {
        var text = new StringBuilder("# Wander harness cube\r\n");
        foreach (var (x, y, z) in Corners()) {
            text.Append(FormattableString.Invariant($"v {x} {y} {z}\r\n"));
        }
        foreach (var (a, b, c) in Faces()) {
            text.Append(FormattableString.Invariant($"f {a + 1} {b + 1} {c + 1}\r\n"));
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// glTF 2.0 with the buffer inlined as a data URI, so the file stands
    /// on its own. No <c>nodes</c> array: without one the reader places
    /// every mesh at the origin, which is the whole scene here.
    /// </summary>
    private static void Gltf(string path) {
        var corners = Corners();
        var faces = Faces();

        var buffer = new byte[(corners.Count * 12) + (faces.Count * 3 * 2)];
        int at = 0;
        foreach (var (x, y, z) in corners) {
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(at), x);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(at + 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(at + 8), z);
            at += 12;
        }
        int indexOffset = at;
        foreach (var (a, b, c) in faces) {
            foreach (int index in new[] { a, b, c }) {
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at), (ushort)index);
                at += 2;
            }
        }

        int indexCount = faces.Count * 3;
        string json =
            "{\n" +
            "  \"asset\": { \"version\": \"2.0\", \"generator\": \"Wander harness\" },\n" +
            $"  \"buffers\": [ {{ \"byteLength\": {buffer.Length}, " +
            $"\"uri\": \"data:application/octet-stream;base64,{Convert.ToBase64String(buffer)}\" }} ],\n" +
            "  \"bufferViews\": [\n" +
            $"    {{ \"buffer\": 0, \"byteOffset\": 0, \"byteLength\": {indexOffset} }},\n" +
            $"    {{ \"buffer\": 0, \"byteOffset\": {indexOffset}, \"byteLength\": {indexCount * 2} }}\n" +
            "  ],\n" +
            "  \"accessors\": [\n" +
            $"    {{ \"bufferView\": 0, \"componentType\": 5126, \"count\": {corners.Count}, \"type\": \"VEC3\" }},\n" +
            $"    {{ \"bufferView\": 1, \"componentType\": 5123, \"count\": {indexCount}, \"type\": \"SCALAR\" }}\n" +
            "  ],\n" +
            "  \"meshes\": [ { \"primitives\": [ " +
            "{ \"attributes\": { \"POSITION\": 0 }, \"indices\": 1, \"mode\": 4, \"material\": 0 } ] } ],\n" +
            "  \"materials\": [ { \"pbrMetallicRoughness\": { \"baseColorFactor\": [ 0.35, 0.6, 0.85, 1.0 ] } } ]\n" +
            "}\n";
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }


    private static List<(float X, float Y, float Z)> Corners() {
        var corners = new List<(float, float, float)>();
        foreach (int z in new[] { -1, 1 }) {
            foreach (int y in new[] { -1, 1 }) {
                foreach (int x in new[] { -1, 1 }) {
                    corners.Add((x * 10f, y * 10f, z * 10f));
                }
            }
        }

        return corners;
    }

    /// <summary>
    /// The twelve faces, as indices into <see cref="Corners"/>: the corner
    /// at index <c>i</c> is <c>(x, y, z)</c> with bit 0 for x, bit 1 for y,
    /// bit 2 for z, so a face is two triangles over one fixed bit.
    /// </summary>
    private static List<(int A, int B, int C)> Faces() {
        return new List<(int, int, int)> {
            (0, 2, 3), (0, 3, 1),                                   // z = -1
            (4, 5, 7), (4, 7, 6),                                   // z = +1
            (0, 1, 5), (0, 5, 4),                                   // y = -1
            (2, 6, 7), (2, 7, 3),                                   // y = +1
            (0, 4, 6), (0, 6, 2),                                   // x = -1
            (1, 3, 7), (1, 7, 5),                                   // x = +1
        };
    }

    private static List<Triangle> Cube() {
        var corners = Corners();

        return Faces()
            .Select(f => new Triangle(corners[f.A], corners[f.B], corners[f.C]))
            .ToList();
    }


    private readonly record struct Triangle(
        (float X, float Y, float Z) A,
        (float X, float Y, float Z) B,
        (float X, float Y, float Z) C);
}
