using System.Globalization;
using Wander.Core.Companions;
using Wander.Core.FileSystem;
using Wander.Core.Rename;

namespace Wander.Core.Tests;

public class RenamePlannerTests {
    private const string Folder = @"C:\photos";

    private static readonly DateTime _shot = new(2026, 5, 17, 14, 30, 0, DateTimeKind.Local);


    // --- Nothing to do ------------------------------------------------------

    [Fact]
    public void DefaultRules_ChangeNothing() {
        var preview = Preview(RenameRules.Default, Items("a.txt", "b.txt"));

        Assert.True(RenameRules.Default.IsIdentity);
        Assert.All(preview.Rows, row => Assert.Equal(RenameRowStatus.Unchanged, row.Status));
        Assert.Equal(0, preview.Changed);
        Assert.False(preview.CanApply);
        Assert.Empty(preview.Plan());
    }


    // --- Step 1: find / replace ----------------------------------------------

    [Fact]
    public void Find_ReplacesInTheStem_NotInTheExtension() {
        var rules = new RenameRules { Find = "txt", Replace = "doc" };

        var preview = Preview(rules, Items("txt-notes.txt"));

        Assert.Equal("doc-notes.txt", preview.Rows[0].NewName);
    }

    [Fact]
    public void Find_IgnoresCaseByDefault_AndRespectsItWhenAsked() {
        var loose = new RenameRules { Find = "img", Replace = "photo" };
        var strict = loose with { FindIgnoreCase = false };

        Assert.Equal("photo_1.jpg", Preview(loose, Items("IMG_1.jpg")).Rows[0].NewName);
        Assert.Equal(RenameRowStatus.Unchanged, Preview(strict, Items("IMG_1.jpg")).Rows[0].Status);
    }

    [Fact]
    public void Regex_UsesGroups() {
        var rules = new RenameRules { Find = @"^IMG_(\d+)$", Replace = "shot-$1", FindIsRegex = true };

        var preview = Preview(rules, Items("IMG_0042.jpg"));

        Assert.Equal("shot-0042.jpg", preview.Rows[0].NewName);
    }

    [Fact]
    public void Regex_ThatDoesNotParse_IsARuleError_NotACrash() {
        var rules = new RenameRules { Find = "(unclosed", FindIsRegex = true };

        var preview = Preview(rules, Items("a.txt"));

        Assert.Equal(RenamePlanner.RegexErrorKey, preview.RuleErrorKey);
        Assert.NotNull(preview.RuleErrorDetail);
        Assert.False(preview.CanApply);
        Assert.Equal("a.txt", preview.Rows[0].NewName);
    }


    // --- Step 2: template --------------------------------------------------

    [Fact]
    public void Counter_RunsInListOrder_WithStartStepAndWidth() {
        var rules = new RenameRules { Template = "[C]-[N]", CounterStart = 10, CounterStep = 5, CounterWidth = 3 };

        var preview = Preview(rules, Items("b.jpg", "a.jpg"));

        Assert.Equal("010-b.jpg", preview.Rows[0].NewName);
        Assert.Equal("015-a.jpg", preview.Rows[1].NewName);
    }

    [Fact]
    public void CounterWidth_IsCappedAtTheMaximum() {
        // A million from a hand-edited state.json pads to ten digits, not to
        // a million characters.
        var rules = new RenameRules { Template = "[C]", CounterWidth = 1_000_000 };

        var preview = Preview(rules, Items("a.txt"));

        Assert.Equal("0000000001.txt", preview.Rows[0].NewName);
        Assert.Equal(10, RenameRules.MaxCounterWidth);
    }

