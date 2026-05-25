using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;

namespace Wander.App;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;


    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        if (e.NewValue is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
            Vm.NavigateTo(node.FullPath);
        }
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (Vm.SelectedEntry is FileSystemEntry entry) {
            Vm.OpenEntry(entry);
        }
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
}
