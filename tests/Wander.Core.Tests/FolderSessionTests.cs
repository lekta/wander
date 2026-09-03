using Wander.Core.FileSystem;
using Wander.Core.Listing;

namespace Wander.Core.Tests;

/// <summary>
/// The invariants of "the folder being looked at" — the ones that used to be
/// checkable only by hand (MANUAL-CHECKS, «Скорость и отзывчивость»), now
/// pinned where a regression trips them before the app is even launched.
/// </summary>
public class FolderSessionTests {

    private static FileSystemEntry Row(string name, params string[] companions) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\folder\" + name,
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false,
            Companions: companions.Length == 0 ? null : companions.Select(c => @"C:\folder\" + c).ToArray());
    }


    // --- Epochs: who is answering about the folder on screen -------------

    [Fact]
    public void NavigationDuringAnUnfinishedListing_MakesTheFirstAnswerStale() {
        // The regression class "optimising the load broke navigation": the
        // user walks on before the first folder finished listing, and the
        // late answer must not land on the folder they moved to.
        var session = new FolderSession();

        int first = session.BeginListing(@"C:\slow", out _);
        int second = session.BeginListing(@"C:\next", out _);

        Assert.False(session.IsCurrent(first));
        Assert.True(session.IsCurrent(second));
    }

    [Fact]
    public void RelistingTheSameFolder_AlsoMakesTheFirstAnswerStale() {
        // Same folder is not "same listing": an F5 during a slow load must
        // win over the load it replaced, or the rows go backwards in time.
        var session = new FolderSession();

        int first = session.BeginListing(@"C:\folder", out _);
        int second = session.BeginListing(@"C:\folder", out _);

        Assert.False(session.IsCurrent(first));
        Assert.True(session.IsCurrent(second));
    }

    [Fact]
    public void SearchTakingTheListOver_DropsEveryListingInFlight() {
        var session = new FolderSession();
        int listing = session.BeginListing(@"C:\folder", out _);

        session.InvalidateListings();

        Assert.False(session.IsCurrent(listing));
    }

    [Fact]
    public void WalkingIntoADifferentFolder_IsAnArrival() {
        // The view mode is chosen for an arrival, not for every F5.
        var session = new FolderSession();
        session.BeginListing(@"C:\one", out _);
        session.NoteListed(@"C:\one");

        session.BeginListing(@"C:\two", out bool arriving);

        Assert.True(arriving);
        Assert.Null(session.ListedPath);
    }

    [Fact]
    public void RelistingTheFolderOnScreen_IsNotAnArrival() {
        var session = new FolderSession();
        session.BeginListing(@"C:\one", out _);
        session.NoteListed(@"C:\one");

        session.BeginListing(@"C:\ONE", out bool arriving);

        Assert.False(arriving);
        Assert.Equal(@"C:\one", session.ListedPath);
    }


    // --- The arrival intent: one slot, replaced, applied once -------------

    [Fact]
    public void SettingAnIntent_ReplacesThePendingOne() {
        // Two callers, one slot: the second caller wins because it spoke
        // later — a decision, not an accident of line order.
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(@"C:\folder", new[] { @"C:\folder\a.txt" }));
        session.SetArrival(ArrivalIntent.Rows(@"C:\folder", new[] { @"C:\folder\b.txt" }));

        var decision = session.DecideArrival(@"C:\folder", new[] { Row("a.txt"), Row("b.txt") });

