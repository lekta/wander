using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wander.App.Controls;

/// <summary>
/// A folder panel: a plain list of the lines the model says are shown,
/// indented by depth (decision P3, REDESIGN 4.7). A <see cref="ListBox"/>
/// for what it gives for free - virtualisation, <c>ScrollIntoView</c>,
/// containers that take the keyboard - with its selection switched off:
/// what is lit, where the cursor is, what is open are the model's, drawn
/// from each line's own flags. Nothing here moves a selection: no click, no
/// arrow key, no letter typed, no focus arriving. WPF's own tree did all of
/// those, and every one of them had to be told apart from the user after
/// the fact.
/// </summary>
public sealed class FolderPanelList : ListBox {
    public FolderPanelList() {
        IsTextSearchEnabled = false;
        IsSynchronizedWithCurrentItem = false;
        SelectionMode = SelectionMode.Single;
    }


    /// <summary>How many lines fit the panel - what a page of PageUp / PageDown is.</summary>
    public int PageSize => Math.Max(1, (int)(ActualHeight / FolderPanelItem.LineHeight));


    protected override DependencyObject GetContainerForItemOverride() {
        return new FolderPanelItem();
    }

    protected override bool IsItemItsOwnContainerOverride(object item) {
        return item is FolderPanelItem;
    }

    /// <summary>
    /// The keys a list answers by moving its selection are the panel's own
    /// (the view posts them to the model): the list does not get them.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End
            or Key.PageUp or Key.PageDown or Key.Space) {
            return;
        }

        base.OnKeyDown(e);
    }
}


/// <summary>
/// A line of a <see cref="FolderPanelList"/>. A press puts the keyboard on
/// it and selects nothing; what the press means is the view's to post.
/// </summary>
public sealed class FolderPanelItem : ListBoxItem {
    /// <summary>The height of a line, for the page size; the template's MinHeight.</summary>
    public const double LineHeight = 22;


    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
        Focus();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) {
        e.Handled = true;
    }
}
