using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class CompanionResolverTests {
    private const string Folder = @"C:\assets";


    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, System.IO.Path.Combine(Folder, name), EntryKind.File, 1, DateTime.MinValue, false, false, false, false);
    }

    private static FileSystemEntry Dir(string name) {
        return new FileSystemEntry(name, System.IO.Path.Combine(Folder, name), EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);
    }

    private static CompanionResolver XmpResolver() {
        return new CompanionResolver(new[] { new CompanionRule(".xmp", CompanionNaming.Replaced, "XMP") });
    }


    // --- Collapse: the appended pattern (Unity, RawTherapee) -----------

    [Fact]
    public void Collapse_FoldsAppendedSidecar_IntoItsMainFile() {
        var result = CompanionResolver.Default.Collapse(new[] { File("Sprite.png"), File("Sprite.png.meta") });

        var only = Assert.Single(result);
        Assert.Equal("Sprite.png", only.Name);
        Assert.True(only.HasCompanions);
        Assert.Equal(@"C:\assets\Sprite.png.meta", Assert.Single(only.Companions!));
    }

    [Fact]
    public void Collapse_FoldsSeveralSidecars_OfOneFile() {
        var result = CompanionResolver.Default.Collapse(new[] {
            File("IMG_1234.CR2"), File("IMG_1234.CR2.pp3"), File("IMG_1234.CR2.meta"),
        });

        var only = Assert.Single(result);
        Assert.Equal(2, only.Companions!.Count);
    }

    [Fact]
    public void Collapse_FoldsFolderSidecar() {
        // Unity writes Scripts.meta next to the *folder* Scripts.
        var result = CompanionResolver.Default.Collapse(new[] { Dir("Scripts"), File("Scripts.meta") });

        var only = Assert.Single(result);
        Assert.Equal("Scripts", only.Name);
        Assert.True(only.HasCompanions);
    }

    [Fact]
    public void Collapse_LeavesOrphanSidecar_Visible() {
        // A .meta whose asset is gone is exactly what the user needs to see.
        var result = CompanionResolver.Default.Collapse(new[] { File("Gone.png.meta") });

        Assert.Equal("Gone.png.meta", Assert.Single(result).Name);
        Assert.False(result[0].HasCompanions);
    }

    [Fact]
    public void Collapse_DoesNotFoldIntoARowThatIsItselfFolded() {
        // a.png.meta.pp3 belongs to a.png.meta, which is itself folded into
        // a.png. Folding both would make the .pp3 vanish from the listing
        // entirely; it stays visible on its own line instead.
        var result = CompanionResolver.Default.Collapse(new[] {
            File("a.png"), File("a.png.meta"), File("a.png.meta.pp3"),
        });

        Assert.Equal(new[] { "a.png", "a.png.meta.pp3" }, result.Select(e => e.Name));
    }

    [Fact]
    public void Collapse_PreservesOrder_AndUntouchedEntries() {
        var result = CompanionResolver.Default.Collapse(new[] {
            Dir("Sub"), File("a.txt"), File("Sprite.png"), File("Sprite.png.meta"), File("z.txt"),
        });

        Assert.Equal(new[] { "Sub", "a.txt", "Sprite.png", "z.txt" }, result.Select(e => e.Name));
    }

    [Fact]
    public void Collapse_ReturnsInputInstance_WhenNothingToFold() {
        var input = new[] { File("a.txt"), File("b.txt") };

        Assert.Same(input, CompanionResolver.Default.Collapse(input));
    }


    // --- Collapse: the replaced pattern (XMP, AAE) ---------------------

    [Fact]
    public void Collapse_FoldsReplacedSidecar_ByStem() {
        var result = XmpResolver().Collapse(new[] { File("IMG_1234.CR2"), File("IMG_1234.xmp") });

        var only = Assert.Single(result);
        Assert.Equal("IMG_1234.CR2", only.Name);
        Assert.True(only.HasCompanions);
    }

    [Fact]
    public void Collapse_LeavesAmbiguousReplacedSidecar_Alone() {
        // Two candidates share the stem and neither is a RAW - attaching to
        // a guess would hide a sidecar under the wrong file.
        var result = XmpResolver().Collapse(new[] { File("IMG.png"), File("IMG.jpg"), File("IMG.xmp") });

        Assert.Equal(3, result.Count);
        Assert.All(result, e => Assert.False(e.HasCompanions));
    }

    [Fact]
    public void Collapse_GivesTheSidecarToTheRaw_WhenAJpegSharesTheStem() {
        // The camera's RAW+JPEG pair, or a JPEG made from the RAW: the XMP
        // is the RAW's. This is what a converted preview beside its RAW
        // looks like, and the rating must stay on the RAW.
        var result = XmpResolver().Collapse(new[] { File("IMG.CR2"), File("IMG.jpg"), File("IMG.xmp") });

        Assert.Equal(2, result.Count);
        var raw = Assert.Single(result, e => e.Name == "IMG.CR2");
        Assert.Equal(new[] { @"C:\assets\IMG.xmp" }, raw.Companions);
        Assert.False(Assert.Single(result, e => e.Name == "IMG.jpg").HasCompanions);
    }

    [Fact]
    public void Collapse_TwoRaws_StayAmbiguous() {
        var result = XmpResolver().Collapse(new[] { File("IMG.CR2"), File("IMG.DNG"), File("IMG.xmp") });

        Assert.Equal(3, result.Count);
        Assert.All(result, e => Assert.False(e.HasCompanions));
    }

    [Fact]
    public void Group_GivesTheSidecarToTheRaw_WhenAJpegSharesTheStem() {
        var groups = XmpResolver().Group(new[] { @"C:\p\IMG.jpg", @"C:\p\IMG.CR2", @"C:\p\IMG.xmp" });

        Assert.Equal(2, groups.Count);
        var raw = Assert.Single(groups, g => g.Primary == @"C:\p\IMG.CR2");
        Assert.Equal(new[] { @"C:\p\IMG.xmp" }, raw.Companions);
        Assert.Empty(Assert.Single(groups, g => g.Primary == @"C:\p\IMG.jpg").Companions);
    }

    [Fact]
    public void FindCompanions_LeavesTheXmpToTheRaw_WhenAJpegSharesTheStem() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\p\IMG.CR2"] = Array.Empty<byte>();
        fs.Files[@"C:\p\IMG.jpg"] = Array.Empty<byte>();
        fs.Files[@"C:\p\IMG.xmp"] = Array.Empty<byte>();

        Assert.Empty(XmpResolver().FindCompanions(@"C:\p\IMG.jpg", fs));
        Assert.Equal(@"C:\p\IMG.xmp", Assert.Single(XmpResolver().FindCompanions(@"C:\p\IMG.CR2", fs)));
        // Alone, the JPEG owns it.
        fs.Files.Remove(@"C:\p\IMG.CR2");
        Assert.Equal(@"C:\p\IMG.xmp", Assert.Single(XmpResolver().FindCompanions(@"C:\p\IMG.jpg", fs)));
    }


    // --- FindCompanions / RenamePlan (the operation side) -------------

    [Fact]
    public void FindCompanions_ReturnsOnlyWhatExistsOnDisk() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\assets\Sprite.png"] = Array.Empty<byte>();
        fs.Files[@"C:\assets\Sprite.png.meta"] = Array.Empty<byte>();

        var found = CompanionResolver.Default.FindCompanions(@"C:\assets\Sprite.png", fs);

        Assert.Equal(@"C:\assets\Sprite.png.meta", Assert.Single(found));
    }

    [Fact]
    public void FindCompanions_IsEmpty_WhenNoSidecarExists() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\assets\Sprite.png"] = Array.Empty<byte>();

        Assert.Empty(CompanionResolver.Default.FindCompanions(@"C:\assets\Sprite.png", fs));
    }

    [Fact]
    public void RenamePlan_RenamesSidecar_ToMatchTheNewName() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\assets\Sprite.png"] = Array.Empty<byte>();
        fs.Files[@"C:\assets\Sprite.png.meta"] = Array.Empty<byte>();

        var plan = CompanionResolver.Default.RenamePlan(@"C:\assets\Sprite.png", "Ship.png", fs);

        Assert.Equal(2, plan.Count);
        Assert.Equal((@"C:\assets\Sprite.png", "Ship.png"), plan[0]);
        Assert.Equal((@"C:\assets\Sprite.png.meta", "Ship.png.meta"), plan[1]);
    }

    [Fact]
    public void RenamePlan_IsJustTheMainFile_WhenItHasNoSidecars() {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\assets\Sprite.png"] = Array.Empty<byte>();

        Assert.Single(CompanionResolver.Default.RenamePlan(@"C:\assets\Sprite.png", "Ship.png", fs));
    }

    [Fact]
    public void RuleFor_RecognisesKnownSuffixes_AndNothingElse() {
        Assert.Equal(".meta", CompanionResolver.Default.RuleFor(@"C:\a\Sprite.png.meta")?.Suffix);
        Assert.Equal(".pp3", CompanionResolver.Default.RuleFor(@"C:\a\IMG.CR2.pp3")?.Suffix);
        Assert.Null(CompanionResolver.Default.RuleFor(@"C:\a\Sprite.png"));
    }
}