    [Fact]
    public void Date_PrintsTheModifiedDate_InTheGivenFormat() {
        var modified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var items = new[] { new RenameItem(Path.Combine(Folder, "a.jpg"), false, modified) };

        var plain = Preview(new RenameRules { Template = "[D]" }, items);
        var custom = Preview(new RenameRules { Template = "[D:yyyyMMdd_HHmm]" }, items);

        Assert.Equal(modified.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jpg", plain.Rows[0].NewName);
        Assert.Equal(modified.ToLocalTime().ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".jpg", custom.Rows[0].NewName);
    }

    [Fact]
    public void ShotDate_ComesFromTheContext_AndFallsBackToModified() {
        var modified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var items = new[] {
            new RenameItem(Path.Combine(Folder, "a.jpg"), false, modified),
            new RenameItem(Path.Combine(Folder, "b.jpg"), false, modified),
        };
        var context = new RenameContext(_ => false, ShotDate: path => path.EndsWith("a.jpg") ? _shot : null);

        var preview = RenamePlanner.Preview(new RenameRules { Template = "[X]" }, items, context);

        Assert.Equal("2026-05-17.jpg", preview.Rows[0].NewName);
        Assert.Equal(modified.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jpg", preview.Rows[1].NewName);
    }

    [Fact]
    public void ShotDate_IsNotAskedFor_UnlessTheTemplateUsesIt() {
        int asked = 0;
        var context = new RenameContext(_ => false, ShotDate: _ => { asked++; return _shot; });

        RenamePlanner.Preview(new RenameRules { Template = "[C]" }, Items("a.jpg"), context);

        Assert.Equal(0, asked);
    }

    [Fact]
    public void BadDateFormat_IsARuleError() {
        var preview = Preview(new RenameRules { Template = "[D:yyyy-MM-dd HH:mm:ss.fffffffff]" }, Items("a.jpg"));

        Assert.Equal(RenamePlanner.DateFormatErrorKey, preview.RuleErrorKey);
    }

    [Fact]
    public void ParentFolder_AndUnknownTokens() {
        var preview = Preview(new RenameRules { Template = "[P]-[N]-[Q]" }, Items("a.jpg"));

        Assert.Equal("photos-a-[Q].jpg", preview.Rows[0].NewName);
    }


    // --- Step 3: case ------------------------------------------------------

    [Fact]
    public void Case_AppliesToNameAndExtensionSeparately() {
        var rules = new RenameRules { NameCase = NameCase.Sentence, ExtensionCase = NameCase.Lower };

        var preview = Preview(rules, Items("MY HOLIDAY.JPG"));

        Assert.Equal("My holiday.jpg", preview.Rows[0].NewName);
        Assert.Equal(RenameRowStatus.Renamed, preview.Rows[0].Status);
    }

    [Fact]
    public void CaseOnlyChange_IsARename_NotACollisionWithItself() {
        var rules = new RenameRules { NameCase = NameCase.Upper };
        var context = new RenameContext(path => path.EndsWith("a.txt", StringComparison.OrdinalIgnoreCase));

        var preview = RenamePlanner.Preview(rules, Items("a.txt"), context);

        Assert.Equal("A.txt", preview.Rows[0].NewName);
        Assert.Equal(RenameRowStatus.Renamed, preview.Rows[0].Status);
    }

    [Fact]
    public void Folder_HasNoExtension() {
        var items = new[] { new RenameItem(Path.Combine(Folder, "my.folder"), true, DateTime.UnixEpoch) };

        var preview = Preview(new RenameRules { Template = "[N]-x", ExtensionCase = NameCase.Upper }, items);

        Assert.Equal("my.folder-x", preview.Rows[0].NewName);
    }


    // --- What cannot be done ------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a:b")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    public void InvalidNames_AreRefused(string name) {
        Assert.False(RenamePlanner.IsValidName(name));
    }

    [Fact]
    public void InvalidName_IsAConflict() {
        var preview = Preview(new RenameRules { Find = "a", Replace = "a:b" }, Items("a.txt"));

        Assert.Equal(RenameRowStatus.InvalidName, preview.Rows[0].Status);
        Assert.Equal(1, preview.Conflicts);
        Assert.False(preview.CanApply);
    }

    [Fact]
    public void TwoRowsLandingOnOneName_AreBothDuplicates() {
        var preview = Preview(new RenameRules { Template = "same" }, Items("a.txt", "b.txt"));

        Assert.All(preview.Rows, row => Assert.Equal(RenameRowStatus.DuplicateInBatch, row.Status));
        Assert.Equal(2, preview.Conflicts);
    }

    [Fact]
    public void RenamingOntoAnUnchangedNeighbour_IsADuplicate() {
        var preview = Preview(new RenameRules { Find = "a", Replace = "b" }, Items("a.txt", "b.txt"));

        Assert.Equal(RenameRowStatus.DuplicateInBatch, preview.Rows[0].Status);
        Assert.Equal(RenameRowStatus.Unchanged, preview.Rows[1].Status);
    }

    [Fact]
    public void NameTakenOutsideTheBatch_Collides() {
        var context = new RenameContext(path => path.EndsWith("taken.txt"));

        var preview = RenamePlanner.Preview(new RenameRules { Template = "taken" }, Items("a.txt"), context);

        Assert.Equal(RenameRowStatus.Collides, preview.Rows[0].Status);
    }

    [Fact]
    public void Swap_IsNotACollision() {
        // Both names exist on disk - they are the batch's own. The counter
        // runs in list order, so "2" becomes 1 and "1" becomes 2.
        var context = new RenameContext(path => path.EndsWith("1.txt") || path.EndsWith("2.txt"));

        var preview = RenamePlanner.Preview(new RenameRules { Template = "[C]" }, Items("2.txt", "1.txt"), context);

        Assert.All(preview.Rows, row => Assert.Equal(RenameRowStatus.Renamed, row.Status));
        Assert.True(preview.CanApply);
    }

    [Fact]
    public void Plan_ListsRenamedRowsInOrder_CompanionsBehindTheirFile() {
        var items = new[] {
            new RenameItem(Path.Combine(Folder, "IMG_1.CR2"), false, DateTime.UnixEpoch,
                new[] { Path.Combine(Folder, "IMG_1.CR2.pp3") }),
            new RenameItem(Path.Combine(Folder, "IMG_2.CR2"), false, DateTime.UnixEpoch),
        };
        var context = new RenameContext(_ => false, CompanionResolver.Default);

        var preview = RenamePlanner.Preview(new RenameRules { Find = "IMG", Replace = "shot" }, items, context);

        Assert.Equal(new[] {
            (Path.Combine(Folder, "IMG_1.CR2"), "shot_1.CR2"),
            (Path.Combine(Folder, "IMG_1.CR2.pp3"), "shot_1.CR2.pp3"),
            (Path.Combine(Folder, "IMG_2.CR2"), "shot_2.CR2"),
        }, preview.Plan());
    }

    [Fact]
    public void CompanionLandingOnAnExistingFile_MakesItsRowCollide() {
        var items = new[] {
            new RenameItem(Path.Combine(Folder, "IMG_1.CR2"), false, DateTime.UnixEpoch,
                new[] { Path.Combine(Folder, "IMG_1.CR2.pp3") }),
        };
        var context = new RenameContext(path => path.EndsWith("shot_1.CR2.pp3"), CompanionResolver.Default);

        var preview = RenamePlanner.Preview(new RenameRules { Find = "IMG", Replace = "shot" }, items, context);

        Assert.Equal(RenameRowStatus.Collides, preview.Rows[0].Status);
    }


    // --- The gate ------------------------------------------------------------

    [Fact]
    public void Gate_WantsTwoOrMore_OfOneKind() {
        Assert.Equal(BatchRenameKind.TooFew, BatchRenameGate.Classify(new[] { File("a") }));
        Assert.Equal(BatchRenameKind.Files, BatchRenameGate.Classify(new[] { File("a"), File("b") }));
        Assert.Equal(BatchRenameKind.Folders, BatchRenameGate.Classify(new[] { Dir("a"), Dir("b") }));
        Assert.Equal(BatchRenameKind.Mixed, BatchRenameGate.Classify(new[] { File("a"), Dir("b") }));
    }


    // --- Helpers -------------------------------------------------------------

    private static RenamePreview Preview(RenameRules rules, IReadOnlyList<RenameItem> items) {
        return RenamePlanner.Preview(rules, items, new RenameContext(_ => false));
    }

    private static RenameItem[] Items(params string[] names) {
        return names.Select(name => new RenameItem(Path.Combine(Folder, name), false, DateTime.UnixEpoch)).ToArray();
    }

    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, Path.Combine(Folder, name), EntryKind.File, 1, DateTime.UnixEpoch, false, false, false, false);
    }

    private static FileSystemEntry Dir(string name) {
        return new FileSystemEntry(name, Path.Combine(Folder, name), EntryKind.Directory, null, DateTime.UnixEpoch, false, false, false, false);
    }
}
