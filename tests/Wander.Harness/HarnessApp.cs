using System.Windows;
using System.Windows.Threading;
using Wander.App;
using Wander.App.Dialogs;
using Wander.Core;
using Wander.Core.Logging;
using Wander.Harness.Host;

namespace Wander.Harness;

/// <summary>
/// The real <see cref="Wander.App.App"/> with two things swapped in after
/// its own startup ran: a logger that keeps every line in memory (the
/// runner waits on log lines and asserts against them) and a dialog service
/// that answers by policy. The window is then created the way StartupUri
/// would create it - off-screen, because <see cref="Wander.App.App.Headless"/>
/// is set before the app is constructed - and the scenario starts once the
/// dispatcher goes idle for the first time.
/// </summary>
/// <remarks>
/// <c>InitializeComponent</c> is not called: it looks for App.xaml's BAML in
/// the assembly of the concrete type, which is this one. The two things it
/// would have done - merge the resource dictionaries and set StartupUri -
/// are done by hand here instead.
/// </remarks>
public sealed class HarnessApp : Wander.App.App {
    private readonly RunContext _context;
    private readonly ScriptedDialogs _dialogs = new();
    private CapturingLogger _log = null!;


    public HarnessApp(RunContext context) {
        _context = context;
        Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("/Wander;component/Resources/Palette.xaml", UriKind.Relative),
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("/Wander;component/Resources/MenuStyles.xaml", UriKind.Relative),
        });
    }


    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        _log = new CapturingLogger(ServiceLocator.Get<ILogger>(), ServiceLocator.Get<ILogFile>());
        ServiceLocator.Register<ILogger>(_log);
        ServiceLocator.Register<ILogFile>(_log);
        ServiceLocator.Register<IDialogs>(_dialogs);

        // Checked, not assumed. The window is parked off-screen and refuses
        // activation only while this flag is on, and it is read once, in
        // MainWindow's constructor - anything that clears it between the
        // Program setting it and this line puts a live file manager on the
        // desktop of whoever is working there, and takes their keyboard.
        // That is the one failure this harness must never have, so it is a
        // refusal rather than a warning.
        if (!Wander.App.App.Headless) {
            _log.Error("HARNESS refusing to run: App.Headless is off, the window would open on the real desktop");
            Shutdown(70);

            return;
        }

        var window = new MainWindow();
        window.Show();
        _log.Info($"HARNESS window at ({window.Left:F0}, {window.Top:F0}), taskbar={window.ShowInTaskbar}, active={window.IsActive}");
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => _ = RunAsync()));
    }


    private async Task RunAsync() {
        var report = new RunReport(_context);
        int code;
        try {
            var window = (MainWindow)MainWindow!;
            var vm = (MainViewModel)window.DataContext;
            var runner = new ScenarioRunner(_context, window, vm, _log, _dialogs, report);
            code = await runner.RunAsync();
        } catch (Exception ex) {
            report.Fatal(ex);
            code = 70;
        }

        report.Write(_log, _dialogs);
        Shutdown(code);
    }
}
