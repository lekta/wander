using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class FindWalkTests {
    private static FileSystemEntry Row(string name, string? snippet, EntryKind kind = EntryKind.File) {
        return new FileSystemEntry(name, @"C:\found\" + name, kind, 1, DateTime.UnixEpoch, false, false, false, false,
            MatchSnippet: snippet);
    }


    [Fact]
    public void Next_TheFoundRowAfterTheShownOne_SkippingThoseFoundByName() {
        var rows = new[] { Row("a.txt", "...x..."), Row("b.txt", null), Row("c.txt", "...x..."), Row("d.txt", "x") };

        Assert.Equal("c.txt", FindWalk.Next(rows, @"C:\found\a.txt")?.Name);
        Assert.Equal("d.txt", FindWalk.Next(rows, @"C:\FOUND\C.TXT")?.Name);
    }

    [Fact]
    public void Next_PastTheLast_Null() {
        var rows = new[] { Row("a.txt", "x"), Row("b.txt", "x") };

        Assert.Null(FindWalk.Next(rows, @"C:\found\b.txt"));
    }

    [Fact]
    public void Next_ShownNotAmongTheRows_TheFirstFound() {
        var rows = new[] { Row("sub", "x", EntryKind.Directory), Row("a.txt", null), Row("b.txt", "x") };

        Assert.Equal("b.txt", FindWalk.Next(rows, null)?.Name);
        Assert.Equal("b.txt", FindWalk.Next(rows, @"C:\elsewhere\z.txt")?.Name);
    }
}
