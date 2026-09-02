using System.Windows;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

public partial class ConflictDialog : Window {
    private ConflictDialog(FileConflictInfo conflict) {
        InitializeComponent();
        App.ParkIfHeadless(this);

        HeaderText.Text = string.Format(Strings.ConflictHeader, conflict.ExistingTarget.Name);

        SourceName.Text = conflict.Source.Name;
        SourceSize.Text = DescribeSize(conflict.Source.Size);
        SourceModified.Text = TimeFormat.FromUtc(conflict.Source.ModifiedUtc);

        TargetName.Text = conflict.ExistingTarget.Name;
        TargetSize.Text = DescribeSize(conflict.ExistingTarget.Size);
        TargetModified.Text = TimeFormat.FromUtc(conflict.ExistingTarget.ModifiedUtc);
    }


    public ConflictResolution Result { get; private set; } = ConflictResolution.Cancel;


    public static ConflictResolution Show(FileConflictInfo conflict) {
        var dlg = new ConflictDialog(conflict) {
            Owner = Application.Current?.MainWindow,
        };
        dlg.ShowDialog();
        return dlg.Result;
    }


    /// <summary>
    /// Sizes are formatted the same way everywhere; the only thing this
    /// dialog says differently is what "no size" means. A folder has none,
    /// and here that is worth saying in words — elsewhere an em dash is
    /// enough, because the row already shows the kind.
    /// </summary>
    private static string DescribeSize(long? size) {
        return size is { } bytes ? SizeFormatter.Format(bytes) : Strings.KindFolderNoun;
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
