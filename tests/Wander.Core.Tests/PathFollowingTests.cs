using Wander.Core.Navigation;

namespace Wander.Core.Tests;

/// <summary>
/// A folder Wander moved: who is told, in what order (REDESIGN 4.12, F-1,
/// F-3). What each holder does with it is its own class's test
/// (FolderSession, ClipboardController, RecentPaths, FolderSettingsBook,
/// NavigationService).
/// </summary>
public class PathFollowingTests {
    /// <summary>F-1: every holder is told, the panels first and the history - the navigation - last.</summary>
    [Fact]
    public void EveryHolderIsTold_ThePanelsFirst_TheHistoryLast() {
        var plan = PathFollowing.Plan(new[] { (@"C:\A", @"C:\B") });

        Assert.Equal(Enum.GetValues<PathHolder>().Length, plan.Count);
        Assert.Equal(PathHolder.Panels, plan[0].Holder);
        Assert.Equal(PathHolder.History, plan[^1].Holder);
        Assert.Equal(Enum.GetValues<PathHolder>().OrderBy(h => h), plan.Select(p => p.Holder).OrderBy(h => h));
    }

    /// <summary>What the history's navigation reads on the way - the pinned view, where the user was - has moved before it.</summary>
    [Fact]
    public void TheMemoryAndTheBook_MoveBeforeTheHistory() {
        var order = PathFollowing.Order.ToList();

        Assert.True(order.IndexOf(PathHolder.SelectionMemory) < order.IndexOf(PathHolder.History));
        Assert.True(order.IndexOf(PathHolder.FolderBook) < order.IndexOf(PathHolder.History));
    }

    /// <summary>Several folders moved at once: each holder hears of every one before the next holder hears of any.</summary>
    [Fact]
    public void SeveralMoves_HolderByHolder() {
        var plan = PathFollowing.Plan(new[] { (@"C:\A", @"D:\A"), (@"C:\B", @"D:\B") });

        Assert.Equal((PathHolder.Panels, @"C:\A", @"D:\A"), plan[0]);
        Assert.Equal((PathHolder.Panels, @"C:\B", @"D:\B"), plan[1]);
        Assert.Equal(PathHolder.Bookmarks, plan[2].Holder);
    }

    /// <summary>F-3, and a move that went nowhere: a folder "moved" onto itself asks nothing of anyone.</summary>
    [Fact]
    public void AMoveOntoItself_AsksNothing() {
        Assert.Empty(PathFollowing.Plan(new[] { (@"C:\A\", @"c:\a") }));
    }
}
