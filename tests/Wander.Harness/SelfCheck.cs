using System.IO;
using System.Windows.Media.Imaging;
using Wander.Core.Icons;
using Wander.Harness.Sandbox;
using Wander.Platform.Windows.Icons;

namespace Wander.Harness;

/// <summary>
/// Proves the generators against the readers the app really uses: the
/// embedded preview comes out of every synthetic RAW and decodes, and
/// MetadataExtractor reads back the orientation that was written - for
/// CR3, DNG and JPEG alike. Run it after touching a generator.
/// </summary>
public static class SelfCheck {
    public static int Run(Options options) {
        string dir = Path.GetFullPath(options.Value("dir") ?? Path.Combine(Path.GetTempPath(), "wander-sandbox", "selfcheck"));
        SandboxBuilder.Remove(dir);
        var built = SandboxBuilder.Build(dir, new[] { "photos", "raw" }, new SandboxOptions(Photos: 8, Big: 0, RawCount: 4, RawMb: 3));
        Console.WriteLine($"selfcheck sandbox: {built.Root}");

        var reader = new MetadataExtractorImageReader();
        int failures = 0;

        foreach (string path in Directory.EnumerateFiles(Path.Combine(dir, "raw")).Where(p => p.EndsWith(".CR3", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dng", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p)) {
            int index = int.Parse(Path.GetFileNameWithoutExtension(path)[4..]);
            int expected = RawFiles.OrientationFor(index);
            failures += Check(path, expected, reader, requirePreview: true);
        }
        foreach (string path in Directory.EnumerateFiles(Path.Combine(dir, "photos"), "IMG_*.jpg").OrderBy(p => p)) {
            int index = int.Parse(Path.GetFileNameWithoutExtension(path)[4..]);
            failures += Check(path, RawFiles.OrientationFor(index), reader, requirePreview: false);
        }

        Console.WriteLine(failures == 0 ? "selfcheck: OK" : $"selfcheck: {failures} failure(s)");

        return failures == 0 ? 0 : 1;
    }


    private static int Check(string path, int expectedOrientation, MetadataExtractorImageReader reader, bool requirePreview) {
        var problems = new List<string>();
        long size = new FileInfo(path).Length;

        string previewNote = "-";
        if (requirePreview) {
            using var stream = File.OpenRead(path);
            var jpeg = RawPreviewExtractor.Extract(stream);
            if (jpeg is null) {
                problems.Add("no embedded preview");
            } else {
                try {
                    var decoder = new JpegBitmapDecoder(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    previewNote = $"preview {frame.PixelWidth}x{frame.PixelHeight} ({jpeg.Length / 1024} KB)";
                } catch (Exception ex) {
                    problems.Add($"preview does not decode: {ex.Message}");
                }
            }
        }

        var meta = reader.Read(path);
        if (meta is null) {
            problems.Add("metadata unreadable");
        } else if (meta.Orientation != expectedOrientation) {
            problems.Add($"orientation {meta.Orientation?.ToString() ?? "null"}, expected {expectedOrientation}");
        }

        string verdict = problems.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", problems);
        Console.WriteLine($"  {Path.GetFileName(path),-16} {size / (1024 * 1024.0),7:F1} MB  o={expectedOrientation}  {previewNote,-28} {verdict}");

        return problems.Count == 0 ? 0 : 1;
    }
}
