using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class ClipboardControllerTests {
    private const string FileA = @"C:\src\a.txt";
    private const string FileB = @"C:\src\b.txt";
    private const string FileC = @"C:\src\c.txt";


    [Fact]
    public void FreshController_HasNothing() {
        var clip = new ClipboardController();

        Assert.False(clip.HasContent);
        Assert.False(clip.IsCut);
        Assert.Empty(clip.Paths);
    }

    [Fact]
    public void Copy_StoresPaths_ClearsCutFlag() {
        var clip = new ClipboardController();

        clip.Copy(new[] { FileA, FileB });

        Assert.True(clip.HasContent);
        Assert.False(clip.IsCut);
        Assert.Equal(new[] { FileA, FileB }, clip.Paths);
    }

    [Fact]
    public void Cut_StoresPaths_SetsCutFlag() {
        var clip = new ClipboardController();

        clip.Cut(new[] { FileA });

        Assert.True(clip.HasContent);
        Assert.True(clip.IsCut);
        Assert.Equal(new[] { FileA }, clip.Paths);
    }

    [Fact]
    public void Copy_AfterCut_FlipsBackToCopyMode() {
        var clip = new ClipboardController();
        clip.Cut(new[] { FileA });
        Assert.True(clip.IsCut);

        clip.Copy(new[] { FileB });

        Assert.False(clip.IsCut);
        Assert.Equal(new[] { FileB }, clip.Paths);
    }

    [Fact]
    public void Cut_AfterCopy_FlipsToCutMode() {
        var clip = new ClipboardController();
        clip.Copy(new[] { FileA });

        clip.Cut(new[] { FileB });

        Assert.True(clip.IsCut);
        Assert.Equal(new[] { FileB }, clip.Paths);
    }

    [Fact]
    public void Clear_EmptiesPaths_AndResetsCutFlag() {
        var clip = new ClipboardController();
        clip.Cut(new[] { FileA, FileB });

        clip.Clear();

        Assert.False(clip.HasContent);
        Assert.False(clip.IsCut);
        Assert.Empty(clip.Paths);
    }

    [Fact]
    public void Clear_OnFreshController_DoesNotFireChanged() {
        var clip = new ClipboardController();
        int fired = 0;
        clip.Changed += (_, _) => fired++;

        clip.Clear();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Changed_FiresOnCopy_Cut_Clear() {
        var clip = new ClipboardController();
        int fired = 0;
        clip.Changed += (_, _) => fired++;

        clip.Copy(new[] { FileA });      // +1
        clip.Cut(new[] { FileB });       // +1
        clip.Clear();                    // +1

        Assert.Equal(3, fired);
    }

    [Fact]
    public void Paths_IsSnapshot_NotLiveReference() {
        // Mutating the caller's list after Copy should not change the
        // controller's internal store. (Defends against accidental aliasing.)
        var clip = new ClipboardController();
        var input = new List<string> { FileA, FileB };

        clip.Copy(input);
        input.Add(FileC);

        Assert.Equal(2, clip.Paths.Count);
        Assert.DoesNotContain(FileC, clip.Paths);
    }

    [Fact]
    public void Copy_EmptyInput_ResetsToEmptyState() {
        var clip = new ClipboardController();
        clip.Cut(new[] { FileA });

        clip.Copy(Array.Empty<string>());

        Assert.False(clip.HasContent);
        Assert.False(clip.IsCut);
    }


    // --- Mirroring onto the OS clipboard --------------------------------

    [Fact]
    public void Copy_MirrorsPathsOntoTheSystemClipboard() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);

        clip.Copy(new[] { FileA, FileB });

        Assert.Equal(new[] { FileA, FileB }, system.Content!.Value.Paths);
        Assert.False(system.Content!.Value.IsCut);
        Assert.Null(clip.LastSystemIssue);
    }

    [Fact]
    public void Cut_MirrorsTheMoveFlagToo() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);

        clip.Cut(new[] { FileA });

        Assert.True(system.Content!.Value.IsCut);
    }

    [Fact]
    public void Copy_WhenClipboardIsBusy_KeepsWorkingLocally_AndSaysSo() {
        var system = new FakeSystemClipboard { Fails = true };
        var clip = new ClipboardController(system);

        clip.Copy(new[] { FileA });

        Assert.Equal(new[] { FileA }, clip.Paths);
        Assert.Equal(ClipboardController.SystemIssue.WriteFailed, clip.LastSystemIssue);
    }

    [Fact]
    public void Clear_EmptiesTheSystemClipboardToo() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);
        clip.Cut(new[] { FileA });

        clip.Clear();

        Assert.False(system.Content!.Value.HasContent);
    }

    [Fact]
    public void Sync_AdoptsWhatAnotherApplicationCopied() {
        var system = new FakeSystemClipboard {
            Content = new ClipboardFiles(new[] { FileC }, IsCut: true),
        };
        var clip = new ClipboardController(system);

        bool changed = clip.SyncFromSystem();

        Assert.True(changed);
        Assert.Equal(new[] { FileC }, clip.Paths);
        Assert.True(clip.IsCut);
    }

    [Fact]
    public void Sync_WithOurOwnContentStillThere_ReportsNoChange() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);
        clip.Copy(new[] { FileA, FileB });

        Assert.False(clip.SyncFromSystem());
    }

    [Fact]
    public void Sync_IsCaseInsensitiveAboutPaths() {
        // Windows paths are case-insensitive; the same list in another case
        // is the same list, and adopting it would fire Changed for nothing.
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);
        clip.Copy(new[] { FileA });
        system.Content = new ClipboardFiles(new[] { FileA.ToUpperInvariant() }, IsCut: false);

        Assert.False(clip.SyncFromSystem());
    }

    [Fact]
    public void Sync_WhenClipboardHoldsSomethingElse_DropsOurPaths() {
        // The user copied text somewhere. Paste has to grey out rather than
        // paste what they copied ten minutes ago.
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);
        clip.Copy(new[] { FileA });
        system.Content = ClipboardFiles.Empty;

        Assert.True(clip.SyncFromSystem());
        Assert.False(clip.HasContent);
    }

    [Fact]
    public void Sync_WhenClipboardCannotBeRead_KeepsWhatWeHave() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);
        clip.Copy(new[] { FileA });
        system.Fails = true;

        Assert.False(clip.SyncFromSystem());
        Assert.Equal(new[] { FileA }, clip.Paths);
    }

    [Fact]
    public void Sync_NotesFilesThatAreNotOnDisk() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);
        clip.Copy(new[] { FileA });

        // Now the user copies an attachment out of a mail client: files, but
        // not files that exist anywhere Wander could reach them.
        system.Content = new ClipboardFiles(Array.Empty<string>(), IsCut: false, HasUnsupportedFiles: true);
        clip.SyncFromSystem();

        Assert.Equal(ClipboardController.SystemIssue.VirtualFiles, clip.LastSystemIssue);
        Assert.False(clip.HasContent);
    }

    [Fact]
    public void Copy_WithShellObject_SendsItInsteadOfTheFileList() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);

        clip.Copy(new[] { FileA }, systemObject: "shell-object");

        Assert.Equal(new[] { "SetShellObject" }, system.CallLog);
        Assert.Equal("shell-object", system.SharedObject);
        // The paths stay: Wander's own paste extracts from them.
        Assert.Equal(new[] { FileA }, clip.Paths);
        Assert.Null(clip.LastSystemIssue);
    }

    [Fact]
    public void Sync_KeepsOurOwnShellObjectsPaths() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);

        // A copy out of an archive: the clipboard reads back as "files that
        // are not on disk", because that is what a shell object looks like
        // from outside. Our own paths must survive it.
        clip.Copy(new[] { FileA }, systemObject: "shell-object");

        Assert.False(clip.SyncFromSystem());
        Assert.Equal(new[] { FileA }, clip.Paths);
        Assert.Null(clip.LastSystemIssue);
    }

    [Fact]
    public void Sync_AdoptsWhatCameAfterOurShellObject() {
        var system = new FakeSystemClipboard();
        var clip = new ClipboardController(system);
        clip.Copy(new[] { FileA }, systemObject: "shell-object");

        // Somebody copied real files afterwards; ours is gone.
        system.Content = new ClipboardFiles(new[] { FileC }, IsCut: false);

        Assert.True(clip.SyncFromSystem());
        Assert.Equal(new[] { FileC }, clip.Paths);
    }

    [Fact]
    public void Sync_FiresChanged_SoPasteCanRefresh() {
        var system = new FakeSystemClipboard {
            Content = new ClipboardFiles(new[] { FileC }, IsCut: false),
        };
        var clip = new ClipboardController(system);
        int fired = 0;
        clip.Changed += (_, _) => fired++;

        clip.SyncFromSystem();

        Assert.Equal(1, fired);
    }
}
