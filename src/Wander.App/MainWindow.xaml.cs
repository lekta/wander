using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;

namespace Wander.App;

public partial class MainWindow : Window {
    private bool _userClickedExpander;
    private bool _altWasHeld;


    public MainWindow() {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;


    // --- File list selection / opening ---------------------------------

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        OpenSelected();
    }

    private void Tiles_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        OpenSelected();
    }

    private void Rename_Click(object sender, RoutedEventArgs e) {
        if (Vm.SelectedEntry is not FileSystemEntry entry) {
            return;
        }

        string? input = PromptDialog.Show("Rename", "New name:", entry.Name);
        if (input is null || input == entry.Name) {
            return;
        }

        Vm.RenameCommand.Execute(input);
    }


    private void OpenSelected() {
        if (Vm.SelectedEntry is FileSystemEntry entry) {
            Vm.OpenEntry(entry);
        }
    }


    // --- Tree: selection -----------------------------------------------

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        if (e.NewValue is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
            Vm.NavigateTo(node.FullPath);
        }
    }


    // --- Tree: custom expand/collapse semantics ------------------------
    //
    // Default WPF behavior is "click chevron → toggle this node, leave the
    // IsExpanded state of children untouched". Wander adds two Alt-modifier
    // gestures on top of that; plain clicks behave like default WPF.
    //
    //   Collapsed node:
    //     plain click      → expand this node only
    //     Alt + click      → expand this node AND its direct children
    //                        (one level deep; grandchildren are lazy-loaded
    //                        but not opened)
    //
    //   Expanded node:
    //     plain click      → collapse this node only
    //                        (children keep their IsExpanded state, so
    //                        re-expanding restores the previous shape)
    //     Alt + click      → collapse this node AND every descendant
    //                        (next plain expand shows just this node)
    //
    // The _userClickedExpander flag distinguishes UI clicks from programmatic
    // IsExpanded changes (state restore, tree-to-current expansion), so those
    // never trigger the Alt rules.

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (HitTestExpander(e.OriginalSource as DependencyObject)) {
            _userClickedExpander = true;
            _altWasHeld = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        } else {
            _userClickedExpander = false;
            _altWasHeld = false;
        }
    }

    private void TreeViewItem_Expanded(object sender, RoutedEventArgs e) {
        bool isUserClick = _userClickedExpander;
        bool altHeld = _altWasHeld;
        _userClickedExpander = false;

        if (!isUserClick || !altHeld) {
            return;
        }

        if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is TreeNodeViewModel node) {
            ExpandDirectChildren(node);
        }
    }

    private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e) {
        bool isUserClick = _userClickedExpander;
        bool altHeld = _altWasHeld;
        _userClickedExpander = false;

        if (!isUserClick || !altHeld) {
            return;
        }

        if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is TreeNodeViewModel node) {
            CollapseRecursively(node);
        }
    }


    private static bool HitTestExpander(DependencyObject? hit) {
        while (hit is not null) {
            if (hit is ToggleButton) {
                return true;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        return false;
    }

    private static void ExpandDirectChildren(TreeNodeViewModel node) {
        foreach (var child in node.Children) {
            if (string.IsNullOrEmpty(child.FullPath)) {
                continue;
            }
            child.IsExpanded = true;
        }
    }

    private static void CollapseRecursively(TreeNodeViewModel node) {
        foreach (var child in node.Children) {
            if (child.IsExpanded) {
                CollapseRecursively(child);
                child.IsExpanded = false;
            }
        }
    }
}
