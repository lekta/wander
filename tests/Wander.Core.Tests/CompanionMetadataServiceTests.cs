using System.Text;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class CompanionMetadataServiceTests {
    private const string Pp3Path = @"C:\photos\IMG_1234.CR2.pp3";
    private const string XmpPath = @"C:\photos\IMG_1234.xmp";
    private const string MetaPath = @"C:\assets\Sprite.png.meta";

    private const string Pp3 = "[General]\nRank=2\nColorLabel=1\n\n[Exposure]\nCompensation=0.35\n";
    private const string Xmp =
        "<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d'?>\n" +
        "<x:xmpmeta xmlns:x='adobe:ns:meta/'>\n" +
        " <rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>\n" +
        "  <rdf:Description rdf:about=''\n" +
        "    xmlns:xmp='http://ns.adobe.com/xap/1.0/'\n" +
        "   xmp:Rating=\"2\"\n" +
        "   xmp:Label=\"Red\"/>\n" +
        " </rdf:RDF>\n" +
        "</x:xmpmeta>\n<?xpacket end='w'?>\n";


    private static (CompanionMetadataService Service, FakeFileSystem Fs, UndoService Undo) Build(
        string? pp3 = Pp3, string? xmp = null) {

        var fs = new FakeFileSystem();
        if (pp3 is not null) {
            fs.Files[Pp3Path] = Utf8(pp3);
        }
        if (xmp is not null) {
            fs.Files[XmpPath] = Utf8(xmp);
        }
        var undo = new UndoService();

        return (new CompanionMetadataService(fs, undo, NullLogger.Instance), fs, undo);
    }

    private static byte[] Utf8(string text) {
        return new UTF8Encoding(false).GetBytes(text);
    }

    private static string Text(byte[] bytes) {
        return new UTF8Encoding(false).GetString(bytes);
    }


    // --- Reading --------------------------------------------------------

    [Fact]
    public void ReadRating_ReadsAPp3() {
        var (service, _, _) = Build();

        var rating = service.ReadRating(Pp3Path);

        Assert.Equal(2, rating!.Rank);
        Assert.Equal(1, rating.ColorLabel);
        Assert.Equal("Red", rating.ColorLabelName);
    }

    [Fact]
    public void ReadRating_ReadsAnXmp() {
        var (service, _, _) = Build(pp3: null, xmp: Xmp);

        var rating = service.ReadRating(XmpPath);

        Assert.Equal(2, rating!.Rank);
        Assert.Equal(1, rating.ColorLabel);
    }

    [Fact]
    public void ReadRating_ReturnsNull_WhenThereIsNoSidecar() {
        var (service, _, _) = Build(pp3: null);

        Assert.Null(service.ReadRating(Pp3Path));
    }

    [Fact]
    public void ReadRating_ReturnsNull_ForAFormatWithNoRating() {
        var (service, fs, _) = Build(pp3: null);
        fs.Files[MetaPath] = Utf8("guid: abc\n");

        Assert.Null(service.ReadRating(MetaPath));
    }


    // --- Writing --------------------------------------------------------

    [Fact]
    public void SetRating_WritesAtomically_AndKeepsTheRest() {
        var (service, fs, _) = Build();

        service.SetRating(Pp3Path, RatingField.Rank, 5);

        Assert.Contains($"ReplaceAtomic:{Pp3Path}", fs.CallLog);
        Assert.Equal(Pp3.Replace("Rank=2", "Rank=5"), Text(fs.Files[Pp3Path]));
    }

    [Fact]
    public void SetRating_WritesTheColorLabel() {
        var (service, fs, _) = Build();

        service.SetRating(Pp3Path, RatingField.ColorLabel, 4);

        Assert.Equal(Pp3.Replace("ColorLabel=1", "ColorLabel=4"), Text(fs.Files[Pp3Path]));
    }

    [Fact]
    public void SetRating_WritesIntoAnXmp_ByLabelName() {
        var (service, fs, _) = Build(pp3: null, xmp: Xmp);

        service.SetRating(XmpPath, RatingField.ColorLabel, 3);

        Assert.Equal(Xmp.Replace("\"Red\"", "\"Green\""), Text(fs.Files[XmpPath]));
    }

    [Fact]
    public void SetRating_RefusesToCreateTheSidecar() {
        // An empty .pp3 appearing out of nowhere changes how RawTherapee
        // renders the photo — not something a click on a star may do.
        var (service, fs, _) = Build(pp3: null);

        Assert.Throws<FileNotFoundException>(() => service.SetRating(Pp3Path, RatingField.Rank, 3));
        Assert.Empty(fs.Files);
    }

    [Fact]
    public void SetRating_RefusesAFormatItCannotWrite() {
        var (service, fs, _) = Build(pp3: null);
        fs.Files[MetaPath] = Utf8("guid: abc\n");

        Assert.Throws<NotSupportedException>(() => service.SetRating(MetaPath, RatingField.Rank, 3));
        Assert.Equal("guid: abc\n", Text(fs.Files[MetaPath]));
    }


    // --- Undo -----------------------------------------------------------

    [Fact]
    public void SetRating_IsUndoable() {
        var (service, fs, undo) = Build();

        service.SetRating(Pp3Path, RatingField.Rank, 5);
        undo.Undo();

        Assert.Equal(2, Pp3Sidecar.Read(fs.Files[Pp3Path]).Rank);
    }

    [Fact]
    public void SetRating_Undo_LeavesTheOtherFieldAlone() {
        var (service, fs, undo) = Build();

        service.SetRating(Pp3Path, RatingField.Rank, 5);
        service.SetRating(Pp3Path, RatingField.ColorLabel, 4);
        undo.Undo();

        var rating = Pp3Sidecar.Read(fs.Files[Pp3Path]);
        Assert.Equal(5, rating.Rank);
        Assert.Equal(1, rating.ColorLabel);
    }

    [Fact]
    public void SetRating_Undo_DoesNotGrowTheStack() {
        // The undo write must not push a step of its own, or Ctrl+Z would
        // bounce the rating back and forth forever.
        var (service, _, undo) = Build();

        service.SetRating(Pp3Path, RatingField.Rank, 5);
        undo.Undo();

        Assert.Equal(0, undo.Depth);
    }


    // --- Ratings across a whole listing ---------------------------------

    private static FileSystemEntry Row(string name, params string[] companions) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\photos\" + name,
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false,
            Companions: companions.Length == 0 ? null : companions);
    }


    [Fact]
    public void WithRatings_FillsInWhatTheSidecarSays() {
        var (service, _, _) = Build();
        var rows = new[] { Row("IMG_1234.CR2", Pp3Path) };

        var rated = service.WithRatings(rows);

        Assert.Equal(2, rated[0].Rating!.Rank);
        Assert.Equal(1, rated[0].Rating!.ColorLabel);
    }

    [Fact]
    public void WithRatings_ReturnsTheSameListWhenNothingIsRated() {
        // The caller skips the whole UI pass on reference equality, so this
        // is the contract and not an implementation detail.
        var (service, _, _) = Build(pp3: null);
        var rows = new[] { Row("notes.txt"), Row("Sprite.png", MetaPath) };

        Assert.Same(rows, service.WithRatings(rows));
    }

    [Fact]
    public void WithRatings_LeavesRowsWithoutCompanionsAlone() {
        var (service, _, _) = Build();
        var rows = new[] { Row("plain.jpg"), Row("IMG_1234.CR2", Pp3Path) };

        var rated = service.WithRatings(rows);

        Assert.Null(rated[0].Rating);
        Assert.NotNull(rated[1].Rating);
    }

    [Fact]
    public void WithRatings_IgnoresCompanionsThatHoldNoRating() {
        var (service, _, _) = Build(pp3: null);
        var rows = new[] { Row("Sprite.png", MetaPath) };

        var rated = service.WithRatings(rows);

        Assert.Null(rated[0].Rating);
    }


    // --- Creating a sidecar ---------------------------------------------

    [Fact]
    public void SidecarPathFor_AppendsForPp3AndReplacesForXmp() {
        var (service, _, _) = Build(pp3: null);

        Assert.Equal(Pp3Path, service.SidecarPathFor(@"C:\photos\IMG_1234.CR2", SidecarFormat.Pp3));
        Assert.Equal(XmpPath, service.SidecarPathFor(@"C:\photos\IMG_1234.CR2", SidecarFormat.Xmp));
    }

    [Fact]
    public void CreateRatingSidecar_WritesAnXmpThatReadsBack() {
        var (service, fs, _) = Build(pp3: null);

        string created = service.CreateRatingSidecar(
            @"C:\photos\IMG_1234.CR2", SidecarFormat.Xmp, RatingField.Rank, 4);

        Assert.Equal(XmpPath, created);
        Assert.True(fs.FileExists(created));
        Assert.Equal(4, service.ReadRating(created)!.Rank);
    }

    [Fact]
    public void CreateRatingSidecar_WritesAPp3ThatReadsBack() {
        var (service, fs, _) = Build(pp3: null);

        string created = service.CreateRatingSidecar(
            @"C:\photos\IMG_1234.CR2", SidecarFormat.Pp3, RatingField.ColorLabel, 3);

        Assert.Equal(Pp3Path, created);
        var rating = service.ReadRating(created);
        Assert.Equal(3, rating!.ColorLabel);
        Assert.Equal(0, rating.Rank);
        Assert.Contains("[General]", Text(fs.Files[created]));
    }

    [Fact]
    public void CreateRatingSidecar_RefusesWhenTheFileIsAlreadyThere() {
        // An existing sidecar is an edit, and an edit has to go through the
        // path that preserves every other byte of somebody's develop recipe.
        var (service, _, _) = Build();

        Assert.Throws<InvalidOperationException>(
            () => service.CreateRatingSidecar(@"C:\photos\IMG_1234.CR2", SidecarFormat.Pp3, RatingField.Rank, 1));
    }

    [Fact]
    public void CreateRatingSidecar_IsUndoneByDeletingTheFile() {
        var (service, fs, undo) = Build(pp3: null);

        string created = service.CreateRatingSidecar(
            @"C:\photos\IMG_1234.CR2", SidecarFormat.Xmp, RatingField.Rank, 5);
        Assert.True(fs.FileExists(created));

        undo.Undo();

        Assert.False(fs.FileExists(created));
    }
}
