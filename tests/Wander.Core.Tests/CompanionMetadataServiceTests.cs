using System.Text;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Listing;
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

        var rated = RatedListing.WithRatings(rows, service.ReadRatingFor);

        Assert.Equal(2, rated[0].Rating!.Rank);
        Assert.Equal(1, rated[0].Rating!.ColorLabel);
    }

    [Fact]
    public void WithRatings_ReturnsTheSameListWhenNothingIsRated() {
        // The caller skips the whole UI pass on reference equality, so this
        // is the contract and not an implementation detail.
        var (service, _, _) = Build(pp3: null);
        var rows = new[] { Row("notes.txt"), Row("Sprite.png", MetaPath) };

        Assert.Same(rows, RatedListing.WithRatings(rows, service.ReadRatingFor));
    }

    [Fact]
    public void WithRatings_LeavesRowsWithoutCompanionsAlone() {
        var (service, _, _) = Build();
        var rows = new[] { Row("plain.jpg"), Row("IMG_1234.CR2", Pp3Path) };

        var rated = RatedListing.WithRatings(rows, service.ReadRatingFor);

        Assert.Null(rated[0].Rating);
        Assert.NotNull(rated[1].Rating);
    }

    [Fact]
    public void WithRatings_IgnoresCompanionsThatHoldNoRating() {
        var (service, _, _) = Build(pp3: null);
        var rows = new[] { Row("Sprite.png", MetaPath) };

        var rated = RatedListing.WithRatings(rows, service.ReadRatingFor);

        Assert.Null(rated[0].Rating);
    }

    [Fact]
    public void WithRatings_TakesOffACarriedRatingTheSidecarNoLongerHolds() {
        var (service, _, _) = Build(pp3: null);
        var rows = new[] { Row("Sprite.png", MetaPath) with { Rating = new SidecarRating(4, null) } };

        var rated = RatedListing.WithRatings(rows, service.ReadRatingFor);

        Assert.Null(rated[0].Rating);
    }

    [Fact]
    public void ReadRatingFor_TakesTheSidecarNamedAfterTheWholeFile_BeforeTheNeutralOne() {
        // Decision 2026-10-01: darktable's IMG.CR2.xmp (or a .pp3) first,
        // IMG.xmp after - whatever order the row lists them in.
        var (service, fs, _) = Build(pp3: null, xmp: Xmp);
        fs.Files[@"C:\photos\IMG_1234.CR2.xmp"] = Utf8(Xmp.Replace("xmp:Rating=\"2\"", "xmp:Rating=\"5\""));
        var row = Row("IMG_1234.CR2", XmpPath, @"C:\photos\IMG_1234.CR2.xmp");

        Assert.Equal(5, service.ReadRatingFor(row)?.Rank);
    }

    [Fact]
    public void RatingSidecars_PutTheEditorsOwnFirst_AndKeepTheRestInOrder() {
        var (service, _, _) = Build(pp3: null);
        var companions = new[] { @"C:\p\IMG.xmp", @"C:\p\IMG.CR2.pp3", @"C:\p\IMG.CR2.meta", @"C:\p\IMG.CR2.xmp" };

        Assert.Equal(
            new[] { @"C:\p\IMG.CR2.pp3", @"C:\p\IMG.CR2.xmp", @"C:\p\IMG.xmp" },
            service.RatingSidecars("IMG.CR2", companions));
    }

    [Fact]
    public void CarryRatings_PutsTheShownRatingsBack_OnlyWhereACompanionIs() {
        var shown = new[] {
            Row("a.cr2", Pp3Path) with { Rating = new SidecarRating(5, null) },
            Row("b.jpg") with { Rating = new SidecarRating(3, null) },
        };
        // The same files listed again - no ratings yet, and b.jpg's sidecar gone.
        var listed = new[] { Row("a.cr2", Pp3Path), Row("b.jpg"), Row("c.cr2", Pp3Path) };

        var carried = RatedListing.CarryRatings(listed, shown, SortOptions.Default);

        Assert.Equal(5, carried[0].Rating?.Rank);
        Assert.Null(carried[1].Rating);
        Assert.Null(carried[2].Rating);
    }

    [Fact]
    public void CarryRatings_KeepsThePhotosOwnRating_WithoutACompanion() {
        var shown = new[] { Row("IMG_8923.CR3") with { Rating = new SidecarRating(5, null, InPhoto: true) } };

        var carried = RatedListing.CarryRatings(new[] { Row("IMG_8923.CR3") }, shown, SortOptions.Default);

        Assert.Equal(5, carried[0].Rating?.Rank);
    }

    [Fact]
    public void RatingSidecars_LeaveDarktableDuplicatesOut() {
        // The stars are the original's; IMG_01.CR2.xmp is another version.
        var (service, _, _) = Build(pp3: null);

        Assert.Equal(
            new[] { @"C:\p\IMG.CR2.xmp" },
            service.RatingSidecars("IMG.CR2", new[] { @"C:\p\IMG_01.CR2.xmp", @"C:\p\IMG.CR2.xmp" }));
    }


    // --- The photo's own rating (the camera's) ----------------------------

    private const string CameraRaw = @"C:\photos\IMG_8923.CR3";

    private static (CompanionMetadataService Service, FakeFileSystem Fs, UndoService Undo) BuildCamera(int stars) {
        var built = Build(pp3: null);
        built.Fs.Files[CameraRaw] = EmbeddedRatingTests.Cr3(EmbeddedRatingTests.Packet(stars));

        return built;
    }

    [Fact]
    public void ReadRatingFor_ShowsTheCamerasStars_WithoutASidecar() {
        var (service, _, _) = BuildCamera(5);

        var rating = service.ReadRatingFor(Row("IMG_8923.CR3"));

        Assert.Equal(5, rating?.Rank);
        Assert.True(rating!.InPhoto);
    }

    [Fact]
    public void ReadRatingFor_ASidecarWithTheField_OverridesTheCamera() {
        var (service, fs, _) = BuildCamera(5);
        fs.Files[@"C:\photos\IMG_8923.CR3.pp3"] = Utf8("[General]\nRank=2\n");

        Assert.Equal(2, service.ReadRatingFor(Row("IMG_8923.CR3", @"C:\photos\IMG_8923.CR3.pp3"))?.Rank);
    }

    [Fact]
    public void PhotoRating_IsReadOnce_UntilTheFileChangesOrIsForgotten() {
        var (service, fs, _) = BuildCamera(5);
        var row = Row("IMG_8923.CR3");
        Assert.Equal(5, service.PhotoRating(row)?.Rank);

        // Rewritten with its size and time kept (exiftool -P): the cache
        // still answers - until the watcher or F5 says otherwise.
        fs.Files[CameraRaw] = EmbeddedRatingTests.Cr3(EmbeddedRatingTests.Packet(3));
        Assert.Equal(5, service.PhotoRating(row)?.Rank);
        service.ForgetPhotoRating(CameraRaw);
        Assert.Equal(3, service.PhotoRating(row)?.Rank);

        fs.Files[CameraRaw] = EmbeddedRatingTests.Cr3(EmbeddedRatingTests.Packet(1));
        service.ForgetPhotoRatings(@"C:\photos\");
        Assert.Equal(1, service.PhotoRating(row)?.Rank);

        // Another size or time is another file: read again on its own.
        fs.Files[CameraRaw] = EmbeddedRatingTests.Cr3(EmbeddedRatingTests.Packet(4));
        Assert.Equal(4, service.PhotoRating(row with { Size = 1 })?.Rank);
    }

    [Fact]
    public void PlanWrite_ClearingCreatesASidecar_OnlyOverThePhotosOwnStars() {
        var (service, _, _) = BuildCamera(5);

        Assert.Equal(RatingWrite.Create, service.PlanWrite(Row("IMG_8923.CR3"), RatingField.Rank, 0));
        Assert.Equal(RatingWrite.None, service.PlanWrite(Row("IMG_8923.CR3"), RatingField.ColorLabel, 0));
        Assert.Equal(RatingWrite.None, service.PlanWrite(Row("IMG_0001.CR3"), RatingField.Rank, 0));
        Assert.Equal(RatingWrite.Create, service.PlanWrite(Row("IMG_0001.CR3"), RatingField.Rank, 2));
        Assert.Equal(RatingWrite.Edit, service.PlanWrite(Row("IMG_1234.CR2", Pp3Path), RatingField.Rank, 0));
        Assert.Equal(RatingWrite.None, service.PlanWrite(Row("notes.txt"), RatingField.Rank, 2));
    }

    [Fact]
    public void ApplyRatingToMany_ClearingTheCamerasStars_CreatesASidecarWithZero() {
        var (service, fs, undo) = BuildCamera(5);

        var result = Assert.Single(service.ApplyRatingToMany(new[] { Row("IMG_8923.CR3") }, RatingField.Rank, 0, SidecarFormat.Xmp));

        Assert.Equal(@"C:\photos\IMG_8923.xmp", result.SidecarPath);
        Assert.Equal(0, service.ReadRating(result.SidecarPath)?.Rank);
        Assert.Equal(0, result.Rating.Rank);
        undo.Undo();
        Assert.False(fs.FileExists(@"C:\photos\IMG_8923.xmp"));
    }

    [Fact]
    public void ApplyRatingToMany_ALabelOnACameraRatedPhoto_KeepsItsStars() {
        var (service, _, _) = BuildCamera(5);

        var result = Assert.Single(service.ApplyRatingToMany(new[] { Row("IMG_8923.CR3") }, RatingField.ColorLabel, 3, SidecarFormat.Xmp));

        var written = service.ReadRating(result.SidecarPath);
        Assert.Equal(5, written?.Rank);
        Assert.Equal(3, written?.ColorLabel);
        Assert.Equal(5, result.Rating.Rank);
    }


    // --- Two sidecars of one photo --------------------------------------

    [Fact]
    public void ApplyRatingToMany_WritesEverySidecarThatHoldsTheField_InOneStep() {
        // Decision 2026-10-01: the neutral IMG.xmp and RawTherapee's .pp3
        // keep agreeing - both written, one Ctrl+Z.
        var (service, _, undo) = Build(xmp: Xmp);
        var row = Row("IMG_1234.CR2", Pp3Path, XmpPath);

        service.ApplyRatingToMany(new[] { row }, RatingField.Rank, 4, SidecarFormat.Xmp);

        Assert.Equal(4, service.ReadRating(Pp3Path)?.Rank);
        Assert.Equal(4, service.ReadRating(XmpPath)?.Rank);
        Assert.Equal(1, undo.Depth);
        undo.Undo();
        Assert.Equal(2, service.ReadRating(Pp3Path)?.Rank);
        Assert.Equal(2, service.ReadRating(XmpPath)?.Rank);
    }

    [Fact]
    public void ApplyRatingToMany_ASidecarWithoutTheField_IsLeftAlone() {
        string noRating = Xmp.Replace("   xmp:Rating=\"2\"\n", "");
        var (service, fs, _) = Build(xmp: noRating);

        service.ApplyRatingToMany(new[] { Row("IMG_1234.CR2", Pp3Path, XmpPath) }, RatingField.Rank, 4, SidecarFormat.Xmp);

        Assert.Equal(4, service.ReadRating(Pp3Path)?.Rank);
        Assert.Equal(noRating, Text(fs.Files[XmpPath]));
    }

    [Fact]
    public void CarryRatings_SortedByRating_OrdersByWhatWasCarried() {
        var shown = new[] {
            Row("a.cr2", Pp3Path) with { Rating = new SidecarRating(1, null) },
            Row("b.cr2", Pp3Path) with { Rating = new SidecarRating(5, null) },
        };
        var byRating = new SortOptions(SortKey.Rating, Ascending: false, GroupFoldersFirst: true);

        var carried = RatedListing.CarryRatings(new[] { Row("a.cr2", Pp3Path), Row("b.cr2", Pp3Path) }, shown, byRating);

        Assert.Equal(new[] { "b.cr2", "a.cr2" }, carried.Select(e => e.Name));
    }

    [Fact]
    public void CarryRatings_NothingRatedOnScreen_ReturnsTheSameList() {
        var listed = new[] { Row("a.cr2", Pp3Path) };

        Assert.Same(listed, RatedListing.CarryRatings(listed, new[] { Row("a.cr2", Pp3Path) }, SortOptions.Default));
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


    // --- Many photos, one undo step -------------------------------------

    [Fact]
    public void ApplyRatingToMany_EditsExistingAndCreatesMissing() {
        var (service, fs, _) = Build();
        var targets = new[] {
            Row("IMG_1234.CR2", Pp3Path),
            Row("IMG_9999.CR2"),
        };

        var results = service.ApplyRatingToMany(targets, RatingField.Rank, 4, SidecarFormat.Xmp);

        Assert.Equal(2, results.Count);
        Assert.Equal(4, service.ReadRating(Pp3Path)!.Rank);
        Assert.True(fs.FileExists(@"C:\photos\IMG_9999.xmp"));
        Assert.Equal(4, service.ReadRating(@"C:\photos\IMG_9999.xmp")!.Rank);
    }

    [Fact]
    public void ApplyRatingToMany_IsOneUndoStep() {
        // Rating a selection is one gesture; taking it back has to be one
        // press, not one per file.
        var (service, fs, undo) = Build();
        var targets = new[] {
            Row("IMG_1234.CR2", Pp3Path),
            Row("IMG_9999.CR2"),
        };

        service.ApplyRatingToMany(targets, RatingField.Rank, 5, SidecarFormat.Xmp);
        Assert.Equal(1, undo.Depth);

        undo.Undo();

        Assert.Equal(2, service.ReadRating(Pp3Path)!.Rank);
        Assert.False(fs.FileExists(@"C:\photos\IMG_9999.xmp"));
    }

    [Fact]
    public void ApplyRatingToMany_WithOneTarget_PushesThePlainStep() {
        var (service, _, undo) = Build();
        var targets = new[] {
            Row("IMG_1234.CR2", Pp3Path),
        };

        service.ApplyRatingToMany(targets, RatingField.ColorLabel, 3, SidecarFormat.Xmp);

        Assert.Equal(1, undo.Depth);
        undo.Undo();
        Assert.Equal(1, service.ReadRating(Pp3Path)!.ColorLabel);
    }

    [Fact]
    public void ApplyRatingToMany_SkipsWhatItCannotWrite() {
        // One unwritable photo must not take the rest of the batch down.
        var (service, fs, _) = Build();
        var targets = new[] {
            Row("ghost.CR2", @"C:\photos\ghost.CR2.pp3"),
            Row("IMG_1234.CR2", Pp3Path),
        };

        var results = service.ApplyRatingToMany(targets, RatingField.Rank, 1, SidecarFormat.Xmp);

        Assert.Single(results);
        Assert.Equal(@"C:\photos\IMG_1234.CR2", results[0].MainPath);
        Assert.False(fs.FileExists(@"C:\photos\ghost.CR2.pp3"));
    }

    /// <summary>A skipped photo is not a silent one: the caller hears which, and why, to tell the user.</summary>
    [Fact]
    public void ApplyRatingToMany_TellsWhichItCouldNotWrite() {
        var (service, _, _) = Build();
        var targets = new[] {
            Row("ghost.CR2", @"C:\photos\ghost.CR2.pp3"),
            Row("IMG_1234.CR2", Pp3Path),
        };
        var failed = new List<(string Path, Exception Error)>();

        service.ApplyRatingToMany(targets, RatingField.Rank, 1, SidecarFormat.Xmp, (path, ex) => failed.Add((path, ex)));

        var (path, error) = Assert.Single(failed);
        Assert.Equal(@"C:\photos\ghost.CR2", path);
        Assert.IsType<FileNotFoundException>(error);
    }

    [Fact]
    public void ApplyRatingToMany_WithNothingToDo_TouchesNothing() {
        var (service, _, undo) = Build();

        var results = service.ApplyRatingToMany(
            Array.Empty<FileSystemEntry>(), RatingField.Rank, 3, SidecarFormat.Xmp);

        Assert.Empty(results);
        Assert.Equal(0, undo.Depth);
    }


    // --- What an undo has to refresh ------------------------------------

    [Fact]
    public void RatingUndo_NamesThePhotoAndNotTheSidecar() {
        // The UI answers this by re-reading that one row instead of
        // re-listing the folder, so naming the sidecar here would point it
        // at a file that is not in the listing at all.
        var (service, _, undo) = Build();

        service.SetRating(Pp3Path, RatingField.Rank, 3, @"C:\photos\IMG_1234.CR2");

        Assert.Equal(new[] { @"C:\photos\IMG_1234.CR2" }, undo.Undo()!.MetadataTargets);
    }

    [Fact]
    public void BatchUndo_NamesEveryPhotoItTouched() {
        var (service, _, undo) = Build();
        var targets = new[] {
            Row("IMG_1234.CR2", Pp3Path),
            Row("IMG_9999.CR2"),
        };

        service.ApplyRatingToMany(targets, RatingField.Rank, 2, SidecarFormat.Xmp);

        Assert.Equal(
            new[] { @"C:\photos\IMG_1234.CR2", @"C:\photos\IMG_9999.CR2" },
            undo.Undo()!.MetadataTargets);
    }

    [Fact]
    public void MixedComposite_ClaimsNoMetadataTargets() {
        // A composite that also creates a folder changes the listing, and a
        // caller that took the cheap path on it would leave a folder on
        // screen that no longer matches the disk.
        var (service, _, _) = Build();
        var rating = new SidecarRatingAction(service, Pp3Path, RatingField.Rank, 1, 2, @"C:\photos\IMG_1234.CR2");
        var somethingElse = new CreateAction(new FakeRecycleBin(new FakeFileSystem()), @"C:\photos\new");

        var composite = new CompositeAction("mixed", new IUndoableAction[] { rating, somethingElse });

        Assert.Empty(composite.MetadataTargets);
    }
}
