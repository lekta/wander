using Wander.Core.Navigation;

namespace Wander.Core.Tests;

public class RecentPathsTests {
    private const string Foo = @"D:\foo";
    private const string Bar = @"D:\bar";
    private const string Baz = @"D:\baz";


    [Fact]
    public void Add_PutsNewestFirst() {
        var recent = new RecentPaths();
        recent.Add(Foo);
        recent.Add(Bar);

        Assert.Equal(new[] { Bar, Foo }, recent.Items);
    }

    [Fact]
    public void Add_MovesKnownPathToFrontWithoutDuplicating() {
        var recent = new RecentPaths();
        recent.Add(Foo);
        recent.Add(Bar);
        recent.Add(Foo);

        Assert.Equal(new[] { Foo, Bar }, recent.Items);
    }

    [Fact]
    public void Add_TreatsCaseAndTrailingSeparatorAsSamePath() {
        var recent = new RecentPaths();
        recent.Add(Foo);
        recent.Add(@"d:\FOO\");

        Assert.Single(recent.Items);
    }

    [Fact]
    public void Add_IgnoresRepeatOfTheCurrentHead() {
        var recent = new RecentPaths();
        recent.Add(Foo);
        recent.Add(Foo);

        Assert.Equal(new[] { Foo }, recent.Items);
    }

    [Fact]
    public void Add_IgnoresEmptyPaths() {
        var recent = new RecentPaths();
        recent.Add("");
        recent.Add("   ");

        Assert.Empty(recent.Items);
    }

    [Fact]
    public void Add_DropsOldestPastCapacity() {
        var recent = new RecentPaths(capacity: 2);
        recent.Add(Foo);
        recent.Add(Bar);
        recent.Add(Baz);

        Assert.Equal(new[] { Baz, Bar }, recent.Items);
    }

    [Fact]
    public void Load_KeepsOrderAndDropsDuplicatesAndOverflow() {
        var recent = new RecentPaths(capacity: 2);
        recent.Load(new[] { Foo, @"D:\FOO", Bar, Baz });

        Assert.Equal(new[] { Foo, Bar }, recent.Items);
    }

    [Fact]
    public void Load_ReplacesPreviousContent() {
        var recent = new RecentPaths();
        recent.Add(Foo);
        recent.Load(new[] { Bar });

        Assert.Equal(new[] { Bar }, recent.Items);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecentPaths(0));
    }
}
