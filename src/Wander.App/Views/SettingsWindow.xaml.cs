using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wander.App.Dialogs;
using Wander.App.Resources;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.Actions;
using Wander.Core.Icons;
using Wander.Core.Persistence;
using Wander.Core.Shell;

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
        App.ParkIfHeadless(this);
        Loaded += OnLoaded;
    }


    private void OnLoaded(object sender, RoutedEventArgs e) {
        if (DataContext is SettingsViewModel vm) {
            _baseline = vm.ToRecord();
            _ = RefreshCacheStatusAsync(vm);
            ScanShellHandlers(vm);
            _ = LocateToolsAsync(vm, rescan: true);
        }
    }


    /// <summary>
    /// Finds the programs for the "Программы" page and the marks in the
    /// actions table. On opening the answers kept for the session are
    /// dropped first: the dialog is where the user comes to check whether the
    /// program they just installed is seen. The walk over <c>PATH</c> runs on
    /// the pool.
    /// </summary>
    private static async Task LocateToolsAsync(SettingsViewModel vm, bool rescan) {
        if (ServiceLocator.TryGet<IToolLocator>() is not { } locator) {
            return;
        }

        if (rescan) {
            locator.Refresh();
        }
        var catalog = vm.Actions;
        var given = vm.ToolPaths;
        var tools = await Task.Run(() => ActionCatalog.LocateTools(catalog, given, locator.Find, File.Exists));
        vm.SetToolLocations(tools);
    }


    /// <summary>
    /// Fills the context-menu table from the registry. Done on opening the
    /// dialog rather than on startup — nothing else needs it — and
    /// synchronously, because the base scopes measure around 50 ms cold and
    /// under 30 warm; a background task here would buy a flicker instead of
    /// a wait.
    /// </summary>
    private static void ScanShellHandlers(SettingsViewModel vm) {
        if (ServiceLocator.TryGet<IShellHandlerRegistry>() is not { } registry) {
            return;
        }

        try {
            vm.SetShellHandlers(registry.Scan(vm.ScannedScopes));
        } catch (Exception) {
            // A registry we cannot read costs the two informational columns,
            // never the dialog: the table still lists everything Wander has
            // met in menus, which is what the checkboxes used to offer.
        }
    }


    /// <summary>
    /// "Сбросить": everything on this page back to defaults. Asks first with
    /// Cancel as the default button — it throws away choices the user cannot
    /// get back, which is the same test the file operations use. Cancel on
    /// the dialog still rolls it back, like any other change here.
    /// </summary>
    private void ResetShellScopes_Click(object sender, RoutedEventArgs e) {
        if (DataContext is not SettingsViewModel vm) {
            return;
        }

        bool accepted = ServiceLocator.Get<IDialogs>().Ask(new DialogRequest(
            DialogKind.ShellMenuReset, Strings.SettingsShellReset, Strings.SettingsShellResetConfirm,
            DialogButtons.OkCancel, DialogIcon.Warning));
        if (!accepted) {
            return;
        }

        vm.ResetContextMenu();
        ScanShellHandlers(vm);
    }


    /// <summary>
    /// "Добавить": pick an application or a file type, and its handlers join
    /// the table. The picker does its own, wider scan — see
    /// <see cref="ShellScopePicker"/>.
    /// </summary>
    private void AddShellScope_Click(object sender, RoutedEventArgs e) {
        if (DataContext is not SettingsViewModel vm || ServiceLocator.TryGet<IShellHandlerRegistry>() is not { } registry) {
            return;
        }

        var picker = new ShellScopePicker(registry, vm.RecentScopes) { Owner = this };
        if (picker.ShowDialog() != true) {
            return;
        }

        vm.TrackScopes(picker.SelectedScopes);
        vm.SetShellHandlers(registry.Scan(vm.ScannedScopes));
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

        bool accepted = ServiceLocator.Get<IDialogs>().Ask(new DialogRequest(
            DialogKind.SettingsReset, Strings.SettingsResetGroup, Strings.SettingsResetConfirm,
            DialogButtons.OkCancel, DialogIcon.Warning));

        if (accepted) {
            vm.ApplyFrom(new AppSettings());
            // The chosen programs went with the rest.
            _ = LocateToolsAsync(vm, rescan: false);
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

        ServiceLocator.Get<IIconProvider>().ClearCache();
        // Decoded copies live a tier above the provider's; leaving them
        // would make the button look like it did nothing.
        Controls.IconImageCache.Clear();
        _ = RefreshCacheStatusAsync(vm);
    }


    /// <summary>
    /// Reads the cache folder's real size - a listing of a few thousand
    /// files, so it runs on the pool rather than while the dialog is
    /// opening. Only ever done on opening and right after a clear.
    /// </summary>
    private static async Task RefreshCacheStatusAsync(SettingsViewModel vm) {
        var provider = ServiceLocator.Get<IIconProvider>();
        var (directory, size) = await Task.Run(provider.DescribeCache);
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


    // --- Actions table ----------------------------------------------------

    /// <summary>
    /// A click into a cell's text box does not select the row - the box
    /// takes the mouse first - and the buttons under the table act on the
    /// selected one. Following the keyboard focus keeps the two the same.
    /// </summary>
    private void ActionsGrid_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
        if (sender is DataGrid grid
            && e.NewFocus is DependencyObject focused
            && ItemsControl.ContainerFromElement(grid, focused) is DataGridRow row) {
            grid.SelectedItem = row.Item;
        }
    }

    private void AddAction_Click(object sender, RoutedEventArgs e) {
        ActionsPage(sender)?.Add();
    }

    private void CopyAction_Click(object sender, RoutedEventArgs e) {
        ActionsPage(sender)?.CopySelected();
    }

    private void RemoveAction_Click(object sender, RoutedEventArgs e) {
        ActionsPage(sender)?.RemoveSelected();
    }


    private static ActionsSettingsCategory? ActionsPage(object sender) {
        return (sender as FrameworkElement)?.DataContext as ActionsSettingsCategory;
    }


    // --- Programs page ----------------------------------------------------

    /// <summary>"Указать…": the program's file, chosen by hand; it wins over what was found.</summary>
    private void PointTool_Click(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { DataContext: ToolRowViewModel row } || DataContext is not SettingsViewModel vm) {
            return;
        }

        var picker = new Microsoft.Win32.OpenFileDialog {
            Title = string.Format(Strings.ToolsPointAtTitle, row.Title),
            Filter = Strings.ToolsProgramFilter,
            FileName = row.Tool + ".exe",
            CheckFileExists = true,
        };
        if (row.PathText.Length > 0 && Path.GetDirectoryName(row.PathText) is { } folder && Directory.Exists(folder)) {
            picker.InitialDirectory = folder;
        }
        if (picker.ShowDialog(this) != true) {
            return;
        }

        vm.SetToolPath(row.Tool, picker.FileName);
        _ = LocateToolsAsync(vm, rescan: false);
    }

    /// <summary>"Сбросить": forget the chosen file and look for the program again.</summary>
    private void ResetTool_Click(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { DataContext: ToolRowViewModel row } || DataContext is not SettingsViewModel vm) {
            return;
        }

        vm.SetToolPath(row.Tool, null);
        _ = LocateToolsAsync(vm, rescan: false);
    }
}
