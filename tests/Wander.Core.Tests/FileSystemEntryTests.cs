using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

/// <summary>
/// Audit 2026-11-18, P3: a row re-read in place takes what the disk says
/// now about the file - size, date, attributes - and keeps what the listing
/// and its passes put on it.
/// </summary>
public class FileSystemEntryTests {

    private static FileSystemEntry Entry(long size, DateTime modified, bool hidden = false, bool readOnly = false, bool system = false) {
        return new FileSystemEntry(
            Name: "IMG_1.jpg",
            FullPath: @"C:\photos\IMG_1.jpg",
            Kind: EntryKind.File,
            Size: size,
            ModifiedUtc: modified,
            IsHidden: hidden,
            IsReadOnly: readOnly,
            IsSystem: system,
            LinksToDirectory: false);
    }

    [Fact]
    public void ARestatedRow_TakesTheSizeDateAndAttributesOfTheFile() {
        var row = Entry(100, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var fresh = Entry(2048, new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc), hidden: true, readOnly: true, system: true);

        var restated = row.Restated(fresh);

        Assert.Equal(2048, restated.Size);
        Assert.Equal(fresh.ModifiedUtc, restated.ModifiedUtc);
        Assert.True(restated.IsHidden);
        Assert.True(restated.IsReadOnly);
        Assert.True(restated.IsSystem);
    }

    [Fact]
    public void ARestatedRow_KeepsWhatTheListingPutOnIt() {
        var rating = new SidecarRating(3, 0);
        var row = Entry(100, DateTime.UnixEpoch) with {
            Companions = new[] { @"C:\photos\IMG_1.jpg.xmp" },
            Rating = rating,
            MatchSnippet = "matched",
            Sharpness = 42,
            ContentSize = 7,
            OriginalLocation = @"C:\was",
        };

        var restated = row.Restated(Entry(200, DateTime.UnixEpoch.AddDays(1)) with { Sharpness = 1, MatchSnippet = null });

        Assert.Equal(row.Companions, restated.Companions);
        Assert.Equal(rating, restated.Rating);
        Assert.Equal("matched", restated.MatchSnippet);
        Assert.Equal(42, restated.Sharpness);
        Assert.Equal(7, restated.ContentSize);
        Assert.Equal(@"C:\was", restated.OriginalLocation);
        Assert.Equal(row.FullPath, restated.FullPath);
    }
}
