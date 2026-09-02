using System.Diagnostics;
using System.IO;
using System.Text;

namespace Wander.Harness.Sandbox;

public sealed record SandboxOptions(int Photos, int Big, int RawCount, int RawMb) {
    public static SandboxOptions From(Options options) {
        return new SandboxOptions(
            options.Int("photos", 120),
            options.Int("big", 5000),
            options.Int("raw", 4),
            options.Int("raw-mb", 25));
    }
}


public sealed record BuiltSandbox(string Root, IReadOnlyList<string> Summary);


/// <summary>
/// Generates the folders the scenarios run against. Profiles are named so a
/// scenario can ask for exactly the data it needs; adding one is a line in
/// <see cref="_profiles"/>. Nothing here touches anything outside
/// <c>root</c>; junctions are created with <c>mklink</c> and removed as
/// junctions (never recursed into) by <see cref="Remove"/>.
/// </summary>
public static class SandboxBuilder {
    private const int PhotoWidth = 1620;
    private const int PhotoHeight = 1080;

    private static readonly Dictionary<string, Action<SandboxContext>> _profiles = new(StringComparer.OrdinalIgnoreCase) {
        ["photos"] = Photos,
        ["raw"] = Raw,
        ["big"] = Big,
        ["deep"] = Deep,
        ["names"] = Names,
    };


    public static IEnumerable<string> KnownProfiles => _profiles.Keys;


    public static BuiltSandbox Build(string root, IEnumerable<string> profiles, SandboxOptions options) {
        Directory.CreateDirectory(root);
        var context = new SandboxContext(root, options);
        foreach (string profile in profiles) {
            if (!_profiles.TryGetValue(profile.Trim(), out var build)) {
                throw new ArgumentException($"unknown sandbox profile '{profile}'; known: {string.Join(", ", KnownProfiles)}");
            }
            var clock = Stopwatch.StartNew();
            build(context);
            context.Note($"{profile}: {clock.ElapsedMilliseconds} ms");
        }

        return new BuiltSandbox(root, context.Summary);
    }

