using Wander.Core.FileSystem;

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
}
