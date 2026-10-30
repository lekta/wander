using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core.Navigation;

namespace Wander.App.Views;

/// <summary>
/// The bookmarks' page: renaming, moving, switching off and removing, for
/// all of them, the absent included. Decides nothing - what a key or a button
/// means for the list is <c>BookmarksController</c>'s.
/// </summary>
public partial class BookmarksManagerView : UserControl {
    private BookmarkRowViewModel? _editing;


    public BookmarksManagerView() {
        InitializeComponent();
    }


    private MainViewModel Vm => (MainViewModel)DataContext;


    /// <summary>Takes the keyboard, onto the row it was on - else the first.</summary>
    public void FocusRows() {
        if (Rows.SelectedItem is null && Rows.Items.Count > 0) {
            Rows.SelectedIndex = 0;
        }

        Rows.UpdateLayout();
        if (Rows.ItemContainerGenerator.ContainerFromItem(Rows.SelectedItem) is FrameworkElement row) {
            row.Focus();
        } else {
            Rows.Focus();
        }
    }


    // --- Keyboard and mouse -----------------------------------------------

    private void Rows_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (_editing is not null || Rows.SelectedItem is not BookmarkRowViewModel row) {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        switch (e.Key) {
            case Key.F2 when modifiers == ModifierKeys.None:
                BeginEdit(row);
                break;
            case Key.Enter when modifiers == ModifierKeys.None:
                Open(row);
                break;
            case Key.Space when modifiers == ModifierKeys.None:
                row.IsShown = !row.IsShown;
                break;
            case Key.Delete when modifiers is ModifierKeys.None or ModifierKeys.Shift:
                Remove(row);
                break;
            case Key.Up when modifiers == ModifierKeys.Control:
                Move(row, -1);
                break;
            case Key.Down when modifiers == ModifierKeys.Control:
                Move(row, +1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Rows_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        var hits = ListVisuals.Ancestors(e.OriginalSource).ToArray();
        if (!hits.OfType<ButtonBase>().Any() && !hits.OfType<TextBox>().Any()
            && hits.OfType<ListBoxItem>().FirstOrDefault()?.DataContext is BookmarkRowViewModel row) {
            Open(row);
        }
    }

    /// <summary>The row's menu: what the row's buttons do, and what only a row whose folder is gone can.</summary>
    private void Rows_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        if (ListVisuals.Ancestors(e.OriginalSource).OfType<ListBoxItem>().FirstOrDefault() is not { DataContext: BookmarkRowViewModel row } host) {
            return;
        }

        Rows.SelectedItem = row;
        var menu = new ContextMenu { PlacementTarget = host, Placement = PlacementMode.MousePoint };
        if (row.IsGone && !row.IsBuiltIn) {
            AddItem(menu, Strings.BookmarksLocate, null, true, () => Vm.RelocateBookmark(row.Path));
            menu.Items.Add(new Separator());
        }
        AddItem(menu, Strings.BookmarksRename, "F2", true, () => BeginEdit(row));
        menu.Items.Add(new Separator());
        AddItem(menu, Strings.BookmarksMoveUp, "Ctrl+↑", row.CanMoveUp, () => Move(row, -1));
        AddItem(menu, Strings.BookmarksMoveDown, "Ctrl+↓", row.CanMoveDown, () => Move(row, +1));
        if (!row.IsBuiltIn) {
            menu.Items.Add(new Separator());
            AddItem(menu, Strings.BookmarksRemove, "Del", true, () => Remove(row));
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void AddItem(ContextMenu menu, string header, string? gesture, bool enabled, Action run) {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled };
        item.Click += (_, _) => run();
        menu.Items.Add(item);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) {
        Move(RowOf(sender), -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e) {
        Move(RowOf(sender), +1);
    }

    private void Remove_Click(object sender, RoutedEventArgs e) {
        Remove(RowOf(sender));
    }

    private static BookmarkRowViewModel RowOf(object sender) {
        return (BookmarkRowViewModel)((FrameworkElement)sender).DataContext;
    }


    // --- What the row asks for --------------------------------------------

    /// <summary>Goes to the bookmark; a device not plugged in has nothing to go to.</summary>
    private void Open(BookmarkRowViewModel row) {
        if (!row.IsDeviceAway) {
            Vm.NavigateAndSelectFolder(row.Path, NavigationSource.Bookmark);
        }
    }

    private void Move(BookmarkRowViewModel row, int delta) {
        if (Vm.Bookmarks.Move(row.Path, delta, overAbsent: true)) {
            Rows.SelectedItem = row;
            FocusRows();
        }
    }

    /// <summary>
    /// The user's own bookmark goes, the folder stays; the built-in rows
    /// have nothing to go from, and are switched off - what Delete does to
    /// them in the panel.
    /// </summary>
    private void Remove(BookmarkRowViewModel row) {
        if (row.IsBuiltIn) {
            row.IsShown = false;

            return;
        }

        int at = Rows.SelectedIndex;
        Vm.Bookmarks.Remove(row.Path);
        Rows.SelectedIndex = Math.Min(at, Rows.Items.Count - 1);
        FocusRows();
    }


    // --- The name in place ------------------------------------------------

    private void BeginEdit(BookmarkRowViewModel row) {
        _editing = row;
        row.IsEditing = true;
        Rows.UpdateLayout();
        if (Rows.ItemContainerGenerator.ContainerFromItem(row) is FrameworkElement host
            && ListVisuals.FindDescendant<TextBox>(host, "NameEditor") is { } box) {
            box.Text = row.Name;
            box.Focus();
            box.SelectAll();
        }
    }

    private void NameEditor_PreviewKeyDown(object sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.Enter:
                Commit((TextBox)sender);
                FocusRows();
                e.Handled = true;
                break;
            case Key.Escape:
                EndEdit();
                FocusRows();
                e.Handled = true;
                break;
        }
    }

    private void NameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
        Commit((TextBox)sender);
    }

    /// <summary>A blank name gives the default back. Taking the editor down loses its focus too, and that lands here with nothing to do.</summary>
    private void Commit(TextBox box) {
        if (EndEdit() is { } row) {
            Vm.Bookmarks.Rename(row.Path, box.Text);
        }
    }

    private BookmarkRowViewModel? EndEdit() {
        var row = _editing;
        _editing = null;
        if (row is not null) {
            row.IsEditing = false;
        }

        return row;
    }
}
