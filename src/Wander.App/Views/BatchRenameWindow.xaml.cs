using System.Windows;
using Wander.App.ViewModels;

namespace Wander.App.Views;

/// <summary>
/// The batch-rename window. Modal to the main window; answers only whether
/// OK was pressed - what to rename is the view model's
/// <see cref="BatchRenameViewModel.Preview"/>, and applying it is the
/// caller's.
/// </summary>
public partial class BatchRenameWindow : Window {
    public BatchRenameWindow(BatchRenameViewModel vm) {
        InitializeComponent();
        DataContext = vm;
        App.ParkIfHeadless(this);

        Closing += (_, _) => vm.Stop();
    }


    private void OnOk(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }
}
