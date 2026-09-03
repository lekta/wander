using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Wander.Core.FileSystem;
using Wander.Core.Icons;
using Wander.Core.Logging;
using Wander.Core.Preview;
using Wander.Core.Search;
using Wander.Harness.Host;
using Wander.Harness.Sandbox;
using Wander.Platform.Windows.FileSystem;
using Wander.Platform.Windows.Icons;
using Wander.Platform.Windows.Shell;

namespace Wander.Harness;

/// <summary>
/// Proves the generators against the readers the app really uses: the
/// embedded preview comes out of every synthetic RAW and decodes,
/// MetadataExtractor reads back the orientation that was written - for
/// CR3, DNG and JPEG alike - and every document, mesh, tag block and
/// codepage the sandbox writes is read back by the class that will read
/// it in the app. Run it after touching a generator.
///
/// <para>
/// This is the cheap half of the harness and the one that catches the
/// expensive mistake: a scenario that fails because the file it was given
/// was malformed costs an afternoon of looking in the wrong place.
/// </para>
/// </summary>
public static class SelfCheck {
    // Every verdict printed, passed or failed: the batch journal wants
    // "passed of total", and the failure count alone does not give it.
    private static int _checks;


    public static int Run(Options options) {
        _checks = 0;
        var clock = Stopwatch.StartNew();
        string dir = Path.GetFullPath(options.Value("dir") ?? Path.Combine(Path.GetTempPath(), "wander-sandbox", "selfcheck"));
        SandboxBuilder.Remove(dir);
        var built = SandboxBuilder.Build(
            dir, new[] { "photos", "raw", "docs", "media", "archives" },
            new SandboxOptions(Photos: 8, Big: 0, RawCount: 4, RawMb: 3));
        Console.WriteLine($"selfcheck sandbox: {built.Root}");
        foreach (string line in built.Summary) {
            Console.WriteLine("  " + line);
        }

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

        failures += CheckDocuments(Path.Combine(dir, "docs"));
        failures += CheckMedia(Path.Combine(dir, "media"));
        failures += CheckArchives(Path.Combine(dir, "archives"));
        failures += CheckFixtures(built.Fixtures);

        Console.WriteLine(failures == 0 ? "selfcheck: OK" : $"selfcheck: {failures} failure(s)");
        RunJournal.Append("selfcheck", _checks - failures, _checks, clock.Elapsed, failures == 0 ? "ok" : "fail");

        return failures == 0 ? 0 : 1;
    }


    // --- Documents and media -------------------------------------------

    /// <summary>
    /// Each zip-and-XML document has to give the search its needle back,
    /// each text file has to be detected as the codepage it was written
    /// in, and the FictionBook has to parse with its cover attached.
    /// </summary>
    private static int CheckDocuments(string dir) {
        int failures = 0;
        var extractor = new ZipDocumentExtractor(new SystemIOFileSystem());

        foreach (string name in new[] { "report.docx", "budget.xlsx", "deck.pptx", "notes.odt", "book.epub" }) {
            string path = Path.Combine(dir, name);
            string? text = extractor.Extract(path, CancellationToken.None);
            failures += Report(name, text?.Contains(DocumentFactory.Needle, StringComparison.Ordinal) == true
                ? $"ok, {text.Length} chars"
                : $"FAIL: no '{DocumentFactory.Needle}' in extracted text");
        }

        foreach (var (name, expected) in new[] {
            ("utf8.txt", TextEncodingKind.Utf8),
            ("utf16.txt", TextEncodingKind.Utf16LittleEndian),
            ("cp1251.txt", TextEncodingKind.Windows1251),
            ("cp866.txt", TextEncodingKind.Dos866),
        }) {
            byte[] bytes = File.ReadAllBytes(Path.Combine(dir, name));
            var detected = EncodingProbe.Detect(bytes);
            string decoded = EncodingProbe.Decode(bytes, detected);
            failures += Report(name, detected == expected && decoded.Contains("кодировки", StringComparison.Ordinal)
                ? $"ok, {detected}"
                : $"FAIL: read as {detected}, expected {expected}");
        }

        using var fb2 = File.OpenRead(Path.Combine(dir, "story.fb2"));
        var book = Fb2Document.Read(fb2);
        fb2.Position = 0;
        byte[]? cover = Fb2Document.ReadCover(fb2);
        failures += Report("story.fb2", book is null
            ? "FAIL: does not parse"
            : cover is null
                ? "FAIL: no cover binary"
                : $"ok, \"{book.Title}\", {book.BodyHtml.Length} chars of html, cover {cover.Length / 1024} KB");

        return failures;
    }

