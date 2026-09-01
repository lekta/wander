using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class EntryComparersTests {

    private static FileSystemEntry Entry(
        string name, long? size = null, DateTime? modified = null, EntryKind kind = EntryKind.File, int? rank = null) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\" + name,
            Kind: kind,
            Size: size,
            ModifiedUtc: modified ?? DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false,
            Rating: rank is null ? null : new SidecarRating(rank, null));
    }


    [Fact]
    public void Name_Ascending_OrdersLexically() {
        var items = new List<FileSystemEntry> { Entry("c.txt"), Entry("a.txt"), Entry("b.txt") };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Name, Ascending: true, GroupFoldersFirst: false)));
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, items.Select(e => e.Name));
    }

    [Fact]
    public void Name_Descending_Reverses() {
        var items = new List<FileSystemEntry> { Entry("a.txt"), Entry("b.txt"), Entry("c.txt") };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Name, Ascending: false, GroupFoldersFirst: false)));
        Assert.Equal(new[] { "c.txt", "b.txt", "a.txt" }, items.Select(e => e.Name));
    }


    [Fact]
    public void Size_BreaksTiesByName() {
        var items = new List<FileSystemEntry> {
            Entry("c.txt", size: 100),
            Entry("a.txt", size: 100),
            Entry("b.txt", size: 100),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Size, Ascending: true, GroupFoldersFirst: false)));
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, items.Select(e => e.Name));
    }

    [Fact]
    public void Size_Ascending_OrdersBySize() {
        var items = new List<FileSystemEntry> {
            Entry("big.bin", size: 1000),
            Entry("tiny.bin", size: 1),
            Entry("medium.bin", size: 500),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Size, Ascending: true, GroupFoldersFirst: false)));
        Assert.Equal(new[] { "tiny.bin", "medium.bin", "big.bin" }, items.Select(e => e.Name));
    }

    [Fact]
    public void Size_NullTreatedAsZero() {
        var items = new List<FileSystemEntry> {
            Entry("file", size: 10),
            Entry("folder", size: null, kind: EntryKind.Directory),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Size, Ascending: true, GroupFoldersFirst: false)));
        Assert.Equal(new[] { "folder", "file" }, items.Select(e => e.Name));
    }


    [Fact]
    public void Modified_Ascending_OldestFirst() {
        var t0 = new DateTime(2020, 1, 1);
        var items = new List<FileSystemEntry> {
            Entry("newest", modified: t0.AddDays(2)),
            Entry("oldest", modified: t0),
            Entry("middle", modified: t0.AddDays(1)),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.ModifiedDate, Ascending: true, GroupFoldersFirst: false)));
        Assert.Equal(new[] { "oldest", "middle", "newest" }, items.Select(e => e.Name));
    }


    [Fact]
    public void Type_OrdersByExtension() {
        var items = new List<FileSystemEntry> {
            Entry("z.txt"),
            Entry("a.png"),
            Entry("k.csv"),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Type, Ascending: true, GroupFoldersFirst: false)));
        // .csv → .png → .txt; equal-extension would fall back to name
        Assert.Equal(new[] { "k.csv", "a.png", "z.txt" }, items.Select(e => e.Name));
    }

    [Fact]
    public void Type_BreaksTiesByName() {
        var items = new List<FileSystemEntry> {
            Entry("zebra.txt"),
            Entry("apple.txt"),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Type, Ascending: true, GroupFoldersFirst: false)));
        Assert.Equal(new[] { "apple.txt", "zebra.txt" }, items.Select(e => e.Name));
    }


    [Fact]
    public void Default_IsNameAscending() {
        var items = new List<FileSystemEntry> { Entry("c"), Entry("a"), Entry("b") };
        items.Sort(EntryComparers.Build(SortOptions.Default));
        Assert.Equal(new[] { "a", "b", "c" }, items.Select(e => e.Name));
    }


    // --- Rating ---------------------------------------------------------

    [Fact]
    public void Rating_OrdersByStars() {
        var items = new List<FileSystemEntry> {
            Entry("b.jpg", rank: 1),
            Entry("a.jpg", rank: 5),
            Entry("c.jpg", rank: 3),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Rating, Ascending: false, GroupFoldersFirst: false)));

        Assert.Equal(new[] { "a.jpg", "c.jpg", "b.jpg" }, items.Select(e => e.Name));
    }

    [Fact]
    public void Rating_UnratedSortsAsZero() {
        // Not below zero: "no sidecar" and "zero stars" are the same
        // statement about a photo, and the pass that reads sidecars turns
        // one into the other halfway through a folder.
        var items = new List<FileSystemEntry> {
            Entry("rated.jpg", rank: 2),
            Entry("explicit.jpg", rank: 0),
            Entry("unrated.jpg"),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Rating, Ascending: true, GroupFoldersFirst: false)));

        Assert.Equal("rated.jpg", items[^1].Name);
        Assert.Equal(new[] { "explicit.jpg", "unrated.jpg" }, items.Take(2).Select(e => e.Name));
    }

    [Fact]
    public void Rating_BreaksTiesByName() {
        var items = new List<FileSystemEntry> {
            Entry("c.jpg", rank: 3),
            Entry("a.jpg", rank: 3),
            Entry("b.jpg", rank: 3),
        };
        items.Sort(EntryComparers.Build(new SortOptions(SortKey.Rating, Ascending: true, GroupFoldersFirst: false)));

        Assert.Equal(new[] { "a.jpg", "b.jpg", "c.jpg" }, items.Select(e => e.Name));
    }


    // --- Sort (the folders-first split both filesystems share) -----------

    [Fact]
    public void Sort_GroupFoldersFirst_PutsFoldersOnTop() {
        var items = new[] {
            Entry("z.txt"),
            Entry("beta", kind: EntryKind.Directory),
            Entry("a.txt"),
            Entry("alpha", kind: EntryKind.Directory),
        };

        var sorted = EntryComparers.Sort(items, new SortOptions(SortKey.Name, Ascending: true, GroupFoldersFirst: true));

        Assert.Equal(new[] { "alpha", "beta", "a.txt", "z.txt" }, sorted.Select(e => e.Name));
    }

    [Fact]
    public void Sort_WithoutGrouping_MergesFoldersAndFiles() {
        var items = new[] {
            Entry("z.txt"),
            Entry("beta", kind: EntryKind.Directory),
            Entry("a.txt"),
        };

        var sorted = EntryComparers.Sort(items, new SortOptions(SortKey.Name, Ascending: true, GroupFoldersFirst: false));

        Assert.Equal(new[] { "a.txt", "beta", "z.txt" }, sorted.Select(e => e.Name));
    }

    [Fact]
    public void Sort_LeavesTheInputAlone() {
        var items = new[] { Entry("b.txt"), Entry("a.txt") };

        EntryComparers.Sort(items, SortOptions.Default);

        Assert.Equal("b.txt", items[0].Name);
    }
}
