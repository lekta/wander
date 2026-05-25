using System.Windows;
using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

public partial class ConflictDialog : Window {
    private ConflictDialog(FileConflictInfo conflict) {
        InitializeComponent();

        HeaderText.Text = $"There is already a file named '{conflict.ExistingTarget.Name}' in this location.";

        SourceName.Text = conflict.Source.Name;
        SourceSize.Text = FormatSize(conflict.Source.Size);
        SourceModified.Text = FormatModified(conflict.Source.ModifiedUtc);

        TargetName.Text = conflict.ExistingTarget.Name;
        TargetSize.Text = FormatSize(conflict.ExistingTarget.Size);
        TargetModified.Text = FormatModified(conflict.ExistingTarget.ModifiedUtc);
    }


    public ConflictResolution Result { get; private set; } = ConflictResolution.Cancel;


    public static ConflictResolution Show(FileConflictInfo conflict) {
        var dlg = new ConflictDialog(conflict) {
            Owner = Application.Current?.MainWindow,
        };
        dlg.ShowDialog();
        return dlg.Result;
    }


    private static string FormatSize(long? size) {
        if (size is null) {
            return "Folder";
        }
        long value = size.Value;
        return value switch {
            < 1024 => $"{value} B",
            < 1024 * 1024 => $"{value / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{value / (1024.0 * 1024):F1} MB",
            _ => $"{value / (1024.0 * 1024 * 1024):F2} GB",
        };
    }

    private static string FormatModified(DateTime utc) {
        if (utc == DateTime.MinValue) {
            return "—";
        }
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }


    private void OnReplace(object sender, RoutedEventArgs e) {
        Result = ConflictResolution.Replace;
        DialogResult = true;
    }

    private void OnSkip(object sender, RoutedEventArgs e) {
        Result = ConflictResolution.Skip;
        DialogResult = true;
    }

    private void OnRename(object sender, RoutedEventArgs e) {
        Result = ConflictResolution.Rename;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) {
        Result = ConflictResolution.Cancel;
        DialogResult = false;
    }
}