    /// <summary>
    /// The WAV has to come back with the tags that went in, and the three
    /// views of one cube have to agree on how many triangles a cube has.
    /// </summary>
    private static int CheckMedia(string dir) {
        int failures = 0;

        var wav = AudioTags.Read(Path.Combine(dir, "tone.wav"));
        failures += Report("tone.wav", wav?.Artist == "Wander Harness" && wav.SampleRate == 44100
            ? $"ok, \"{wav.Title}\" by {wav.Artist}, {wav.SampleRate} Hz, {wav.Channels} ch"
            : $"FAIL: tags read back as {wav?.Title ?? "(none)"} / {wav?.Artist ?? "(none)"}");

        foreach (string name in new[] { "cube.stl", "cube.obj", "cube.gltf" }) {
            var mesh = MeshFile.Read(Path.Combine(dir, name));
            failures += Report(name, mesh?.TriangleCount == 12
                ? $"ok, {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles"
                : $"FAIL: {mesh?.TriangleCount.ToString() ?? "unreadable"} triangles, expected 12");
        }

        return failures;
    }

    /// <summary>
    /// The files no generator can make, read back by the class the app
    /// reads them with. This is where a replaced fixture breaks quietly: a
    /// track whose tags did not survive the swap previews as a track with
    /// no tags, and the screenshot in <c>preview-formats</c> looks
    /// plausible either way. Formats we have no reader for - pdf, doc,
    /// webp, zip - are listed and left alone; they are the shell's and
    /// WebView2's business, and neither can be asked from here.
    /// </summary>
    private static int CheckFixtures(IReadOnlyList<string> fixtures) {
        if (fixtures.Count == 0) {
            Console.WriteLine("  fixtures         none: tests/Fixtures is empty or was not found");

            return 0;
        }

        int failures = 0;
        var documents = new ZipDocumentExtractor(new SystemIOFileSystem());
        foreach (string path in fixtures.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
            string name = Path.GetFileName(path);
            if (AudioTags.IsAudio(path)) {
                var track = AudioTags.Read(path);
                failures += Report(name, track is null || track.IsEmpty
                    ? "FAIL: no tags read back"
                    : $"ok, \"{track.Title ?? "(untitled)"}\" / {track.Artist ?? "(no artist)"}"
                        + (track.Duration is { } duration ? $", {duration.TotalSeconds:F1} s" : "")
                        + (track.Cover is null ? ", no cover" : $", cover {Size(track.Cover.Length)}"));
            } else if (documents.CanExtract(path)) {
                string? text = documents.Extract(path, CancellationToken.None);
                failures += Report(name, text is null ? "FAIL: does not extract" : $"ok, {text.Length} chars");
            } else {
                failures += Report(name, $"copied, {new FileInfo(path).Length / 1024} KB, nothing of ours reads it");
            }
        }

        return failures;
    }

    /// <summary>
    /// Does the shell of <em>this</em> machine open the archive fixtures as
    /// folders, and does it report an entry's size? Both are association
    /// state, not code: hand .7z to 7-Zip and the folder view closes here
    /// exactly as it does in Explorer. Asked before the archives scenario
    /// runs, so a red step there is never a mystery about whose fault it is.
    ///
    /// <para>
    /// A fixture whose extension the shell does not claim is reported and
    /// not counted against the run - that is a machine's configuration
    /// speaking, and the scenario's own assertions say what it costs.
    /// </para>
    /// </summary>
    private static int CheckArchives(string dir) {
        var shell = new WindowsShellNamespace(NullLogger.Instance);
        int failures = 0;

        foreach (string path in Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
            if (shell.ParseArchive(path) is null) {
                continue;
            }

            string name = Path.GetFileName(path);
            if (!shell.CanNavigate(path)) {
                Report(name, "not a folder to this shell: the association is somebody else's");
                continue;
            }

            var entries = shell.Enumerate(path);
            var file = entries.FirstOrDefault(e => e.Kind == EntryKind.File);
            failures += Report(name, entries.Count == 0
                ? "empty or fully encrypted, nothing to read"
                : file is null
                    ? $"ok, {entries.Count} entries, all folders"
                    : file.Size is null
                        ? $"FAIL: {entries.Count} entries, but '{file.Name}' has no size"
                        : $"ok, {entries.Count} entries, '{file.Name}' {Size((int)file.Size.Value)}");
        }

        return failures;
    }

    /// <summary>Bytes below a kilobyte, kilobytes above: a 700-byte cover printed as "0 KB" reads like a missing one.</summary>
    private static string Size(int bytes) {
        return bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024} KB";
    }

    private static int Report(string name, string verdict) {
        _checks++;
        Console.WriteLine($"  {name,-16} {verdict}");

        return verdict.StartsWith("FAIL", StringComparison.Ordinal) ? 1 : 0;
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
        _checks++;
        Console.WriteLine($"  {Path.GetFileName(path),-16} {size / (1024 * 1024.0),7:F1} MB  o={expectedOrientation}  {previewNote,-28} {verdict}");

        return problems.Count == 0 ? 0 : 1;
    }
}
