using System.Windows;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Icons;
using Wander.Core.Persistence;

namespace Wander.App.Views;

/// <summary>
/// Code-behind for the settings dialog. The settings VM is owned by
/// MainViewModel and live-applied — every property change immediately
/// flows through OnSettingsChanged (refresh + persist). This dialog adds
/// OK / Apply / Cancel semantics on top of that:
///
///  - <b>Baseline</b> is captured when the dialog opens. It's the snapshot
///    we restore to on Cancel.
///  - <b>Apply</b> commits the live state as the new baseline (so a later
///    Cancel won't roll past it). Save-to-disk already happened through the
///    live-update path; Apply just shifts the rollback anchor.
///  - <b>OK</b> = Apply + close.
///  - <b>Cancel</b> = restore baseline + close. Restoration goes through
///    SettingsViewModel.ApplyFrom which fires PropertyChanged on each
///    setter, so the file list / tree / bookmarks refresh exactly as if
///    the user had toggled each control back by hand.
/// </summary>
public partial class SettingsWindow : Window {
    private AppSettings _baseline = new();


    public SettingsWindow() {
        InitializeComponent();
        Loaded += OnLoaded;
    }


    private void OnLoaded(object sender, RoutedEventArgs e) {
        if (DataContext is SettingsViewModel vm) {
            _baseline = vm.ToRecord();
            RefreshCacheStatus(vm);
        }
    }


    private void Ok_Click(object sender, RoutedEventArgs e) {
        CommitBaseline();
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e) {
        CommitBaseline();
    }

    /// <summary>
    /// Reset to defaults. Destructive in the same sense a file operation is
    /// — it discards choices the user cannot get back — so it asks first, with
    /// Cancel as the default button. The reset applies live like any other
    /// change; Cancel still rolls it back, because the baseline snapshot is
    /// deliberately left alone here.
    /// </summary>
    private void Reset_Click(object sender, RoutedEventArgs e) {
        if (DataContext is not SettingsViewModel vm) {
            return;
        }

        var answer = MessageBox.Show(
            this,
            Strings.SettingsResetConfirm,
            Strings.SettingsResetGroup,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (answer == MessageBoxResult.OK) {
            vm.ApplyFrom(new AppSettings());
        }
    }

    /// <summary>
    /// Drops every cached thumbnail. Not guarded by a confirmation: nothing
    /// is lost that Wander cannot rebuild on the next visit to the folder.
    /// </summary>
    private void ClearThumbnails_Click(object sender, RoutedEventArgs e) {
        if (DataContext is not SettingsViewModel vm) {
            return;
        }

        if (ServiceLocator.IsRegistered<IIconProvider>()) {
            ServiceLocator.Get<IIconProvider>().ClearCache();
        }
        // Decoded copies live a tier above the provider's; leaving them
        // would make the button look like it did nothing.
        Controls.IconImageCache.Clear();
        RefreshCacheStatus(vm);
    }


    /// <summary>
    /// Reads the cache folder's real size. Cheap (a directory listing), and
    /// only ever done when the dialog opens or right after a clear.
    /// </summary>
    private static void RefreshCacheStatus(SettingsViewModel vm) {
        if (!ServiceLocator.IsRegistered<IIconProvider>()) {
            vm.ThumbnailCacheStatus = "";
            return;
        }

        var (directory, size) = ServiceLocator.Get<IIconProvider>().DescribeCache();
        vm.ThumbnailCacheStatus = directory is null
            ? Strings.SettingsCacheOff
            : string.Format(Strings.SettingsCacheUsage, SizeFormatter.Format(size), directory);
    }


    private void Cancel_Click(object sender, RoutedEventArgs e) {
        if (DataContext is SettingsViewModel vm) {
            vm.ApplyFrom(_baseline);
        }
        Close();
    }


    private void CommitBaseline() {
        if (DataContext is SettingsViewModel vm) {
            _baseline = vm.ToRecord();
        }
    }
}
