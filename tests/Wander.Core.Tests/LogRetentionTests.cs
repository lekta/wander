using Wander.Core.Logging;

namespace Wander.Core.Tests;

public class LogRetentionTests {
    private static readonly DateTime _start = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void Select_UnderTheLimit_RemovesNothing() {
        Assert.Empty(LogRetention.Select(Files(3), keep: 200));
    }

    [Fact]
    public void Select_AtTheLimit_RemovesNothing() {
        Assert.Empty(LogRetention.Select(Files(200), keep: 200));
    }

    [Fact]
    public void Select_KeepsTheNewest() {
        // Files(n) numbers them oldest first, one minute apart.
        var removed = LogRetention.Select(Files(10), keep: 4);

        Assert.Equal(6, removed.Count);
        Assert.Equal(new[] { "log-05", "log-04", "log-03", "log-02", "log-01", "log-00" }, removed);
    }

    [Fact]
    public void Select_BreaksTiesByName() {
        // A dozen runs inside the same second is the normal case for the
        // smoke check, and the order of a directory listing is not.
        var files = new[] {
            ("log-b", _start),
            ("log-c", _start),
            ("log-a", _start),
        };

        Assert.Equal(new[] { "log-a" }, LogRetention.Select(files, keep: 2));
    }

    [Fact]
    public void Select_WithoutRoom_RemovesEverything() {
        Assert.Equal(3, LogRetention.Select(Files(3), keep: 0).Count);
    }


    private static (string Name, DateTime WrittenUtc)[] Files(int count) {
        var files = new (string, DateTime)[count];
        for (int i = 0; i < count; i++) {
            files[i] = ($"log-{i:00}", _start.AddMinutes(i));
        }

        return files;
    }
}
