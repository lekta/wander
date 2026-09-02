using Wander.Core.Persistence;

namespace Wander.Core.Tests;

public class TempFilesTests {
    private const string Entry = @"D:\packs\photos.zip\raw\IMG.CR2";


    [Fact]
    public void FolderFor_IsStableForTheSamePath() {
        Assert.Equal(TempFiles.FolderFor(Entry), TempFiles.FolderFor(Entry));
    }

    [Fact]
    public void FolderFor_IgnoresCase() {
        // Opening the same entry twice must reuse the same folder rather
        // than pile up "IMG (1).CR2" beside the first copy.
        Assert.Equal(TempFiles.FolderFor(Entry), TempFiles.FolderFor(Entry.ToUpperInvariant()));
    }

    [Fact]
    public void FolderFor_SeparatesEntriesWithTheSameName() {
        Assert.NotEqual(
            TempFiles.FolderFor(@"D:\a.zip\notes.txt"),
            TempFiles.FolderFor(@"D:\b.zip\notes.txt"));
    }

    [Fact]
    public void FolderFor_LivesUnderTheTempRoot() {
        Assert.StartsWith(AppPaths.Tmp, TempFiles.FolderFor(Entry), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sweep_OnAMissingRoot_DoesNothing() {
        AppPaths.Override(Path.Combine(Path.GetTempPath(), "wander-tests", Guid.NewGuid().ToString("N")));
        try {
            Assert.Equal(0, TempFiles.Sweep(DateTime.UtcNow));
        } finally {
            AppPaths.Resolve(Array.Empty<string>());
        }
    }
}
