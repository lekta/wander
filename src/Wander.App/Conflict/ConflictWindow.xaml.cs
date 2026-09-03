using System.Windows;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Layout;
using Wander.Core.Logging;
using Wander.Core.Persistence;

namespace Wander.App.Conflict;

/// <summary>
/// The one window a batch asks its questions in. Built once per
/// <see cref="IConflictResolver.ResolveAll"/> call, modal to the main
/// window, and it answers for the whole list at once - nothing has been
/// copied or moved by the time it opens, so Cancel here really does cost
/// nothing.
/// </summary>
public partial class ConflictWindow : Window {
    private readonly ConflictWindowViewModel _vm;


    private ConflictWindow(ConflictWindowViewModel vm) {
        InitializeComponent();

        _vm = vm;
        DataContext = vm;

        RestoreGeometry();
        // After the geometry: parking wins, or a headless run would put the
        // window back on the screen a person is working at.
        App.ParkIfHeadless(this);

        Loaded += (_, _) => vm.Start();
        Closing += (_, _) => {
            vm.Stop();
            SaveGeometry();
        };
    }


    /// <summary>
    /// One answer per pair shown - the ones asked about and the ones found
    /// inside merged folders - in the order they were listed; null on Cancel.
    /// </summary>
    public IReadOnlyList<ConflictAnswer>? Result { get; private set; }


    public static IReadOnlyList<ConflictAnswer>? Show(ConflictRequest request, bool skipIdentical) {
        var vm = new ConflictWindowViewModel(
            new ConflictBatch(request, skipIdentical),
            ServiceLocator.Get<IFileSystem>(),
            ServiceLocator.Get<ILogger>());

        var window = new ConflictWindow(vm) {
            Owner = Application.Current?.MainWindow,
        };
        window.ShowDialog();

        return window.Result;
    }


    private void OnOk(object sender, RoutedEventArgs e) {
        Result = _vm.Batch.Answers();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) {
        Result = null;
        DialogResult = false;
    }

    private void RestoreGeometry() {
        var geometry = ServiceLocator.Get<IAppStateStore>().Load().ConflictWindow;
        if (geometry is not { } saved || !WindowPlacement.IsUsableSize(saved.Width, saved.Height)) {
            return;
        }

        Width = saved.Width;
        Height = saved.Height;

        var screen = new ScreenRect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        (Left, Top) = WindowPlacement.Clamp(new ScreenRect(saved.Left, saved.Top, Width, Height), screen);
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    private void SaveGeometry() {
        // A parked window sits at -32000: remembering that would lose the
        // window for the next real session.
        if (App.Headless || WindowState != WindowState.Normal) {
            return;
        }

        var store = ServiceLocator.Get<IAppStateStore>();
        store.Save(store.Load() with {
            ConflictWindow = new WindowGeometry {
                Left = Left,
                Top = Top,
                Width = Width,
                Height = Height,
            },
        });
    }
}