    /// <summary>Deletes a sandbox. Junctions go first and as junctions, so nothing they point at is touched.</summary>
    public static void Remove(string root) {
        if (!Directory.Exists(root)) {
            return;
        }
        foreach (var dir in new DirectoryInfo(root).EnumerateDirectories("*", SearchOption.AllDirectories).ToList()) {
            if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                dir.Delete();
            }
        }
        Directory.Delete(root, recursive: true);
    }


    // --- Profiles ------------------------------------------------------

    private static void Photos(SandboxContext c) {
        string dir = c.Dir("photos");
        for (int i = 1; i <= c.Options.Photos; i++) {
            string name = $"IMG_{i:0000}";
            int orientation = RawFiles.OrientationFor(i);
            PictureFactory.SaveJpeg(Path.Combine(dir, name + ".jpg"), PhotoWidth, PhotoHeight, orientation, name, seed: i);
            if (i % 3 == 0) {
                File.WriteAllText(Path.Combine(dir, name + ".xmp"), Xmp(i % 5 + 1, i % 2 == 0 ? "Red" : ""), Encoding.UTF8);
            }
            if (i % 5 == 0) {
                File.WriteAllText(Path.Combine(dir, name + ".jpg.pp3"), Pp3(i % 5 + 1, i % 3), Encoding.UTF8);
            }
        }
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "Harness sandbox: synthetic photos with EXIF orientation and sidecars.\r\n");

        string sub = c.Dir("photos", "sub");
        for (int i = 1; i <= 10; i++) {
            string name = $"SUB_{i:000}";
            PictureFactory.SaveJpeg(Path.Combine(sub, name + ".jpg"), PhotoWidth, PhotoHeight, 1, name, seed: 1000 + i);
        }
    }

    private static void Raw(SandboxContext c) {
        string dir = c.Dir("raw");
        long cr3Bytes = c.Options.RawMb * 1024L * 1024L;
        long dngBytes = Math.Max(2, c.Options.RawMb / 4) * 1024L * 1024L;
        for (int i = 1; i <= c.Options.RawCount; i++) {
            int orientation = RawFiles.OrientationFor(i);
            string name = $"IMG_{i:0000}";
            var preview = PictureFactory.Jpeg(PhotoWidth, PhotoHeight, 1, name + " CR3", seed: 2000 + i);
            RawFiles.WriteCr3(Path.Combine(dir, name + ".CR3"), orientation, cr3Bytes, preview, PhotoWidth, PhotoHeight, seed: i);

            string dngName = $"DSC_{i:0000}";
            var dngPreview = PictureFactory.Jpeg(PhotoWidth, PhotoHeight, 1, dngName + " DNG", seed: 3000 + i);
            RawFiles.WriteDng(Path.Combine(dir, dngName + ".dng"), orientation, dngBytes, dngPreview, PhotoWidth, PhotoHeight, seed: 100 + i);
        }
        File.WriteAllText(Path.Combine(dir, "IMG_0001.CR3.pp3"), Pp3(4, 2), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "DSC_0001.xmp"), Xmp(5, "Green"), Encoding.UTF8);
    }

    private static void Big(SandboxContext c) {
        string dir = c.Dir("big");
        var line = Encoding.ASCII.GetBytes(new string('x', 1023) + "\n");
        for (int i = 1; i <= c.Options.Big; i++) {
            File.WriteAllBytes(Path.Combine(dir, $"file-{i:00000}.txt"), line);
        }
        for (int i = 1; i <= 50; i++) {
            Directory.CreateDirectory(Path.Combine(dir, $"dir-{i:00}"));
        }
    }

    private static void Deep(SandboxContext c) {
        string dir = c.Dir("deep");
        string current = dir;
        for (int level = 1; level <= 70; level++) {
            current = Path.Combine(current, $"l{level:00}");
            Directory.CreateDirectory(current);
        }
        File.WriteAllText(Path.Combine(current, "leaf.txt"), "bottom\r\n");

        // A junction pointing at an ancestor: the classic infinite tree.
        string loop = Path.Combine(dir, "l01", "l02", "loop");
        string target = Path.Combine(dir, "l01");
        if (!Directory.Exists(loop)) {
            var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{loop}\" \"{target}\"") {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
            mklink!.WaitForExit();
            c.Note(mklink.ExitCode == 0 ? "deep: junction created" : $"deep: mklink failed ({mklink.ExitCode})");
        }
    }

    private static void Names(SandboxContext c) {
        string dir = c.Dir("names");
        string[] names = {
            "пример файла.txt",
            "emoji \U0001F600 file.txt",
            "עברית.txt",
            "Ünïcödé.txt",
            "with.many.dots.in.name.txt",
            "  leading spaces.txt",
            new string('a', 200) + ".txt",
        };
        foreach (string name in names) {
            File.WriteAllText(Path.Combine(dir, name), name + "\r\n", Encoding.UTF8);
        }

        string deep = dir;
        for (int i = 0; i < 6; i++) {
            deep = Path.Combine(deep, new string((char)('a' + i), 60));
        }
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "beyond-260.txt"), "long path\r\n");
    }


    // --- Sidecars ------------------------------------------------------

    private static string Xmp(int rating, string label) {
        return
            "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\r\n" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\r\n" +
            " <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\r\n" +
            "  <rdf:Description rdf:about=\"\"\r\n" +
            "    xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"\r\n" +
            $"    xmp:Rating=\"{rating}\"\r\n" +
            $"    xmp:Label=\"{label}\"/>\r\n" +
            " </rdf:RDF>\r\n" +
            "</x:xmpmeta>\r\n" +
            "<?xpacket end=\"w\"?>\r\n";
    }

    private static string Pp3(int rank, int color) {
        return $"[General]\r\nRank={rank}\r\nColorLabel={color}\r\nInTrash=false\r\n\r\n[Exposure]\r\nCompensation=0\r\n";
    }
}


public sealed class SandboxContext {
    private readonly List<string> _summary = new();


    public SandboxContext(string root, SandboxOptions options) {
        Root = root;
        Options = options;
    }


    public string Root { get; }

    public SandboxOptions Options { get; }

    public IReadOnlyList<string> Summary => _summary;


    public string Dir(params string[] parts) {
        string path = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);

        return path;
    }

    public void Note(string text) {
        _summary.Add(text);
    }
}
