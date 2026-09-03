using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class FolderChangesTests {

    private static FileSystemEntry Row(string name, params string[] companions) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\shoot\" + name,
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false,
            Companions: companions.Length == 0 ? null : companions.Select(c => @"C:\shoot\" + c).ToArray());
    }


    // --- Accumulating a burst --------------------------------------------

    [Fact]
    public void Fresh_IsEmpty() {
        var changes = new FolderChanges();

        Assert.True(changes.IsEmpty);
        Assert.False(changes.NeedsRelisting);
    }

    [Fact]
    public void ContentChanges_DoNotAskForARelisting() {
        // The whole point: a sidecar written beside a photograph must not
        // cost the folder its rows.
        var changes = new FolderChanges();

        changes.Note(new DirectoryChange(@"C:\shoot\IMG_1.CR3.pp3", Structural: false));

        Assert.False(changes.IsEmpty);
        Assert.False(changes.NeedsRelisting);
        Assert.Equal(new[] { @"C:\shoot\IMG_1.CR3.pp3" }, changes.ChangedPaths);
    }

    [Fact]
    public void OneStructuralChange_TaintsTheWholeBurst() {
        var changes = new FolderChanges();

        changes.Note(new DirectoryChange(@"C:\shoot\IMG_1.CR3.pp3", Structural: false));
        changes.Note(new DirectoryChange(@"C:\shoot\new.jpg", Structural: true));

        Assert.True(changes.NeedsRelisting);
    }

    [Fact]
    public void AStructuralChange_StillNamesItsPath() {
        // The re-listing needs no names, but the caches keyed by path do:
        // a file replaced under its own name is a new file at an old path.
        var changes = new FolderChanges();

        changes.Note(new DirectoryChange(@"C:\shoot\photo.jpg", Structural: true));

        Assert.True(changes.NeedsRelisting);
        Assert.Equal(new[] { @"C:\shoot\photo.jpg" }, changes.ChangedPaths);
    }

    [Fact]
    public void TheSameFileTwice_IsNotedOnce() {
        // One atomic replace is several events for the same path.
        var changes = new FolderChanges();

        changes.Note(new DirectoryChange(@"C:\shoot\a.pp3", Structural: false));
        changes.Note(new DirectoryChange(@"C:\SHOOT\A.PP3", Structural: false));

        Assert.Single(changes.ChangedPaths);
    }

    [Fact]
    public void Unknown_AsksForARelisting() {
        // The watcher lost track; the folder is in an unknown state, which
        // is exactly when a fresh listing is worth its cost.
        var changes = new FolderChanges();

        changes.Note(DirectoryChange.Unknown);

        Assert.True(changes.NeedsRelisting);
    }

    [Fact]
    public void Clear_ForgetsEverything() {
        var changes = new FolderChanges();
        changes.Note(DirectoryChange.Unknown);
        changes.Note(new DirectoryChange(@"C:\shoot\a.pp3", Structural: false));

        changes.Clear();

        Assert.True(changes.IsEmpty);
        Assert.False(changes.NeedsRelisting);
        Assert.Empty(changes.ChangedPaths);
    }


    // --- Mapping changed files onto rows ---------------------------------

    [Fact]
    public void RowsFor_FindsTheRowThatIsTheFile() {
        var rows = new[] { Row("a.jpg"), Row("b.jpg") };

        var touched = FolderChanges.RowsFor(rows, new[] { @"C:\shoot\b.jpg" });

        Assert.Equal(new[] { "b.jpg" }, touched!.Select(r => r.Name));
    }

    [Fact]
    public void RowsFor_FindsTheRowASidecarBelongsTo() {
        // A .pp3 is not a row of its own — it is what a row shows.
        var rows = new[] { Row("IMG_1.CR3", "IMG_1.CR3.pp3"), Row("IMG_2.CR3") };

        var touched = FolderChanges.RowsFor(rows, new[] { @"C:\shoot\IMG_1.CR3.pp3" });

        Assert.Equal(new[] { "IMG_1.CR3" }, touched!.Select(r => r.Name));
    }

    [Fact]
    public void RowsFor_ReportsEachRowOnce() {
        var rows = new[] { Row("IMG_1.CR3", "IMG_1.CR3.pp3", "IMG_1.xmp") };

        var touched = FolderChanges.RowsFor(
            rows, new[] { @"C:\shoot\IMG_1.CR3.pp3", @"C:\shoot\IMG_1.xmp", @"C:\shoot\IMG_1.CR3" });

        Assert.Single(touched!);
    }

    [Fact]
    public void RowsFor_GivesUpOnAFileItDoesNotKnow() {
        // Null means "re-list": the listing has never heard of this file, and
        // guessing is how a list quietly goes out of sync with the disk.
        var rows = new[] { Row("a.jpg") };

        Assert.Null(FolderChanges.RowsFor(rows, new[] { @"C:\shoot\stranger.txt" }));
    }

    [Fact]
    public void RowsFor_NothingChanged_IsNoRows() {
        var rows = new[] { Row("a.jpg") };

        Assert.Empty(FolderChanges.RowsFor(rows, Array.Empty<string>())!);
    }

    [Fact]
    public void RowsFor_IgnoresCase() {
        var rows = new[] { Row("IMG_1.CR3", "IMG_1.CR3.pp3") };

        Assert.Single(FolderChanges.RowsFor(rows, new[] { @"c:\SHOOT\img_1.cr3.PP3" })!);
    }
}
