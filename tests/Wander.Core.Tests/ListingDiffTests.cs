using Wander.Core.FileSystem;
using Wander.Core.Listing;

namespace Wander.Core.Tests;

public class ListingDiffTests {

    private static FileSystemEntry Row(string name, long size = 0) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\folder\" + name,
            Kind: EntryKind.File,
            Size: size,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }


    /// <summary>
    /// Replays a plan the way the view model does, so every test proves the
    /// plan lands on exactly the incoming listing.
    /// </summary>
    private static List<FileSystemEntry> Apply(
        IReadOnlyList<FileSystemEntry> current, IReadOnlyList<FileSystemEntry> incoming, ListingDiffPlan plan) {
        if (plan.Wholesale) {
            return new List<FileSystemEntry>(incoming);
        }

        var rows = new List<FileSystemEntry>(current);
        foreach (var edit in plan.Edits) {
            switch (edit.Kind) {
                case ListingEditKind.RemoveAt:
                    rows.RemoveAt(edit.Index);
                    break;
                case ListingEditKind.Insert:
                    rows.Insert(edit.Index, edit.Entry!);
                    break;
                case ListingEditKind.Move:
                    var moved = rows[edit.Index];
                    rows.RemoveAt(edit.Index);
                    rows.Insert(edit.ToIndex, moved);
                    break;
                case ListingEditKind.Replace:
                    rows[edit.Index] = edit.Entry!;
                    break;
            }
        }

        return rows;
    }


    private static void AssertLandsOnIncoming(FileSystemEntry[] current, FileSystemEntry[] incoming) {
        var plan = ListingDiff.Compute(current, incoming);

        Assert.Equal(incoming, Apply(current, incoming, plan));
    }


    // --- Nothing changed --------------------------------------------------

    [Fact]
    public void SameRows_ProduceNoEdits() {
        // The heart of "the list does not twitch": an unchanged listing must
        // not touch a single container.
        var rows = new[] { Row("a.txt"), Row("b.txt"), Row("c.txt") };

        var plan = ListingDiff.Compute(rows, rows.ToArray());

        Assert.False(plan.Wholesale);
        Assert.Empty(plan.Edits);
    }

    [Fact]
    public void SamePathsDifferentInstances_ProduceNoEdits() {
        // Two enumerations of the same folder are two sets of instances;
        // equal facts must still mean "leave the row alone".
        var current = new[] { Row("a.txt"), Row("b.txt") };
        var incoming = new[] { Row("a.txt"), Row("b.txt") };

        var plan = ListingDiff.Compute(current, incoming);

        Assert.Empty(plan.Edits);
    }


    // --- Single edits -----------------------------------------------------

    [Fact]
    public void ChangedFacts_ReplaceThatRowOnly() {
        // A rating landing on one photo must cost exactly one container.
        var current = new[] { Row("a.txt"), Row("b.txt", size: 1), Row("c.txt") };
        var incoming = new[] { Row("a.txt"), Row("b.txt", size: 2), Row("c.txt") };

        var plan = ListingDiff.Compute(current, incoming);

        var edit = Assert.Single(plan.Edits);
        Assert.Equal(ListingEditKind.Replace, edit.Kind);
        Assert.Equal(1, edit.Index);
        Assert.Equal(incoming, Apply(current, incoming, plan));
    }

    [Fact]
    public void LastRowGone_IsOneRemoval() {
        // Rows above the removal keep their positions, so the threshold
        // keeps the reconcile path and exactly one row leaves.
        var current = new[] { Row("a.txt"), Row("b.txt"), Row("c.txt") };
        var incoming = new[] { Row("a.txt"), Row("b.txt") };

        var plan = ListingDiff.Compute(current, incoming);

        var edit = Assert.Single(plan.Edits);
        Assert.Equal(ListingEditKind.RemoveAt, edit.Kind);
        Assert.Equal(incoming, Apply(current, incoming, plan));
    }

    [Fact]
    public void RowAppended_IsOneInsert() {
        var current = new[] { Row("a.txt"), Row("b.txt") };
        var incoming = new[] { Row("a.txt"), Row("b.txt"), Row("c.txt") };

        var plan = ListingDiff.Compute(current, incoming);

        var edit = Assert.Single(plan.Edits);
        Assert.Equal(ListingEditKind.Insert, edit.Kind);
        Assert.Equal(incoming, Apply(current, incoming, plan));
    }

    [Fact]
    public void Rename_IsRemovePlusInsert() {
        // A renamed file is a different path — the row cannot survive, and
        // pretending otherwise would leave a container showing the old name.
        var current = new[] { Row("old.txt"), Row("z.txt") };
        var incoming = new[] { Row("new.txt"), Row("z.txt") };

        var plan = ListingDiff.Compute(current, incoming);

        Assert.Equal(
            new[] { ListingEditKind.RemoveAt, ListingEditKind.Insert },
            plan.Edits.Select(e => e.Kind).ToArray());
        Assert.Equal(incoming, Apply(current, incoming, plan));
    }

    [Fact]
    public void PathsMatchCaseInsensitively() {
        // NTFS says A.TXT and a.txt are the same file; a case difference
        // alone must not tear the row down and rebuild it.
        var current = new[] { Row("a.txt"), Row("b.txt") };
        var incoming = new[] {
            Row("a.txt") with { FullPath = @"C:\FOLDER\A.TXT" },
            Row("b.txt"),
        };

        var plan = ListingDiff.Compute(current, incoming);

        // Name differs ("a.txt" vs the original casing is kept — Name is
        // unchanged here), so no Replace either: the facts say the same.
        Assert.DoesNotContain(plan.Edits, e => e.Kind is ListingEditKind.RemoveAt or ListingEditKind.Insert);
    }


    // --- Reorders ---------------------------------------------------------

    [Fact]
    public void SmallReorder_MovesWithinThreshold() {
        // Four of six rows still line up — worth reconciling, not rebuilding.
        var a = Row("a.txt");
        var b = Row("b.txt");
        var current = new[] { a, b, Row("c.txt"), Row("d.txt"), Row("e.txt"), Row("f.txt") };
        var incoming = new[] { b, a, Row("c.txt"), Row("d.txt"), Row("e.txt"), Row("f.txt") };

        var plan = ListingDiff.Compute(current, incoming);

        Assert.False(plan.Wholesale);
        Assert.Equal(incoming, Apply(current, incoming, plan));
    }

    [Fact]
    public void MixedChanges_LandOnTheIncomingListing() {
        // Remove + insert + move + replace in one pass — the round trip is
        // the property that matters, whatever the plan looks like inside.
        var current = new[] {
            Row("a.txt"), Row("b.txt"), Row("c.txt", size: 1), Row("d.txt"), Row("e.txt"), Row("g.txt"),
        };
        var incoming = new[] {
            Row("a.txt"), Row("b.txt"), Row("c.txt", size: 2), Row("f.txt"), Row("g.txt"), Row("e.txt"),
        };

        var plan = ListingDiff.Compute(current, incoming);

        Assert.False(plan.Wholesale);
        Assert.Equal(incoming, Apply(current, incoming, plan));
    }


    // --- Wholesale --------------------------------------------------------

    [Fact]
    public void FlippedSort_IsWholesale() {
        // Flipping the sort on a folder moves every row; reconciling that
        // item by item is all cost and no benefit.
        var current = new[] { Row("a.txt"), Row("b.txt"), Row("c.txt"), Row("d.txt") };
        var incoming = current.Reverse().ToArray();

        Assert.True(ListingDiff.Compute(current, incoming).Wholesale);
    }

    [Fact]
    public void EmptyToRows_IsWholesale() {
        Assert.True(ListingDiff.Compute(Array.Empty<FileSystemEntry>(), new[] { Row("a.txt") }).Wholesale);
    }

    [Fact]
    public void RowsToEmpty_IsWholesale() {
        Assert.True(ListingDiff.Compute(new[] { Row("a.txt") }, Array.Empty<FileSystemEntry>()).Wholesale);
    }

    [Fact]
    public void RowGoneNearTheTop_IsWholesale() {
        // Alignment is positional: removing an early row shifts every row
        // below it, so the threshold honestly reports "almost nothing lines
        // up" and the list rebuilds in one Reset. Deliberate in the original
        // and preserved here — the shifted rows would each need a Move
        // anyway, which is no cheaper and no less disruptive.
        var current = new[] { Row("a.txt"), Row("b.txt"), Row("c.txt"), Row("d.txt"), Row("e.txt") };
        var incoming = new[] { Row("b.txt"), Row("c.txt"), Row("d.txt"), Row("e.txt") };

        Assert.True(ListingDiff.Compute(current, incoming).Wholesale);
    }

    [Fact]
    public void HalfAligned_IsNotWholesale() {
        // Exactly at the threshold: aligned * 2 == count keeps the
        // reconcile path — the rebuild is for listings that share less.
        var current = new[] { Row("a.txt"), Row("b.txt"), Row("c.txt"), Row("d.txt") };
        var incoming = new[] { Row("a.txt"), Row("b.txt"), Row("x.txt"), Row("y.txt") };

        Assert.False(ListingDiff.Compute(current, incoming).Wholesale);
    }
}
