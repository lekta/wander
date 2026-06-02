using System.Windows;
using Wander.App.ViewModels;
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
        }
    }


    private void Ok_Click(object sender, RoutedEventArgs e) {
        CommitBaseline();
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e) {
        CommitBaseline();
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
