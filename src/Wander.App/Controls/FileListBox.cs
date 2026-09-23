using System.Collections;
using System.Windows.Controls;

namespace Wander.App.Controls;

/// <summary>
/// The tile views' list: a <see cref="ListBox"/> that takes a whole
/// selection in one call (AB). <c>SelectedItems.Add</c> is linear in the
/// selection inside WPF, so a selection put on row by row is quadratic -
/// five thousand rows took ten seconds, the protected
/// <c>SetSelectedItems</c> a third of one (stand SelectionProbe).
/// </summary>
public sealed class FileListBox : ListBox {
    /// <summary>Makes <paramref name="items"/> the selection, in one change and one SelectionChanged.</summary>
    public void ReplaceSelection(IEnumerable items) {
        SetSelectedItems(items);
    }
}
