using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Wander.Platform.Windows.Shell;

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


/// <param name="Fixtures">The files copied in from <c>tests\Fixtures</c>, by full path - what selfcheck reads back.</param>
public sealed record BuiltSandbox(string Root, IReadOnlyList<string> Summary, IReadOnlyList<string> Fixtures);


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
        ["docs"] = Docs,
        ["code"] = Code,
        ["media"] = Media,
        ["attrs"] = Attributes,
        ["links"] = Links,
        ["archives"] = Archives,
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

        return new BuiltSandbox(root, context.Summary, context.CopiedFixtures);
    }

    /// <summary>Deletes a sandbox. Junctions go first and as junctions, so nothing they point at is touched.</summary>
    public static void Remove(string root) {
        if (!Directory.Exists(root)) {
            return;
        }

        var top = new DirectoryInfo(root);
        foreach (var dir in top.EnumerateDirectories("*", SearchOption.AllDirectories).ToList()) {
            if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                dir.Delete();
            }
        }

        // The read-only flag comes off first: the attrs profile writes such
        // a file on purpose, and Directory.Delete refuses it - a --rebuild
        // of that sandbox died with "access denied" on a file the harness
        // had made itself. Hidden and system delete fine.
        foreach (var file in top.EnumerateFiles("*", SearchOption.AllDirectories)) {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly)) {
                file.Attributes &= ~FileAttributes.ReadOnly;
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


    /// <summary>
    /// Every format the content search and the preview pane claim to read,
    /// one file each, all hiding <see cref="DocumentFactory.Needle"/> in
    /// their prose. What the generators cannot make - a Word 97 <c>.doc</c>
    /// among them - is copied in from the fixtures folder if it is there.
    /// </summary>
    private static void Docs(SandboxContext c) {
        string dir = c.Dir("docs");
        DocumentFactory.WriteAll(dir);
        c.Fixtures.CopyEach(c, dir, ".doc", ".pdf", ".rtf", ".docx", ".xlsx", ".pptx", ".epub", ".chm", ".msg", ".djvu");
    }

    /// <summary>
    /// Source files: one per highlighting branch, plus the pair of Unity
    /// assets that decide the same extension two different ways. The
    /// <c>.bat</c> is written in codepage 866 rather than UTF-8, because a
    /// batch file in DOS Cyrillic is the case <c>EncodingProbe</c> exists
    /// for and the one nobody ever has a sample of.
    /// </summary>
    private static void Code(SandboxContext c) {
        string dir = c.Dir("code");

        File.WriteAllText(Path.Combine(dir, "Program.cs"),
            "using System;\r\n\r\nnamespace Sample;\r\n\r\n" +
            "internal static class Program {\r\n" +
            "    public static void Main() {\r\n" +
            "        Console.WriteLine(\"hello\");\r\n" +
            "    }\r\n}\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "Window.xaml"),
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n" +
            "        Title=\"Sample\" Width=\"320\" Height=\"240\">\r\n" +
            "    <Grid>\r\n        <TextBlock Text=\"hello\"/>\r\n    </Grid>\r\n" +
            "</Window>\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "pipeline.yaml"),
            "name: build\r\non:\r\n  push:\r\n    branches: [ master ]\r\njobs:\r\n" +
            "  check:\r\n    runs-on: windows-latest\r\n    steps:\r\n" +
            "      - uses: actions/checkout@v4\r\n      - run: tools\\check.bat\r\n",
            new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(dir, "build.bat"), CyrillicEncoding.Encode(
            "@echo off\r\nrem Сборка проекта\r\necho Собираем...\r\ndotnet build\r\n",
            CyrillicEncoding.Dos866));
        File.WriteAllText(Path.Combine(dir, "change.diff"),
            "--- a/Program.cs\r\n+++ b/Program.cs\r\n@@ -5,3 +5,3 @@\r\n" +
            " internal static class Program {\r\n" +
            "-        Console.WriteLine(\"hello\");\r\n" +
            "+        Console.WriteLine(\"hello, world\");\r\n" +
            " }\r\n", new UTF8Encoding(false));

        // Unity serialises the same extension two ways depending on a
        // project switch, so .asset is routed as "text if the bytes say so"
        // - one of each is what makes that decision visible.
        File.WriteAllText(Path.Combine(dir, "Settings.asset"),
            "%YAML 1.1\r\n%TAG !u! tag:unity3d.com,2011:\r\n--- !u!114 &11400000\r\n" +
            "MonoBehaviour:\r\n  m_ObjectHideFlags: 0\r\n  m_Name: Settings\r\n" +
            "  volume: 0.8\r\n  language: ru\r\n", new UTF8Encoding(false));
        var blob = new byte[4096];
        new Random(17).NextBytes(blob);
        blob[0] = 0;
        File.WriteAllBytes(Path.Combine(dir, "Binary.asset"), blob);
        foreach (string asset in new[] { "Settings.asset", "Binary.asset" }) {
            File.WriteAllText(Path.Combine(dir, asset + ".meta"),
                "fileFormatVersion: 2\r\nguid: 0123456789abcdef0123456789abcdef\r\n" +
                "NativeFormatImporter:\r\n  externalObjects: {}\r\n  userData:\r\n",
                new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Audio, video and pictures. A WAV and three views of one cube are
    /// generated; everything that needs a real encoder comes from the
    /// fixtures folder, and a format nobody has supplied is a note rather
    /// than a failure - the scenario that needs it asserts for itself.
    /// </summary>
    private static void Media(SandboxContext c) {
        string dir = c.Dir("media");
        MediaFactory.WriteAll(dir);
        c.Fixtures.CopyEach(c, dir,
            ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".m4b", ".aac", ".wma",
            ".mp4", ".webm", ".gif", ".webp", ".heic");

        // A cover beside a track, which is the other half of the audio
        // card: tags from the file, picture from the folder.
        PictureFactory.SaveJpeg(Path.Combine(dir, "cover.jpg"), 600, 600, 1, "COVER", seed: 42);
    }

    /// <summary>
    /// Files the listing has to decide about rather than just show: hidden
    /// and system flags, a read-only file, a folder with its own icon, and
    /// one file the current user is denied read on. <c>locked.txt</c> is
    /// ordinary here on purpose - a handle held by the builder would be
    /// gone by the time a scenario ran against a sandbox it did not
    /// rebuild, so the runner takes it with <c>fs lock</c> instead.
    /// </summary>
    private static void Attributes(SandboxContext c) {
        string dir = c.Dir("attrs");

        Write(dir, "plain.txt", FileAttributes.Normal);
        Write(dir, "hidden.txt", FileAttributes.Hidden);
        Write(dir, "system.txt", FileAttributes.System);
        Write(dir, "hidden-system.txt", FileAttributes.Hidden | FileAttributes.System);
        Write(dir, "readonly.txt", FileAttributes.ReadOnly);
        Write(dir, "locked.txt", FileAttributes.Normal);

        // A folder only honours desktop.ini when it is marked system or
        // read-only - without the flag the file is just a file and the
        // custom icon never appears.
        string themed = c.Dir("attrs", "themed");
        File.WriteAllText(Path.Combine(themed, "desktop.ini"),
            "[.ShellClassInfo]\r\nIconResource=%SystemRoot%\\system32\\SHELL32.dll,43\r\n" +
            "InfoTip=Harness folder with a custom icon\r\n", Encoding.Unicode);
        File.SetAttributes(Path.Combine(themed, "desktop.ini"), FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(themed, File.GetAttributes(themed) | FileAttributes.System);
        File.WriteAllText(Path.Combine(themed, "inside.txt"), "inside a themed folder\r\n");

        // Denied to Everyone by SID rather than by name: the well-known
        // account is spelled differently on a localised Windows, and this
        // has to work on both.
        string denied = Path.Combine(dir, "denied.txt");
        File.WriteAllText(denied, "you should not be able to read this\r\n");
        c.Note(Run("icacls.exe", $"\"{denied}\" /deny *S-1-1-0:(R)")
            ? "attrs: denied.txt is unreadable"
            : "attrs: icacls failed, denied.txt is readable");
    }

    /// <summary>
    /// Three shortcuts: to a file, to a folder, and to a path that is not
    /// there. Made through the app's own <c>IShortcutService</c>, so a
    /// broken .lnk writer would break the sandbox rather than hide behind
    /// a hand-built file that Explorer happens to accept.
    /// </summary>
    private static void Links(SandboxContext c) {
        string dir = c.Dir("links");
        string target = Path.Combine(dir, "target.txt");
        File.WriteAllText(target, "the file a shortcut points at\r\n");
        string folder = c.Dir("links", "target-folder");
        File.WriteAllText(Path.Combine(folder, "inside.txt"), "inside\r\n");

        var shortcuts = new ShellShortcutService();
        shortcuts.Create(target, Path.Combine(dir, "to-file.lnk"));
        shortcuts.Create(folder, Path.Combine(dir, "to-folder.lnk"));
        shortcuts.Create(Path.Combine(dir, "gone.txt"), Path.Combine(dir, "broken.lnk"));
        c.Note("links: 3 shortcuts");
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


    /// <summary>
    /// Archives to walk into, all holding the same three files under the
    /// same two names, so one <c>assert-entries</c> serves every format.
    /// Zip is written by the runtime and <c>.tar.gz</c> by the system's own
    /// <c>tar.exe</c>; the two nobody can make here - a real 7z and a
    /// password-protected zip - come from the fixtures folder.
    ///
    /// <para>
    /// <c>plain.zip</c> next to them is not an archive to walk into but the
    /// control case: a real folder called <c>plain.zip</c>, which has to
    /// open as the folder it is.
    /// </para>
    /// </summary>
    private static void Archives(SandboxContext c) {
        string dir = c.Dir("archives");
        string tree = Path.Combine(dir, "~build");
        Directory.CreateDirectory(Path.Combine(tree, "docs"));
        File.WriteAllText(Path.Combine(tree, "readme.txt"), "Wander archive fixture.\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(tree, "docs", "manual.txt"), "manual\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(tree, "docs", "notes.txt"), "notes\r\n", new UTF8Encoding(false));

        string zip = Path.Combine(dir, "nested.zip");
        File.Delete(zip);
        ZipFile.CreateFromDirectory(tree, zip, CompressionLevel.Optimal, includeBaseDirectory: false);

        // tar.exe has shipped with Windows since 1803 and is the only tar
        // writer on the machine. Missing it costs the .tar.gz row and
        // nothing else - the scenario asserts per archive.
        c.Note(Run("tar.exe", $"-czf \"{Path.Combine(dir, "nested.tar.gz")}\" -C \"{tree}\" .")
            ? "archives: nested.tar.gz written"
            : "archives: tar.exe unavailable, no nested.tar.gz");

        Directory.Delete(tree, recursive: true);
        // nested.rar mirrors nested.zip; solid.rar is the slow kind - six
        // entries in one stream, what the preview pane's one-at-a-time
        // unpacking exists for.
        c.Fixtures.CopyNamed(c, dir, "nested.7z", "locked.zip", "nested.rar", "solid.rar");

        string folder = c.Dir("archives", "plain.zip");
        File.WriteAllText(Path.Combine(folder, "inside.txt"), "A folder, not an archive.\r\n", new UTF8Encoding(false));
    }


    // --- Small helpers -------------------------------------------------

    /// <summary>A one-line text file with attributes; the name says what it is for.</summary>
    private static void Write(string dir, string name, FileAttributes attributes) {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, $"{name}: {attributes}\r\n", Encoding.UTF8);
        if (attributes != FileAttributes.Normal) {
            File.SetAttributes(path, attributes);
        }
    }

    /// <summary>Runs a console tool and says whether it succeeded. Nothing here needs its output.</summary>
    private static bool Run(string exe, string arguments) {
        try {
            using var process = Process.Start(new ProcessStartInfo(exe, arguments) {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) {
                return false;
            }
            process.WaitForExit();

            return process.ExitCode == 0;
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) {
            return false;
        }
    }
}


public sealed class SandboxContext {
    private readonly List<string> _summary = new();
    private readonly List<string> _fixtureFiles = new();
    private FixtureLibrary? _fixtures;


    public SandboxContext(string root, SandboxOptions options) {
        Root = root;
        Options = options;
    }


    public string Root { get; }

    public SandboxOptions Options { get; }

    /// <summary>The files no generator can make. Found once, on the first profile that asks.</summary>
    public FixtureLibrary Fixtures => _fixtures ??= FixtureLibrary.Discover();

    public IReadOnlyList<string> Summary => _summary;

    /// <summary>
    /// Where each fixture landed. Kept because a fixture cannot be found
    /// again by name afterwards - it keeps whatever name it came with, and
    /// a generated file may share its extension (docs has both a made-up
    /// report.docx and whatever real .docx somebody supplied).
    /// </summary>
    public IReadOnlyList<string> CopiedFixtures => _fixtureFiles;


    public string Dir(params string[] parts) {
        string path = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);

        return path;
    }

    public void Note(string text) {
        _summary.Add(text);
    }

    public void NoteFixture(string path) {
        _fixtureFiles.Add(path);
    }
}