        Assert.Equal(ArrivalOutcome.SelectRows, decision.Outcome);
        Assert.Equal(@"C:\folder\b.txt", decision.Rows.Single().FullPath);
    }

    [Fact]
    public void NavigatingSomewhereElse_DropsTheIntent() {
        // An intent that outlived its folder would select something the
        // user is no longer looking at.
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(@"C:\folder", new[] { @"C:\folder\a.txt" }));

        session.OnNavigating(@"C:\elsewhere", selectedPath: null);

        Assert.Null(session.Arrival);
    }

    [Fact]
    public void NavigatingWhereTheIntentPoints_KeepsIt() {
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(@"C:\folder", new[] { @"C:\folder\a.txt" }));

        session.OnNavigating(@"C:\FOLDER", selectedPath: null);

        Assert.NotNull(session.Arrival);
    }

    [Fact]
    public void SomebodyElsesListing_LeavesTheIntentWaiting() {
        // The listing that landed is not the intent's folder: neither apply
        // nor consume — its own listing is still coming.
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(@"C:\target", new[] { @"C:\target\a.txt" }));

        var decision = session.DecideArrival(@"C:\other", new[] { Row("a.txt") });

        Assert.Equal(ArrivalOutcome.None, decision.Outcome);
        Assert.NotNull(session.Arrival);
    }

    [Fact]
    public void AnEmptyListing_IsNotAnAnswer() {
        // The gap between leaving one folder and the next one listing:
        // consuming the intent on the empty in-between is what used to stop
        // "up one level" highlighting the folder it came out of.
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(@"C:\folder", new[] { @"C:\folder\a.txt" }));

        var decision = session.DecideArrival(@"C:\folder", Array.Empty<FileSystemEntry>());

        Assert.Equal(ArrivalOutcome.None, decision.Outcome);
        Assert.NotNull(session.Arrival);
    }

    [Fact]
    public void SelectingTheFolderItself_WorksOnAnEmptyListing() {
        // A folder is never a row in its own listing — emptiness is no
        // obstacle, and the intent is consumed.
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Folder(@"C:\folder"));

        var decision = session.DecideArrival(@"C:\folder", Array.Empty<FileSystemEntry>());

        Assert.Equal(ArrivalOutcome.SelectFolder, decision.Outcome);
        Assert.Equal(@"C:\folder", decision.FolderPath);
        Assert.Null(session.Arrival);
    }

    [Fact]
    public void RowsThatLeftTheFolder_ConsumeTheIntentWithNothingFound() {
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(@"C:\folder", new[] { @"C:\folder\gone.txt" }));

        var decision = session.DecideArrival(@"C:\folder", new[] { Row("a.txt") });

        Assert.Equal(ArrivalOutcome.NothingFound, decision.Outcome);
        Assert.Null(session.Arrival);
    }

    [Fact]
    public void TheIntentIsConsumedByItsListing_NotByTheNextOne() {
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(@"C:\folder", new[] { @"C:\folder\a.txt" }));

        var first = session.DecideArrival(@"C:\folder", new[] { Row("a.txt") });
        var second = session.DecideArrival(@"C:\folder", new[] { Row("a.txt") });

        Assert.Equal(ArrivalOutcome.SelectRows, first.Outcome);
        Assert.Equal(ArrivalOutcome.None, second.Outcome);
    }

    [Fact]
    public void RenameOpensOnlyOnTheRowItWasAskedFor() {
        // A listing that lands for some other reason must not open an
        // editor under the user's hands.
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(
            @"C:\folder", new[] { @"C:\folder\made.txt" }, renameTarget: @"C:\folder\made.txt"));

        var decision = session.DecideArrival(@"C:\folder", new[] { Row("made.txt") });

        Assert.Equal(@"C:\folder\made.txt", decision.RenameTarget);
    }

    [Fact]
    public void RenameDoesNotOpen_WhenAnotherRowLandedFirst() {
        // The editor was requested for a row the listing did not bring
        // back first — opening it on a different row would edit the wrong
        // file.
        var session = new FolderSession();
        session.SetArrival(ArrivalIntent.Rows(
            @"C:\folder",
            new[] { @"C:\folder\b.txt", @"C:\folder\a.txt" },
            renameTarget: @"C:\folder\b.txt"));

        // The listing only contains a.txt — found[0] is not the rename
        // target, so no editor.
        var decision = session.DecideArrival(@"C:\folder", new[] { Row("a.txt") });

        Assert.Equal(ArrivalOutcome.SelectRows, decision.Outcome);
        Assert.Null(decision.RenameTarget);
    }


    // --- Planning the default arrival -------------------------------------

    [Fact]
    public void GoingUp_HighlightsTheFolderWeCameOutOf() {
        // What makes Backspace a way to look around rather than a way to
        // lose your place.
        var session = new FolderSession();
        session.BeginListing(@"C:\parent\child", out _);
        session.NoteListed(@"C:\parent\child");

        session.OnNavigating(@"C:\parent", selectedPath: null);

        var intent = session.Arrival;
        Assert.NotNull(intent);
        Assert.Equal(@"C:\parent\child", intent!.Paths.Single());
    }

    [Fact]
    public void WalkingBackIn_LandsOnTheRememberedRow() {
        var session = new FolderSession();
        session.BeginListing(@"C:\folder", out _);
        session.NoteListed(@"C:\folder");

        // Leaving: the selection is noted. Coming back: it is the plan.
        session.OnNavigating(@"C:\elsewhere", selectedPath: @"C:\folder\b.txt");
        session.NoteListed(@"C:\elsewhere");
        session.OnNavigating(@"C:\folder", selectedPath: null);

        var intent = session.Arrival;
        Assert.NotNull(intent);
        Assert.Equal(@"C:\folder\b.txt", intent!.Paths.Single());
    }

    [Fact]
    public void ACallerWhoKnewBetter_IsLeftAlone() {
        // A rename knows the new name; the default plan must not overwrite
        // it with "whatever was selected".
        var session = new FolderSession();
        session.BeginListing(@"C:\parent\child", out _);
        session.NoteListed(@"C:\parent\child");
        session.SetArrival(ArrivalIntent.Rows(@"C:\parent", new[] { @"C:\parent\renamed.txt" }));

        session.OnNavigating(@"C:\parent", selectedPath: null);

        Assert.Equal(@"C:\parent\renamed.txt", session.Arrival!.Paths.Single());
    }

    [Fact]
    public void SelectionMemory_IsBounded() {
        // A long session walks through a lot of folders; the oldest memory
        // goes, and the newest sixty-four stay.
        var session = new FolderSession();

        for (int i = 0; i < 70; i++) {
            session.NoteListed($@"C:\folder{i}");
            session.OnNavigating(@"C:\elsewhere", selectedPath: $@"C:\folder{i}\row.txt");
        }

        session.NoteListed(@"C:\elsewhere");
        session.OnNavigating(@"C:\folder0", selectedPath: null);
        Assert.Null(session.Arrival);

        session.OnNavigating(@"C:\folder69", selectedPath: null);
        Assert.NotNull(session.Arrival);
    }


    // --- The watcher tick: idempotent, and never losing a change ----------

    [Fact]
    public void AQuietTick_IsIdle_HoweverManyTimesItFires() {
        var session = new FolderSession();
        var rows = new[] { Row("a.txt") };

        Assert.Equal(WatchOutcome.Idle, session.DecideWatchTick(busy: false, rows).Outcome);
        Assert.Equal(WatchOutcome.Idle, session.DecideWatchTick(busy: false, rows).Outcome);
    }

    [Fact]
    public void MetadataChange_RefreshesTheRow_NotTheFolder() {
        // The project rule made testable: a sidecar written beside a photo
        // must not cost the folder its rows.
        var session = new FolderSession();
        var photo = Row("IMG_1.CR3", "IMG_1.CR3.pp3");
        session.NoteChange(new DirectoryChange(@"C:\folder\IMG_1.CR3.pp3", Structural: false));

        var decision = session.DecideWatchTick(busy: false, rows: new[] { photo, Row("other.txt") });

        Assert.Equal(WatchOutcome.RefreshRows, decision.Outcome);
        Assert.Equal(new[] { photo }, decision.Rows);
    }

    [Fact]
    public void CompositionChange_Relists_AndTellsTheTrees() {
        var session = new FolderSession();
        session.NoteChange(new DirectoryChange(@"C:\folder\new.txt", Structural: true));

        var decision = session.DecideWatchTick(busy: false, rows: Array.Empty<FileSystemEntry>());

        Assert.Equal(WatchOutcome.Relist, decision.Outcome);
        Assert.True(decision.RefreshTrees);
    }

    [Fact]
    public void AReplacedFile_IsReportedStale_EvenThoughTheFolderIsRelisted() {
        // A picture deleted and another one copied in under its name. The
        // listing is rebuilt, but the path is the same one - so whatever is
        // cached against that path has to be named, or the tile goes on
        // showing the picture that was deleted.
        var session = new FolderSession();
        session.NoteChange(new DirectoryChange(@"C:\folder\photo.jpg", Structural: true));

        var decision = session.DecideWatchTick(busy: false, rows: Array.Empty<FileSystemEntry>());

        Assert.Equal(WatchOutcome.Relist, decision.Outcome);
        Assert.Equal(new[] { @"C:\folder\photo.jpg" }, decision.Stale);
    }

    [Fact]
    public void AnEditedFile_IsReportedStale_AlongsideItsRowRefresh() {
        var session = new FolderSession();
        var photo = Row("IMG_1.CR3");
        session.NoteChange(new DirectoryChange(@"C:\folder\IMG_1.CR3", Structural: false));

        var decision = session.DecideWatchTick(busy: false, rows: new[] { photo });

        Assert.Equal(WatchOutcome.RefreshRows, decision.Outcome);
        Assert.Equal(new[] { @"C:\folder\IMG_1.CR3" }, decision.Stale);
    }

    [Fact]
    public void AQuietTick_NamesNothingStale() {
        var session = new FolderSession();

        Assert.Null(session.DecideWatchTick(busy: false, rows: Array.Empty<FileSystemEntry>()).Stale);
    }

    [Fact]
    public void AChangedFileNobodyShows_Relists_WithoutBotheringTheTrees() {
        // The listing does not know the file; only a fresh listing can say
        // what it is. Nothing says the panels are affected.
        var session = new FolderSession();
        session.NoteChange(new DirectoryChange(@"C:\folder\unknown.tmp", Structural: false));

        var decision = session.DecideWatchTick(busy: false, rows: new[] { Row("a.txt") });

        Assert.Equal(WatchOutcome.Relist, decision.Outcome);
        Assert.False(decision.RefreshTrees);
    }

    [Fact]
    public void ABusyTick_HoldsTheChanges_AndALaterTickAnswersThem() {
        // A rename editor or our own operation postpones the answer; it
        // must not eat it.
        var session = new FolderSession();
        var rows = new[] { Row("a.txt") };
        session.NoteChange(new DirectoryChange(@"C:\folder\a.txt", Structural: false));

        Assert.Equal(WatchOutcome.Hold, session.DecideWatchTick(busy: true, rows).Outcome);
        Assert.Equal(WatchOutcome.RefreshRows, session.DecideWatchTick(busy: false, rows).Outcome);
    }

    [Fact]
    public void AnsweredChanges_AreNotAnsweredTwice() {
        var session = new FolderSession();
        var rows = new[] { Row("a.txt") };
        session.NoteChange(new DirectoryChange(@"C:\folder\a.txt", Structural: false));

        session.DecideWatchTick(busy: false, rows);

        Assert.Equal(WatchOutcome.Idle, session.DecideWatchTick(busy: false, rows).Outcome);
    }

    [Fact]
    public void AnUnwatchedFolder_ForgetsItsPendingChanges() {
        var session = new FolderSession();
        session.NoteChange(new DirectoryChange(@"C:\folder\a.txt", Structural: false));

        session.ForgetPendingChanges();

        Assert.Equal(
            WatchOutcome.Idle,
            session.DecideWatchTick(busy: false, rows: Array.Empty<FileSystemEntry>()).Outcome);
    }
}
