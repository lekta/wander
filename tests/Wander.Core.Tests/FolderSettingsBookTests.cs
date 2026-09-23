using Wander.Core.Folders;

namespace Wander.Core.Tests;

public class FolderSettingsBookTests {
    private const string Photos = @"D:\shoot\photos";
    private const string Docs = @"D:\docs";
    private static readonly DateTime _created = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly _monday = new(2026, 9, 21);
    private static readonly DateOnly _tuesday = new(2026, 9, 22);


    private static FolderRecord Pin(string path, ViewMode view, DateOnly? visited = null, DateTime? created = null) {
        return new FolderRecord(path, created, visited ?? _monday, view);
    }


    // --- Find, SetView, Touch ---

    [Fact]
    public void Find_IgnoresCaseAndTrailingSeparator() {
        var book = new FolderSettingsBook(new[] { Pin(Photos, ViewMode.Gallery) });

        Assert.Equal(ViewMode.Gallery, book.Find(@"d:\SHOOT\Photos\")?.View);
        Assert.Null(book.Find(Docs));
    }

    [Fact]
    public void SetView_PinsAndReportsTheChange() {
        var book = new FolderSettingsBook();

        Assert.True(book.SetView(Photos, ViewMode.Tiles, _created, _monday));
        Assert.False(book.SetView(Photos, ViewMode.Tiles, _created, _monday));

        var record = book.Find(Photos);
        Assert.Equal(ViewMode.Tiles, record?.View);
        Assert.Equal(_created, record?.CreatedUtc);
        Assert.Equal(_monday, record?.LastVisit);
    }

    [Fact]
    public void SetView_KeepsTheCreationTimeItAlreadyKnows() {
        var book = new FolderSettingsBook(new[] { Pin(Photos, ViewMode.Tiles, created: _created) });

        book.SetView(Photos, ViewMode.Gallery, createdUtc: null, _tuesday);

        Assert.Equal(_created, book.Find(Photos)?.CreatedUtc);
        Assert.Equal(_tuesday, book.Find(Photos)?.LastVisit);
    }

    [Fact]
    public void SetView_Null_DropsARecordWithNothingElseInIt() {
        var book = new FolderSettingsBook(new[] { Pin(Photos, ViewMode.Tiles) });

        Assert.True(book.SetView(Photos, null, null, _monday));
        Assert.Null(book.Find(Photos));
        Assert.Equal(0, book.Count);
        Assert.False(book.SetView(Photos, null, null, _monday));
    }

    [Fact]
    public void Touch_MovesAKnownFolderToTodayAndLearnsItsCreationTime() {
        var book = new FolderSettingsBook(new[] { Pin(Photos, ViewMode.Tiles) });

        Assert.True(book.Touch(Photos, _created, _tuesday));

        var record = book.Find(Photos);
        Assert.Equal(_tuesday, record?.LastVisit);
        Assert.Equal(_created, record?.CreatedUtc);
    }

    [Fact]
    public void Touch_SameDayAndNothingNew_IsNotAChange() {
        var book = new FolderSettingsBook(new[] { Pin(Photos, ViewMode.Tiles, _tuesday, _created) });

        Assert.False(book.Touch(Photos, _created, _tuesday));
        // A clock set back does not move the visit backwards either.
        Assert.False(book.Touch(Photos, _created, _monday));
    }

    [Fact]
    public void Touch_DoesNotCreateARecordForAFolderWithNothingToSay() {
        // The book holds pins, not a diary of every folder opened.
        var book = new FolderSettingsBook();

        Assert.False(book.Touch(Docs, _created, _monday));
        Assert.Equal(0, book.Count);
    }

    // --- Loading ---

    [Fact]
    public void Constructor_DropsEmptyRecordsAndKeepsTheLaterOfDuplicates() {
        var book = new FolderSettingsBook(new[] {
            new FolderRecord(Docs, null, _monday, null),
            Pin(Photos, ViewMode.Tiles, _monday),
            Pin(@"D:\SHOOT\PHOTOS\", ViewMode.Gallery, _tuesday),
        });

        Assert.Equal(1, book.Count);
        Assert.Equal(ViewMode.Gallery, book.Find(Photos)?.View);
    }

    [Fact]
    public void Records_ComeLatestVisitFirst() {
        var book = new FolderSettingsBook(new[] {
            Pin(Docs, ViewMode.Details, _monday),
            Pin(Photos, ViewMode.Gallery, _tuesday),
        });

        Assert.Equal(new[] { Photos, Docs }, book.Records.Select(r => r.Path));
    }

    // --- Ageing ---

    [Fact]
    public void Trim_DropsTheLeastRecentlyVisitedFirst() {
        var book = new FolderSettingsBook(capacity: 2);
        book.SetView(@"D:\a", ViewMode.Tiles, null, new DateOnly(2026, 1, 1));
        book.SetView(@"D:\b", ViewMode.Tiles, null, new DateOnly(2026, 6, 1));
        book.SetView(@"D:\c", ViewMode.Tiles, null, new DateOnly(2026, 3, 1));

        Assert.Equal(2, book.Count);
        Assert.Null(book.Find(@"D:\a"));
        Assert.NotNull(book.Find(@"D:\b"));
        Assert.NotNull(book.Find(@"D:\c"));
    }

    [Fact]
    public void Trim_AppliesToWhatWasLoaded() {
        var book = new FolderSettingsBook(new[] {
            Pin(@"D:\a", ViewMode.Tiles, new DateOnly(2026, 1, 1)),
            Pin(@"D:\b", ViewMode.Tiles, new DateOnly(2026, 6, 1)),
        }, capacity: 1);

        Assert.Equal(new[] { @"D:\b" }, book.Records.Select(r => r.Path));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FolderSettingsBook(capacity: 0));
    }

    // --- Following a rename Wander made ---

    [Fact]
    public void Follow_MovesTheFolderAndEverythingUnderIt() {
        var book = new FolderSettingsBook(new[] {
            Pin(@"D:\shoot", ViewMode.Tiles),
            Pin(@"D:\shoot\2026", ViewMode.Gallery),
            Pin(@"D:\shoot-old", ViewMode.Details),
        });

        Assert.Equal(2, book.Follow(@"D:\shoot", @"E:\archive\shoot"));

        Assert.Equal(ViewMode.Tiles, book.Find(@"E:\archive\shoot")?.View);
        Assert.Equal(ViewMode.Gallery, book.Find(@"E:\archive\shoot\2026")?.View);
        Assert.Null(book.Find(@"D:\shoot"));
        // A sibling that merely starts with the same letters stays.
        Assert.Equal(ViewMode.Details, book.Find(@"D:\shoot-old")?.View);
    }

    [Fact]
    public void Follow_TheMovedRecordReplacesOneAlreadyAtTheNewPath() {
        var book = new FolderSettingsBook(new[] {
            Pin(@"D:\a", ViewMode.Tiles),
            Pin(@"D:\b", ViewMode.Gallery),
        });

        book.Follow(@"D:\a", @"D:\b");

        Assert.Equal(1, book.Count);
        Assert.Equal(ViewMode.Tiles, book.Find(@"D:\b")?.View);
    }

    // --- Picking up a rename made outside Wander ---

    [Fact]
    public void AdoptCandidates_SameCreationTimeOnTheSameVolumeAtAnotherPath() {
        var records = new[] {
            Pin(@"D:\shoot\old-name", ViewMode.Gallery, created: _created),
            Pin(@"E:\elsewhere", ViewMode.Gallery, created: _created),
            Pin(@"D:\other", ViewMode.Tiles, created: _created.AddSeconds(1)),
            Pin(@"D:\shoot\new-name", ViewMode.Tiles, created: _created),
        };

        var candidates = FolderSettingsBook.AdoptCandidates(records, @"D:\shoot\new-name", _created);

        Assert.Equal(new[] { @"D:\shoot\old-name" }, candidates.Select(r => r.Path));
    }

    [Fact]
    public void Adopt_ReKeysTheOneCandidateThatIsGone() {
        var book = new FolderSettingsBook(new[] { Pin(@"D:\shoot\old-name", ViewMode.Gallery, created: _created) });

        string? former = book.Adopt(@"D:\shoot\new-name", _created, gone: new[] { @"D:\shoot\old-name" });

        Assert.Equal(@"D:\shoot\old-name", former);
        Assert.Equal(ViewMode.Gallery, book.Find(@"D:\shoot\new-name")?.View);
        Assert.Null(book.Find(@"D:\shoot\old-name"));
    }

    [Fact]
    public void Adopt_TwoGoneCandidates_AdoptsNeither() {
        // A copy that kept its dates (robocopy /DCOPY:T, an unpacker) gives
        // two folders the same birthday; guessing between them would pin a
        // view to the wrong one.
        var book = new FolderSettingsBook(new[] {
            Pin(@"D:\a", ViewMode.Gallery, created: _created),
            Pin(@"D:\b", ViewMode.Tiles, created: _created),
        });

        Assert.Null(book.Adopt(@"D:\c", _created, gone: new[] { @"D:\a", @"D:\b" }));
        Assert.Equal(2, book.Count);
    }

    [Fact]
    public void Adopt_CandidateStillOnDisk_IsNotAdopted() {
        var book = new FolderSettingsBook(new[] { Pin(@"D:\a", ViewMode.Gallery, created: _created) });

        Assert.Null(book.Adopt(@"D:\c", _created, gone: Array.Empty<string>()));
        Assert.NotNull(book.Find(@"D:\a"));
    }

    [Fact]
    public void Adopt_DoesNothingForAFolderThatAlreadyHasARecord() {
        var book = new FolderSettingsBook(new[] {
            Pin(@"D:\a", ViewMode.Gallery, created: _created),
            Pin(@"D:\c", ViewMode.Tiles, created: _created),
        });

        Assert.Null(book.Adopt(@"D:\c", _created, gone: new[] { @"D:\a" }));
        Assert.Equal(ViewMode.Tiles, book.Find(@"D:\c")?.View);
    }
}
