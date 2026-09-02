using System.Windows;
using Wander.App.Resources;
using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

public partial class BatchConflictDialog : Window {
    private BatchConflictDialog(int conflictCount) {
        InitializeComponent();
        App.ParkIfHeadless(this);
        HeaderText.Text = string.Format(Strings.BatchConflictHeader, conflictCount);
    }

    public ConflictResolution? Result { get; private set; }


    /// <summary>
    /// Shows the dialog and returns the user's batch choice. <c>null</c> means
    /// "Resolve each" — the caller will fall through to per-item prompts.
    /// </summary>
    public static ConflictResolution? Show(int conflictCount) {
        var dlg = new BatchConflictDialog(conflictCount) {
            Owner = Application.Current?.MainWindow,
        };
        dlg.ShowDialog();
        return dlg.Result;
    }


    private void OnReplaceAll(object sender, RoutedEventArgs e) {
        Result = ConflictResolution.Replace;
        DialogResult = true;
    }

    private void OnSkipAll(object sender, RoutedEventArgs e) {
        Result = ConflictResolution.Skip;
        DialogResult = true;
    }

    private void OnResolveEach(object sender, RoutedEventArgs e) {
        Result = null;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) {
        Result = ConflictResolution.Cancel;
        DialogResult = false;
    }
}
