using Wander.Core.Companions;
using Wander.Core.Icons;
using Wander.Core.Listing;

namespace Wander.Core.Tests;

/// <summary>PLAN F1, decisions 2026-11-06: what a folder's own picture shows.</summary>
public class FolderPictureTests {
    private const string Folder = @"D:\inbox\stuff";

    private static readonly DateTime _day = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);


    [Theory]
    [InlineData("IMG_0931.CR3", FolderContent.Picture)]
    [InlineData("Hero_Albedo.png", FolderContent.Picture)]
    [InlineData("cover.psd", FolderContent.Picture)]
    [InlineData("rock.tga", FolderContent.Picture)]
    [InlineData("logo.svg", FolderContent.Picture)]
    [InlineData("clip.mp4", FolderContent.Video)]
    [InlineData("00001.mts", FolderContent.Video)]
    [InlineData("drone45.mp3", FolderContent.Sound)]
    [InlineData("hit.wav", FolderContent.Sound)]
    [InlineData("tree.fbx", FolderContent.Model)]
    [InlineData("scene.blend", FolderContent.Model)]
    [InlineData("Roboto.ttf", FolderContent.Font)]
    [InlineData("manual.pdf", FolderContent.Document)]
    [InlineData("report.docx", FolderContent.Document)]
    [InlineData("book.epub", FolderContent.Document)]
    [InlineData("notes.txt", FolderContent.Document)]
    [InlineData("README.md", FolderContent.Document)]
    [InlineData("Viewer.exe", FolderContent.Program)]
    [InlineData("settings.ini", FolderContent.None)]
    [InlineData("app.cfg", FolderContent.None)]
    [InlineData("run.log", FolderContent.None)]
    [InlineData("core.dll", FolderContent.None)]
    [InlineData("core.pdb", FolderContent.None)]
    [InlineData("site manual.zip", FolderContent.None)]
    [InlineData("Program.cs", FolderContent.None)]
    [InlineData("LICENSE", FolderContent.None)]
    public void Kinds_GoByExtension(string name, FolderContent content) {
        Assert.Equal(content, FolderPicture.ContentOf(name));
    }

    [Fact]
    public void Cards_AreTwoInATile_ThreeInAnIcon_FourOnTheGallerysFace() {
        Assert.Equal(2, FolderPicture.Cards(IconSize.Medium, grid: false));
        Assert.Equal(3, FolderPicture.Cards(IconSize.Large, grid: false));
        Assert.Equal(4, FolderPicture.Cards(IconSize.Large, grid: true));
    }


    [Theory]
    [InlineData("unins000.exe")]
    [InlineData("Uninstall.exe")]
    [InlineData("Updater.exe")]
    [InlineData("AutoUpdate.exe")]
    [InlineData("CrashReporter.exe")]
    [InlineData("BugReport.exe")]
    [InlineData("ViewerHelper.exe")]
    [InlineData("ViewerService.exe")]
    [InlineData("setup.exe")]
    [InlineData("vc_redist.x64.exe")]
    public void HelperPrograms_AreNeverTheMainOne(string helper) {
        var files = new[] { Item(helper, size: 90_000_000), Item("Viewer.exe", size: 1000) };

        Assert.Equal("Viewer.exe", FolderPicture.MainProgram("Tools", files)?.Name);
        Assert.Null(FolderPicture.MainProgram("Tools", new[] { Item(helper) }));
    }

    [Fact]
    public void TheProgramNamedAsTheFolder_WinsOverABiggerOne() {
        var files = new[] { Item("7zG.exe", size: 700), Item("7zFM.exe", size: 900), Item("viewer.EXE", size: 100) };

        Assert.Equal("viewer.EXE", FolderPicture.MainProgram("Viewer", files)?.Name);
        Assert.Equal("7zFM.exe", FolderPicture.MainProgram("7-Zip", files)?.Name);
    }

    [Fact]
    public void ProgramsOfTheSameSize_TheFirstOneWins() {
        var files = new[] { Item("b.exe", size: 5), Item("a.exe", size: 5) };

        Assert.Equal("b.exe", FolderPicture.MainProgram("x", files)?.Name);
    }


    /// <summary>The <c>raw</c> folder of the stand: the freshest files are its sidecars, and the shell drew a blank sheet for them.</summary>
    [Fact]
    public void Sidecars_AreNotShown_ThePicturesAre_FreshestFirst() {
        var files = new[] {
            Item("IMG_01.CR3", age: 30), Item("IMG_01.CR3.xmp", age: 1), Item("IMG_02.CR3", age: 29),
            Item("IMG_02.pp3", age: 2), Item("IMG_03.CR3", age: 28), Item("IMG_04.CR3", age: 27),
        };

        Assert.Equal(new[] { "IMG_04.CR3", "IMG_03.CR3", "IMG_02.CR3" }, Names(Pick(files, cards: 3)));
    }

    /// <summary>The mixed folder of the stand: one of each kind in the order their best files rank; the archive and the settings are not shown.</summary>
    [Fact]
    public void OneOfEachKind_InTheOrderTheirBestFilesRank() {
        var files = new[] {
            Item("drone.mp3", age: 6), Item("shot.jpg", age: 9), Item("tree.fbx", age: 7),
            Item("app.ini", age: 3), Item("notes.txt", age: 5), Item("site.zip", age: 1),
        };

        Assert.Equal(new[] { "notes.txt", "drone.mp3", "tree.fbx", "shot.jpg" }, Names(Pick(files, cards: 4)));
        Assert.Equal(new[] { "notes.txt", "drone.mp3" }, Names(Pick(files, cards: 2)));
    }

    [Fact]
    public void PlacesLeftOver_AreFilledWithTheNextBest_OfAnyKind() {
        var files = new[] {
            Item("a.png", age: 1), Item("b.png", age: 2), Item("c.png", age: 3), Item("song.mp3", age: 4),
        };

        Assert.Equal(new[] { "a.png", "song.mp3", "b.png", "c.png" }, Names(Pick(files, cards: 4)));
    }

    /// <summary>The <c>Viewer</c> folder: the program first, its helpers and libraries never; the readme after it.</summary>
    [Fact]
    public void TheMainProgram_GoesFirst() {
        var files = new[] {
            Item("Updater.exe", age: 6, size: 9000), Item("Viewer.dll", age: 2), Item("readme.txt", age: 7),
            Item("settings.ini", age: 1), Item("unins000.exe", age: 3), Item("Viewer.exe", age: 6, size: 100),
        };

        var picks = Pick(files, cards: 3, folder: @"C:\Programs\Viewer");

        Assert.Equal(new[] { "Viewer.exe", "readme.txt" }, Names(picks));
        Assert.Equal(FolderContent.Program, picks[0].Content);
        Assert.Equal(@"C:\Programs\Viewer\Viewer.exe", picks[0].Path);
    }

    [Fact]
    public void AWalkCutShort_IsShownByWhatItRead_InTheOrderRead() {
        var files = new[] { Item("a.png", age: 9), Item("b.png", age: 8), Item("c.png", age: 1) };

        Assert.Equal(new[] { "a.png", "b.png" }, Names(Pick(files, cards: 2, complete: false)));
        Assert.Equal(new[] { "c.png", "b.png" }, Names(Pick(files, cards: 2, complete: true)));
    }

    [Fact]
    public void Hidden_SystemsOwn_Copies_AndBackups_AreNotShown() {
        var files = new[] {
            Item("secret.png", age: 1, hidden: true), Item("._IMG.jpg", age: 1), Item("Thumbs.db", age: 1),
            Item("shot.jpg.bak", age: 1), Item("shot.jpg~", age: 1), Item("scene.blend1", age: 1),
            Item("shot.jpg", age: 5),
        };

        Assert.Equal(new[] { "shot.jpg" }, Names(Pick(files, cards: 4)));
    }

    [Fact]
    public void NothingWorthShowing_IsNoPick() {
        Assert.Empty(Pick(new[] { Item("site.zip"), Item("core.dll"), Item("app.ini") }, cards: 3));
        Assert.Empty(Pick(Array.Empty<FolderItem>(), cards: 3));
    }


    /// <summary>The <c>_misc</c> folder: nothing of its own, so one file from each subfolder, a level down.</summary>
    [Fact]
    public void AFolderWithNothingOfItsOwn_IsShownByItsSubfolders_OneFileEach() {
        var read = new FolderRead(
            new[] { Item("old.zip") },
            new[] { Item("models", age: 3), Item(".git", age: 1, hidden: true), Item("__MACOSX", age: 1), Item("docs", age: 2), Item("empty", age: 4), Item("photos", age: 5) },
            Complete: true);
        var inside = new Dictionary<string, FolderRead> {
            [Folder + @"\models"] = Read(Item("a.fbx", age: 2), Item("b.fbx", age: 1)),
            [Folder + @"\docs"] = Read(Item("manual.pdf")),
            [Folder + @"\empty"] = Read(),
            [Folder + @"\photos"] = Read(Item("x.jpg")),
        };
        var asked = new List<string>();

        var picks = FolderPicture.Choose(Folder, read, 3, CompanionResolver.Default, path => {
            asked.Add(Path.GetFileName(path));

            return inside[path];
        });

        Assert.Equal(new[] { "manual.pdf", "b.fbx", "x.jpg" }, Names(picks));
        Assert.All(picks, p => Assert.True(p.Inside));
        Assert.Equal(@"D:\inbox\stuff\models\b.fbx", picks[1].Path);
        Assert.Equal(new[] { "docs", "models", "empty", "photos" }, asked);
    }

    [Fact]
    public void Subfolders_AreNotLookedInto_WhenTheFolderHasSomethingOfItsOwn() {
        var read = new FolderRead(new[] { Item("a.png") }, new[] { Item("sub") }, Complete: true);

        var picks = FolderPicture.Choose(Folder, read, 3, CompanionResolver.Default, _ => throw new InvalidOperationException());

        Assert.Equal(new[] { "a.png" }, Names(picks));
        Assert.False(picks[0].Inside);
    }

    [Fact]
    public void OutOfTime_TheSubfoldersReadSoFarAreWhatIsShown() {
        var read = new FolderRead(Array.Empty<FolderItem>(), new[] { Item("a"), Item("b"), Item("c") }, Complete: false);
        int reads = 0;

        var picks = FolderPicture.Choose(Folder, read, 4, CompanionResolver.Default, _ => ++reads == 1 ? Read(Item("x.jpg")) : null);

        Assert.Equal(new[] { "x.jpg" }, Names(picks));
        Assert.Equal(2, reads);
    }


    /// <summary>The <c>mono\data</c> case, decision 2026-11-09: code and settings only - their types, the most frequent first.</summary>
    [Fact]
    public void NothingWorthShowing_ShowsTheTypes_MostFrequentFirst() {
        var read = Read(
            Item("a.config", age: 1), Item("Makefile.am", age: 2), Item("b.py", age: 3), Item("c.py", age: 4),
            Item("x.aspx", age: 5), Item("d.config", age: 6), Item("e.py", age: 7), Item("desktop.ini"), Item(".hide", hidden: true));

        var picks = FolderPicture.Choose(Folder, read, 3, CompanionResolver.Default, _ => throw new InvalidOperationException());

        Assert.Equal(new[] { "b.py", "a.config", "Makefile.am" }, Names(picks));
        Assert.All(picks, p => Assert.Equal(FolderContent.Type, p.Content));
        Assert.All(picks, p => Assert.False(p.Inside));
    }

    [Fact]
    public void TypesComeLast_AfterWhatTheSubfoldersShow_ThenFromTheSubfolders() {
        var withPhotos = new FolderRead(new[] { Item("notes.cs") }, new[] { Item("photos") }, Complete: true);
        var codeOnly = new FolderRead(Array.Empty<FolderItem>(), new[] { Item("gdb", age: 1), Item("lldb", age: 2), Item("empty") }, Complete: true);
        var inside = new Dictionary<string, FolderRead> {
            [Folder + @"\photos"] = Read(Item("x.jpg")),
            [Folder + @"\gdb"] = Read(Item("mono-gdb.py")),
            [Folder + @"\lldb"] = Read(Item("mono.py"), Item("monobt.py")),
            [Folder + @"\empty"] = Read(),
        };

        var photos = FolderPicture.Choose(Folder, withPhotos, 3, CompanionResolver.Default, path => inside[path]);
        var code = FolderPicture.Choose(Folder, codeOnly, 3, CompanionResolver.Default, path => inside[path]);

        Assert.Equal(new[] { "x.jpg" }, Names(photos));
        Assert.Equal(new[] { "mono-gdb.py", "mono.py" }, Names(code));
        Assert.All(code, p => Assert.True(p.Inside && p.Content == FolderContent.Type));
    }


    [Fact]
    public void TheKey_ChangesWithAShownFile_AndWithTheLook() {
        var shown = Pick(new[] { Item("a.png", age: 1), Item("b.png", age: 2) }, cards: 3);
        string key = FolderPicture.Signature(Folder, _day, default, 3, shown);

        var edited = shown.Select(p => p.Item.Name == "b.png" ? p with { Item = p.Item with { Size = 999 } } : p).ToList();
        var touched = shown.Select(p => p.Item.Name == "a.png" ? p with { Item = p.Item with { ModifiedUtc = _day } } : p).ToList();

        Assert.Equal(key, FolderPicture.Signature(Folder, _day, default, 3, shown.ToList()));
        Assert.NotEqual(key, FolderPicture.Signature(Folder, _day, default, 3, edited));
        Assert.NotEqual(key, FolderPicture.Signature(Folder, _day, default, 3, touched));
        Assert.NotEqual(key, FolderPicture.Signature(Folder, _day.AddSeconds(1), default, 3, shown));
        Assert.NotEqual(key, FolderPicture.Signature(Folder, _day, new FolderLook(Grid: true), 4, shown));
    }


    private static FolderItem Item(string name, int age = 0, long size = 10, bool hidden = false) {
        return new FolderItem(name, size, _day.AddDays(-age), hidden);
    }

    private static FolderRead Read(params FolderItem[] files) {
        return new FolderRead(files, Array.Empty<FolderItem>(), Complete: true);
    }

    private static IReadOnlyList<FolderPick> Pick(FolderItem[] files, int cards, bool complete = true, string folder = Folder) {
        return FolderPicture.Pick(folder, files, complete, cards, CompanionResolver.Default);
    }

    private static string[] Names(IReadOnlyList<FolderPick> picks) {
        return picks.Select(p => p.Item.Name).ToArray();
    }
}
