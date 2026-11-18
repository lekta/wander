using Wander.Core.FileSystem;
using Wander.Core.Pins;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

/// <summary>The pins (PLAN, "Pins"): the book in memory, its snapshot for the store.</summary>
public class PinBookTests {
    private const string Shoot = @"D:\shoot";
    private static readonly DateTime _monday = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _tuesday = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly PinVolume _stickOnE = new(@"E:\", 0x1A2B3C4D, IsRemovable: true);
    private static readonly PinVolume _stickOnF = new(@"F:\", 0x1A2B3C4D, IsRemovable: true);
    private static readonly PinVolume _otherStickOnE = new(@"E:\", 0x99990001, IsRemovable: true);
    private static readonly PinVolume _fixedD = new(@"D:\", 0x0BADF00D, IsRemovable: false);


    // --- Pin, Unpin, ask ---

    [Fact]
    public void Pin_MarksThePath_AndReportsTheChangeOnce() {
        var book = new PinBook();
        int changes = 0;
        book.Changed += (_, _) => changes++;

        Assert.True(book.Pin(@"D:\shoot\a.jpg", _monday));
        Assert.False(book.Pin(@"D:\shoot\a.jpg", _tuesday));

        Assert.True(book.IsPinned(@"D:\shoot\a.jpg"));
        Assert.False(book.IsPinned(@"D:\shoot\b.jpg"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Paths_CompareWithoutCase() {
        var book = new PinBook();
        book.Pin(@"D:\Shoot\A.jpg", _monday);

        Assert.True(book.IsPinned(@"d:\SHOOT\a.JPG"));
        Assert.Equal(new[] { "A.jpg" }, book.NamesIn(@"d:\shoot\"));
    }

    [Fact]
    public void AFoldersPin_IsARecordInItsParent() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\day1\", _monday);

        Assert.True(book.IsPinned(@"D:\shoot\day1"));
        Assert.Equal(new[] { "day1" }, book.NamesIn(Shoot));
        Assert.Equal(new[] { Shoot }, book.Folders());
    }

    [Fact]
    public void ARoot_CannotBePinned() {
        var book = new PinBook();

        Assert.False(book.Pin(@"D:\", _monday));
        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void Unpin_TakesThePinOff_AndAnEmptyFolderGoes() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);
        int changes = 0;
        book.Changed += (_, _) => changes++;

        Assert.True(book.Unpin(@"D:\SHOOT\A.JPG"));
        Assert.False(book.Unpin(@"D:\shoot\a.jpg"));

        Assert.False(book.IsPinned(@"D:\shoot\a.jpg"));
        Assert.Empty(book.Folders());
        Assert.Equal(0, book.FolderCount);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void UnpinAll_EmptiesTheBook() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);
        book.Pin(@"E:\docs\b.txt", _monday);
        int changes = 0;
        book.Changed += (_, _) => changes++;

        Assert.Equal(2, book.UnpinAll());
        Assert.Equal(0, book.UnpinAll());

        Assert.Equal(0, book.Count);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Folders_AreListedInPathOrder() {
        var book = new PinBook();
        book.Pin(@"E:\x\1.txt", _monday);
        book.Pin(@"D:\b\1.txt", _monday);
        book.Pin(@"D:\a\1.txt", _monday);

        Assert.Equal(new[] { @"D:\a", @"D:\b", @"E:\x" }, book.Folders());
    }


    // --- Removable volumes ---

    [Fact]
    public void ARemovableVolume_IsKeyedBySerial_OthersByPath() {
        var volumes = new[] { _stickOnE, _fixedD };

        Assert.Equal(@"volume:1A2B3C4D\Photos\2024", PinKeys.ToKey(@"E:\Photos\2024\", volumes));
        Assert.Equal("volume:1A2B3C4D", PinKeys.ToKey(@"E:\", volumes));
        Assert.Equal(@"D:\shoot", PinKeys.ToKey(@"D:\shoot", volumes));
        Assert.Equal(@"G:\x", PinKeys.ToKey(@"G:\x", volumes));
        Assert.Equal(@"E:\Photos\2024", PinKeys.ToPath(@"volume:1a2b3c4d\Photos\2024", volumes));
        Assert.Equal(@"E:\", PinKeys.ToPath("volume:1A2B3C4D", volumes));
        Assert.Null(PinKeys.ToPath(@"volume:99990001\x", volumes));
        Assert.Null(PinKeys.ToPath(@"volume:zz\x", volumes));
    }

    [Fact]
    public void TheSnapshot_TakesReadyVolumesWithASerial() {
        var volumes = PinKeys.Snapshot(new[] {
            new VolumeInfo(@"E:\", "", "FAT32", VolumeKind.Removable, 10, 5, IsReady: true, Serial: 7),
            new VolumeInfo(@"D:\", "", "NTFS", VolumeKind.Fixed, 10, 5, IsReady: true, Serial: 8),
            new VolumeInfo(@"F:\", "", "", VolumeKind.Removable, 0, 0, IsReady: false),
            new VolumeInfo(@"G:\", "", "NTFS", VolumeKind.Removable, 10, 5, IsReady: true),
        });

        Assert.Equal(new[] { new PinVolume(@"E:\", 7, true), new PinVolume(@"D:\", 8, false) }, volumes);
    }

    [Fact]
    public void TheSameStick_UnderAnotherLetter_FindsItsPins() {
        var book = new PinBook();
        book.SetVolumes(new[] { _stickOnE });
        book.Pin(@"E:\Photos\a.jpg", _monday);
        Assert.Equal(@"volume:1A2B3C4D\Photos", book.Snapshot()[0].Key);

        book.SetVolumes(new[] { _stickOnF });

        Assert.True(book.IsPinned(@"F:\Photos\a.jpg"));
        Assert.False(book.IsPinned(@"E:\Photos\a.jpg"));
        Assert.Equal(new[] { @"F:\Photos" }, book.Folders());
    }

    [Fact]
    public void AnotherStick_UnderTheSameLetter_DoesNot() {
        var book = new PinBook();
        book.SetVolumes(new[] { _stickOnE });
        book.Pin(@"E:\Photos\a.jpg", _monday);

        book.SetVolumes(new[] { _otherStickOnE });

        Assert.False(book.IsPinned(@"E:\Photos\a.jpg"));
        Assert.Empty(book.NamesIn(@"E:\Photos"));
    }

    [Fact]
    public void AStickTakenOut_HidesItsPins_AndBringsThemBack() {
        var book = new PinBook();
        book.SetVolumes(new[] { _stickOnE, _fixedD });
        book.Pin(@"E:\Photos\a.jpg", _monday);
        book.Pin(@"D:\shoot\b.jpg", _monday);

        book.SetVolumes(new[] { _fixedD });

        Assert.Equal(new[] { Shoot }, book.Folders());
        Assert.False(book.IsPinned(@"E:\Photos\a.jpg"));
        Assert.Equal(2, book.Count);

        book.SetVolumes(new[] { _stickOnE, _fixedD });

        Assert.True(book.IsPinned(@"E:\Photos\a.jpg"));
        Assert.Equal(new[] { Shoot, @"E:\Photos" }, book.Folders());
    }


    // --- Following ---

    [Fact]
    public void AFileMoved_TakesItsPin_AndTheUndoBringsItBack() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);

        Assert.Equal(1, book.Follow(@"D:\shoot\a.jpg", @"D:\keep\a.jpg"));
        Assert.False(book.IsPinned(@"D:\shoot\a.jpg"));
        Assert.True(book.IsPinned(@"D:\keep\a.jpg"));
        Assert.Equal(_monday, book.Snapshot()[0].Pins[0].PinnedUtc);

        Assert.Equal(1, book.Follow(@"D:\keep\a.jpg", @"D:\shoot\a.jpg"));
        Assert.True(book.IsPinned(@"D:\shoot\a.jpg"));
        Assert.Equal(new[] { Shoot }, book.Folders());
    }

    [Fact]
    public void AFileRenamed_TakesItsPin_AndTheUndoBringsItBack() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);

        book.Follow(@"D:\shoot\a.jpg", @"D:\shoot\best.jpg");
        Assert.Equal(new[] { "best.jpg" }, book.NamesIn(Shoot));

        book.Follow(@"D:\shoot\best.jpg", @"D:\shoot\a.jpg");
        Assert.Equal(new[] { "a.jpg" }, book.NamesIn(Shoot));
    }

    [Fact]
    public void AFolderRenamed_TakesItsOwnPin_AndEveryPinUnderIt_AndTheUndoBringsThemBack() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\day1", _monday);
        book.Pin(@"D:\shoot\day1\a.jpg", _monday);
        book.Pin(@"D:\shoot\day1\raw\b.cr3", _monday);
        book.Pin(@"D:\shoot\day10\c.jpg", _monday);

        Assert.Equal(3, book.Follow(@"D:\shoot\day1", @"D:\shoot\monday"));

        Assert.True(book.IsPinned(@"D:\shoot\monday"));
        Assert.True(book.IsPinned(@"D:\shoot\monday\a.jpg"));
        Assert.True(book.IsPinned(@"D:\shoot\monday\raw\b.cr3"));
        Assert.True(book.IsPinned(@"D:\shoot\day10\c.jpg"));
        Assert.False(book.IsPinned(@"D:\shoot\day1\a.jpg"));

        Assert.Equal(3, book.Follow(@"D:\shoot\monday", @"D:\shoot\day1"));
        Assert.True(book.IsPinned(@"D:\shoot\day1\raw\b.cr3"));
        Assert.Equal(new[] { Shoot, @"D:\shoot\day1", @"D:\shoot\day10", @"D:\shoot\day1\raw" }, book.Folders());
    }

    [Fact]
    public void AFolderMoved_OntoAStick_IsKeyedBySerialThere() {
        var book = new PinBook();
        book.SetVolumes(new[] { _stickOnE, _fixedD });
        book.Pin(@"D:\shoot\day1\a.jpg", _monday);

        book.Follow(@"D:\shoot\day1", @"E:\day1");

        Assert.Equal(@"volume:1A2B3C4D\day1", Assert.Single(book.Snapshot()).Key);
        Assert.True(book.IsPinned(@"E:\day1\a.jpg"));
    }

    [Fact]
    public void Follow_WithNothingUnderIt_IsHarmless_AndARepeatFindsNothing() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);
        int changes = 0;
        book.Changed += (_, _) => changes++;

        Assert.Equal(0, book.Follow(@"D:\shoot\b.jpg", @"D:\shoot\c.jpg"));
        Assert.Equal(1, book.Follow(@"D:\shoot\a.jpg", @"D:\shoot\z.jpg"));
        Assert.Equal(0, book.Follow(@"D:\shoot\a.jpg", @"D:\shoot\z.jpg"));

        Assert.Equal(1, changes);
        Assert.True(book.IsPinned(@"D:\shoot\z.jpg"));
    }

    [Fact]
    public void ACaseOnlyRename_LeavesTheRecord() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);

        Assert.Equal(0, book.Follow(@"D:\shoot\a.jpg", @"D:\shoot\A.JPG"));
        Assert.True(book.IsPinned(@"D:\shoot\A.JPG"));
    }

    [Fact]
    public void Moves_AtOnce_CanSwapTwoNames() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);
        book.Pin(@"D:\shoot\b.jpg", _tuesday);

        Assert.Equal(2, book.Follow(new[] { (@"D:\shoot\a.jpg", @"D:\shoot\b.jpg"), (@"D:\shoot\b.jpg", @"D:\shoot\a.jpg") }));

        var pins = book.Snapshot()[0].Pins;
        Assert.Equal(_tuesday, pins.Single(p => p.Name == "a.jpg").PinnedUtc);
        Assert.Equal(_monday, pins.Single(p => p.Name == "b.jpg").PinnedUtc);
    }


    // --- Deleting ---

    [Fact]
    public void ToTheBin_ThePinHides_SurvivesTheSweep_AndComesBackWithTheFile() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);
        book.Pin(@"D:\shoot\day1\b.jpg", _monday);

        Assert.Equal(1, book.MarkRecycled(@"D:\shoot\a.jpg"));
        Assert.False(book.IsPinned(@"D:\shoot\a.jpg"));
        Assert.Empty(book.Sweep(Shoot, new[] { "day1" }, removeGone: true, book.Version));
        Assert.Equal(2, book.Count);

        book.Sweep(Shoot, new[] { "a.jpg", "day1" }, removeGone: true, book.Version);

        Assert.True(book.IsPinned(@"D:\shoot\a.jpg"));
    }

    [Fact]
    public void AFolderToTheBin_HidesEveryPinUnderIt() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\day1", _monday);
        book.Pin(@"D:\shoot\day1\a.jpg", _monday);
        book.Pin(@"D:\shoot\day1\raw\b.cr3", _monday);

        Assert.Equal(3, book.MarkRecycled(@"D:\shoot\day1"));

        Assert.Empty(book.Folders());
        Assert.Empty(book.Sweep(@"D:\shoot\day1", Array.Empty<string>(), removeGone: true, book.Version));
        Assert.Equal(3, book.Count);
    }

    [Fact]
    public void Forget_DropsThePin_AndEveryPinUnderIt() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\day1", _monday);
        book.Pin(@"D:\shoot\day1\a.jpg", _monday);
        book.Pin(@"D:\shoot\a.jpg", _monday);
        book.MarkRecycled(@"D:\shoot\day1");

        Assert.Equal(2, book.Forget(@"D:\shoot\day1"));

        Assert.Equal(1, book.Count);
        Assert.True(book.IsPinned(@"D:\shoot\a.jpg"));
    }


    // --- Sweep ---

    [Fact]
    public void Sweep_TakesOffWhatIsGone_AndSaysWhat() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);
        book.Pin(@"D:\shoot\b.jpg", _monday);
        book.Pin(@"D:\shoot\day1", _monday);
        int changes = 0;
        book.Changed += (_, _) => changes++;

        var removed = book.Sweep(@"d:\SHOOT\", new[] { "A.JPG", "day1", "other.txt" }, removeGone: true, book.Version);

        Assert.Equal(new[] { "b.jpg" }, removed);
        Assert.Equal(new[] { "a.jpg", "day1" }, book.NamesIn(Shoot));
        Assert.Equal(1, changes);
        Assert.Empty(book.Sweep(Shoot, new[] { "a.jpg", "day1" }, removeGone: true, book.Version));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Sweep_OfAListingOlderThanAChange_TouchesNothing() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);
        int listedAt = book.Version;
        // While the folder was being read: a new file pinned, the old one binned.
        book.Pin(@"D:\shoot\new.jpg", _monday);
        book.MarkRecycled(@"D:\shoot\a.jpg");

        Assert.Empty(book.Sweep(Shoot, new[] { "a.jpg" }, removeGone: true, listedAt));

        Assert.True(book.IsPinned(@"D:\shoot\new.jpg"));
        Assert.False(book.IsPinned(@"D:\shoot\a.jpg"));
    }

    [Fact]
    public void Loading_SkipsNullRecords() {
        var book = new PinBook(new[] { null!, new PinFolder(Shoot, new PinRecord[] { null!, new("a.jpg", _monday) }) });

        Assert.Equal(1, book.Count);
    }

    [Fact]
    public void Sweep_TurnedOff_KeepsWhatIsGone() {
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);

        Assert.Empty(book.Sweep(Shoot, Array.Empty<string>(), removeGone: false, book.Version));
        Assert.True(book.IsPinned(@"D:\shoot\a.jpg"));
    }

    [Fact]
    public void Sweep_OfAnotherFolder_OrAStickNotPluggedIn_TouchesNothing() {
        var book = new PinBook();
        book.SetVolumes(new[] { _stickOnE });
        book.Pin(@"E:\Photos\a.jpg", _monday);
        book.SetVolumes(new[] { _otherStickOnE });

        Assert.Empty(book.Sweep(@"E:\Photos", Array.Empty<string>(), removeGone: true, book.Version));
        Assert.Empty(book.Sweep(Shoot, Array.Empty<string>(), removeGone: true, book.Version));
        Assert.Equal(1, book.Count);
    }


    // --- Snapshot and the store ---

    [Fact]
    public void ARoundTrip_ThroughTheStore_KeepsEveryPin() {
        var store = new FakePinStore();
        var book = new PinBook(store.Load());
        book.Pin(@"D:\shoot\b.jpg", _tuesday);
        book.Pin(@"D:\shoot\a.jpg", _monday);
        book.Pin(@"D:\shoot\day1", _monday);
        store.Save(book.Snapshot());

        var again = new PinBook(store.Load());

        Assert.Equal(3, again.Count);
        Assert.Equal(new[] { "a.jpg", "b.jpg", "day1" }, again.NamesIn(Shoot));
        var pins = Assert.Single(again.Snapshot()).Pins;
        Assert.Equal(_tuesday, pins.Single(p => p.Name == "b.jpg").PinnedUtc);
    }

    [Fact]
    public void AReadOnlyStore_KeepsWhatItHad() {
        var store = new FakePinStore();
        var book = new PinBook();
        book.Pin(@"D:\shoot\a.jpg", _monday);
        store.Save(book.Snapshot());
        store.IsReadOnly = true;

        book.Pin(@"D:\shoot\b.jpg", _monday);
        store.Save(book.Snapshot());

        Assert.Equal(1, store.Saves);
        Assert.Equal(1, new PinBook(store.Load()).Count);
    }

    [Fact]
    public void Loading_SkipsBadRecords_AndKeepsTheLaterOfTwo() {
        var book = new PinBook(new[] {
            new PinFolder(Shoot, new[] {
                new PinRecord("a.jpg", _monday),
                new PinRecord("A.JPG", _tuesday),
                new PinRecord("", _monday),
                new PinRecord(@"x\y", _monday),
            }),
            new PinFolder(" ", new[] { new PinRecord("z.jpg", _monday) }),
        });

        Assert.Equal(1, book.Count);
        Assert.Equal(_tuesday, book.Snapshot()[0].Pins[0].PinnedUtc);
    }
}
