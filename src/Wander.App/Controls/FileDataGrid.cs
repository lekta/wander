using System.Collections;
using System.Windows.Controls;

namespace Wander.App.Controls;

/// <summary>
/// The table: a <see cref="DataGrid"/> that takes a whole selection as one
/// change - see <see cref="FileListBox"/>. A grid has no
/// <c>SetSelectedItems</c>; its own batch (<c>BeginUpdateSelectedItems</c>)
/// is the same one change inside WPF.
/// </summary>
public sealed class FileDataGrid : DataGrid {
    /// <summary>Makes <paramref name="items"/> the selection, in one change and one SelectionChanged.</summary>
    public void ReplaceSelection(IEnumerable items) {
        var wanted = items.Cast<object>().ToList();
        var keep = new HashSet<object>(wanted, ReferenceEqualityComparer.Instance);
        BeginUpdateSelectedItems();
        try {
            for (int i = SelectedItems.Count - 1; i >= 0; i--) {
                if (!keep.Contains(SelectedItems[i]!)) {
                    SelectedItems.RemoveAt(i);
                }
            }

            var present = new HashSet<object>(SelectedItems.Cast<object>(), ReferenceEqualityComparer.Instance);
            foreach (var item in wanted) {
                if (present.Add(item)) {
                    SelectedItems.Add(item);
                }
            }
        } finally {
            EndUpdateSelectedItems();
        }
    }
}
