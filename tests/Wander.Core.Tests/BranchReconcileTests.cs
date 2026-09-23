using Wander.Core.Panels;

namespace Wander.Core.Tests;

/// <summary>
/// The rule a level of a folder panel is brought in line with the disk by.
/// What is asserted is identity: a row whose folder is still there is
/// never replaced, because a replaced row is a closed branch and a lost
/// highlight - the tree closing itself.
/// </summary>
public class BranchReconcileTests {
    private const string A = @"C:\x\a";
    private const string B = @"C:\x\b";
    private const string C = @"C:\x\c";
    private const string D = @"C:\x\d";


    [Fact]
    public void SameRows_NeedNoEdit() {
        Assert.Empty(BranchReconcile.Plan(new[] { A, B, C }, new[] { A, B, C }));
    }

    [Fact]
    public void RowsCompare_WithoutCase_AndWithoutATrailingSeparator() {
        Assert.Empty(BranchReconcile.Plan(new[] { @"c:\X\A\", B }, new[] { A, B }));
    }

    [Fact]
    public void ANewFolder_IsInsertedWhereTheDiskListsIt_AndNothingElseIsTouched() {
        var edits = BranchReconcile.Plan(new[] { A, C }, new[] { A, B, C });

        Assert.Equal(new[] { BranchEdit.Insert(1) }, edits);
        Assert.Equal(new[] { A, B, C }, Apply(new[] { A, C }, new[] { A, B, C }, edits));
    }

    [Fact]
    public void AFolderGone_IsRemoved_AndTheRestKeepTheirRows() {
        var shown = new[] { A, B, C };
        var fresh = new[] { A, C };

        var edits = BranchReconcile.Plan(shown, fresh);

        Assert.DoesNotContain(edits, e => e.Kind == BranchEditKind.Insert);
        Assert.Equal(fresh, Apply(shown, fresh, edits));
    }

    [Fact]
    public void ADifferentOrderOnDisk_MovesRows_RatherThanReplacingThem() {
        var shown = new[] { A, B, C };
        var fresh = new[] { C, A, B };

        var edits = BranchReconcile.Plan(shown, fresh);

        Assert.All(edits, e => Assert.Equal(BranchEditKind.Move, e.Kind));
        Assert.Equal(fresh, Apply(shown, fresh, edits));
    }

    [Fact]
    public void ARenamedFolder_IsOneRowGoneAndOneArrived_UnlessTheRowFollowedFirst() {
        var replaced = BranchReconcile.Plan(new[] { A, B }, new[] { A, D });
        Assert.Contains(replaced, e => e.Kind == BranchEditKind.Insert);
        Assert.Contains(replaced, e => e.Kind == BranchEditKind.Remove);

        // The row already stands on the new path (TreeNodeViewModel.Follow):
        // it is found and kept, with whatever is open under it.
        Assert.Empty(BranchReconcile.Plan(new[] { A, D }, new[] { A, D }));
    }

    [Fact]
    public void AnEmptyLevel_IsFilledInDiskOrder() {
        Assert.Equal(
            new[] { BranchEdit.Insert(0), BranchEdit.Insert(1) },
            BranchReconcile.Plan(Array.Empty<string>(), new[] { A, B }));
    }

    [Fact]
    public void EverythingGone_IsRemovedFromTheEnd() {
        Assert.Equal(
            new[] { BranchEdit.Remove(1), BranchEdit.Remove(0) },
            BranchReconcile.Plan(new[] { A, B }, Array.Empty<string>()));
    }


    /// <summary>Applies the edits the way the panel does, on plain strings.</summary>
    private static List<string> Apply(IReadOnlyList<string> shown, IReadOnlyList<string> fresh, IReadOnlyList<BranchEdit> edits) {
        var level = new List<string>(shown);
        foreach (var edit in edits) {
            switch (edit.Kind) {
                case BranchEditKind.Insert:
                    level.Insert(edit.Index, fresh[edit.Index]);
                    break;
                case BranchEditKind.Move:
                    string moved = level[edit.From];
                    level.RemoveAt(edit.From);
                    level.Insert(edit.Index, moved);
                    break;
                case BranchEditKind.Remove:
                    level.RemoveAt(edit.Index);
                    break;
            }
        }

        return level;
    }
}
