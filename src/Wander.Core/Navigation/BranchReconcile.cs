namespace Wander.Core.Navigation;

/// <summary>What one <see cref="BranchEdit"/> does to the level it is applied to.</summary>
public enum BranchEditKind {
    /// <summary>Insert the fresh row at <see cref="BranchEdit.Index"/> - it is <c>fresh[Index]</c>.</summary>
    Insert,
    /// <summary>Move the row at <see cref="BranchEdit.From"/> to <see cref="BranchEdit.Index"/>.</summary>
    Move,
    /// <summary>Remove the row at <see cref="BranchEdit.Index"/>.</summary>
    Remove,
}

/// <summary>
/// One edit that brings a level of a folder panel in line with the disk.
/// Indexes refer to the level as it stands when the edit is applied, in
/// the order <see cref="BranchReconcile.Plan"/> lists them.
/// </summary>
public readonly record struct BranchEdit(BranchEditKind Kind, int Index, int From = -1) {
    public static BranchEdit Insert(int at) {
        return new BranchEdit(BranchEditKind.Insert, at);
    }

    public static BranchEdit Move(int from, int to) {
        return new BranchEdit(BranchEditKind.Move, to, from);
    }

    public static BranchEdit Remove(int at) {
        return new BranchEdit(BranchEditKind.Remove, at);
    }
}

/// <summary>
/// How a level of a folder panel catches up with the disk without being
/// rebuilt. The rule the "tree never closes itself" pillar rests on: a row
/// whose folder is still there keeps its object - and with it whether it
/// is open, what is open under it and whether it is selected - while rows
/// are inserted, moved and removed around it. Clearing the level and
/// filling it again would close every branch below, which is the Explorer
/// behaviour Wander exists to not have.
///
/// <para>
/// A renamed folder is, to this rule, one row gone and another arrived -
/// unless the row was pointed at the new path first
/// (<c>TreeNodeViewModel.Follow</c>), in which case it is simply found
/// and kept.
/// </para>
/// </summary>
public static class BranchReconcile {
    /// <summary>
    /// The edits that turn <paramref name="shown"/> into
    /// <paramref name="fresh"/>, in the order to apply them. Paths compare
    /// without case and without a trailing separator.
    /// </summary>
    public static IReadOnlyList<BranchEdit> Plan(IReadOnlyList<string> shown, IReadOnlyList<string> fresh) {
        var edits = new List<BranchEdit>();
        var level = new List<string>(shown);
        for (int i = 0; i < fresh.Count; i++) {
            int existing = IndexOf(level, fresh[i], i);
            if (existing < 0) {
                level.Insert(i, fresh[i]);
                edits.Add(BranchEdit.Insert(i));
            } else if (existing != i) {
                level.RemoveAt(existing);
                level.Insert(i, fresh[i]);
                edits.Add(BranchEdit.Move(existing, i));
            }
        }
        while (level.Count > fresh.Count) {
            int last = level.Count - 1;
            level.RemoveAt(last);
            edits.Add(BranchEdit.Remove(last));
        }

        return edits;
    }


    private static int IndexOf(List<string> level, string path, int from) {
        for (int i = from; i < level.Count; i++) {
            if (PathsEqual(level[i], path)) {
                return i;
            }
        }

        return -1;
    }

    private static bool PathsEqual(string a, string b) {
        return string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string path) {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
