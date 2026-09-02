using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Wander.App;
using Wander.App.Dialogs;
using Wander.App.ViewModels;
using Wander.Core.FileSystem;
using Wander.Core.Navigation;

namespace Wander.Harness.Host;

/// <summary>
/// Executes scenario steps on the UI thread against the live window and
/// view model. Every verb is one switch arm in <see cref="ExecuteAsync"/>;
/// the conventions that keep a scenario honest live here too:
/// <list type="bullet">
///   <item>steps that change files refuse to run unless the current folder
///   is inside the sandbox (<see cref="EnsureInSandbox"/>);</item>
///   <item>"idle" means the listing is not loading, the first-screen line
///   for the folder we navigated to has arrived (or five seconds passed),
///   and nothing has been logged for a while - the same signals a person
///   would wait for;</item>
///   <item>a failed step gets a screenshot before the run stops.</item>
/// </list>
/// </summary>
public sealed class ScenarioRunner {
    private const int FirstScreenWaitMs = 5000;

    private readonly RunContext _context;
    private readonly MainWindow _window;
    private readonly MainViewModel _vm;
    private readonly CapturingLogger _log;
    private readonly ScriptedDialogs _dialogs;
    private readonly RunReport _report;
    private readonly MetricsSampler _metrics = new();
    private int _shots;
    private string? _awaitedFolder;
    private volatile bool _firstScreenSeen;


    public ScenarioRunner(
        RunContext context, MainWindow window, MainViewModel vm,
        CapturingLogger log, ScriptedDialogs dialogs, RunReport report) {
        _context = context;
        _window = window;
        _vm = vm;
        _log = log;
        _dialogs = dialogs;
        _report = report;
        _log.Logged += OnLogged;
    }


    public async Task<int> RunAsync() {
        var scenario = _context.Scenario;
        bool failed = false;
        _metrics.Take("start");
        _log.Info($"HARNESS scenario '{scenario.Name}' starts: {scenario.Steps.Count} steps, sandbox {_context.SandboxRoot}");

        for (int i = 0; i < scenario.Steps.Count; i++) {
            var step = scenario.Steps[i];
            string verb = step.Str("do") ?? "?";
            var clock = Stopwatch.StartNew();
            int logStart = _log.Count;
            _log.Info($"HARNESS step {i + 1}: {Describe(step)}");
            try {
                await WithTimeout(ExecuteAsync(step, logStart), step.Int("timeoutMs", scenario.StepTimeoutMs), verb);
                _report.Step(i + 1, Describe(step), "ok", clock.ElapsedMilliseconds);
            } catch (Exception ex) {
                failed = true;
                string? shot = TrySaveScreenshot($"fail-{i + 1}");
                string detail = $"{ex.GetType().Name}: {ex.Message}" + (shot is null ? "" : $" ({Path.GetFileName(shot)})");
                _report.Step(i + 1, Describe(step), "FAIL", clock.ElapsedMilliseconds, detail);
                _log.Error($"HARNESS step {i + 1} '{verb}' failed", ex);
                if (scenario.StopOnFailure) {
                    break;
                }
            }
        }

        _metrics.Take("end");
        _metrics.WriteJson(Path.Combine(_context.OutDir, "metrics.json"));
        _report.Metrics = _metrics.Summary();
        _metrics.Dispose();
        _log.Logged -= OnLogged;

        return failed ? 2 : 0;
    }


    // --- Steps ---------------------------------------------------------

    private async Task ExecuteAsync(JsonElement step, int logStart) {
        string verb = (step.Str("do") ?? "").ToLowerInvariant();
        switch (verb) {
            case "navigate": {
                    string path = _context.Expand(step.Require("path"));
                    _awaitedFolder = path.TrimEnd('\\', '/');
                    _firstScreenSeen = false;
                    _vm.NavigateTo(path, NavigationSource.External);
                    if (step.Bool("wait") != false) {
                        await WaitIdleAsync(step);
                    }
                    break;
                }
            case "wait-idle":
                await WaitIdleAsync(step);
                break;
            case "screenshot":
                await YieldAsync();
                SaveScreenshot(step.Str("name") ?? "shot");
                break;
            case "view":
                _vm.ViewMode = Enum.Parse<ViewMode>(step.Require("mode"), ignoreCase: true);
                await WaitIdleAsync(step);
                break;
            case "select":
                SelectEntries(step.Strings("names"));
                await YieldAsync();
                break;
            case "command":
                RunCommand(step.Require("name"));
                await WaitIdleAsync(step);
                break;
            case "rename": {
                    var entry = Entry(step.Require("name"));
                    if (!string.Equals(_vm.RenamingPath, entry.FullPath, StringComparison.OrdinalIgnoreCase)) {
                        _vm.BeginRename(entry);
                        await YieldAsync();
                    }
                    _vm.CommitRename(step.Require("to"));
                    await WaitIdleAsync(step);
                    break;
                }
            case "key":
                RaiseKey(step.Require("key"), step.Int("repeat", 1));
                await YieldAsync();
                break;
            case "settings":
                SetSetting(step.Require("name"), step.Require("value"));
                await WaitIdleAsync(step);
                break;
            case "dialogs":
                ApplyDialogPolicy(step);
                break;
            case "fs":
                FileSystemOp(step);
                break;
            case "preview": {
                    bool on = step.Bool("on") ?? true;
                    if (_vm.IsPreviewVisible != on) {
                        _vm.TogglePreviewCommand.Execute(null);
                    }
                    await WaitIdleAsync(step);
                    break;
                }
            case "assert-log":
                AssertLog(step, logStart);
                break;
            case "assert-entries":
                AssertEntries(step);
                break;
            case "measure":
                _metrics.Take(step.Str("name") ?? "measure");
                break;
            case "sleep":
                await Task.Delay(step.Int("ms", 500));
                break;
            case "note":
                _report.Note(step.Require("text"));
                break;
            default:
                throw new InvalidDataException($"unknown step '{verb}'");
        }
    }

    private void RunCommand(string name) {
        switch (name.ToLowerInvariant()) {
            case "delete":
                EnsureInSandbox(name);
                _vm.DeleteCommand.Execute(null);
                break;
            case "permanent-delete":
                EnsureInSandbox(name);
                _vm.PermanentDeleteCommand.Execute(null);
                break;
            case "copy":
                _vm.CopyCommand.Execute(null);
                break;
            case "cut":
                _vm.CutCommand.Execute(null);
                break;
            case "paste":
                EnsureInSandbox(name);
                _vm.PasteCommand.Execute(null);
                break;
            case "new-folder":
                EnsureInSandbox(name);
                _vm.NewFolderCommand.Execute(null);
                break;
            case "refresh":
                _vm.RefreshCommand.Execute(null);
                break;
            case "undo":
                EnsureInSandbox(name);
                _vm.UndoCommand.Execute(null);
                break;
            case "up":
                _vm.UpCommand.Execute(null);
                break;
            case "back":
                _vm.BackCommand.Execute(null);
                break;
            case "forward":
                _vm.ForwardCommand.Execute(null);
                break;
            case "clear-search":
                _vm.ClearSearchCommand.Execute(null);
                break;
            default:
                throw new InvalidDataException($"unknown command '{name}'");
        }
    }


    // --- Waiting -------------------------------------------------------

    private async Task WaitIdleAsync(JsonElement step) {
        int quietMs = step.Int("quietMs", 400);
        int timeoutMs = step.Int("idleTimeoutMs", 15_000);
        var clock = Stopwatch.StartNew();

        while (true) {
            await YieldAsync();
            bool waitingFirstScreen = _awaitedFolder is not null && !_firstScreenSeen && clock.ElapsedMilliseconds < FirstScreenWaitMs;
            if (!_vm.IsListLoading && !waitingFirstScreen && _log.MillisecondsSinceLastLine >= quietMs) {
                _awaitedFolder = null;

                return;
            }
            if (clock.ElapsedMilliseconds > timeoutMs) {
                throw new TimeoutException(
                    $"not idle after {timeoutMs} ms (listLoading={_vm.IsListLoading}, quiet for {_log.MillisecondsSinceLastLine} ms)");
            }

            await Task.Delay(50);
        }
    }

    private static Task YieldAsync() {
        return Dispatcher.Yield(DispatcherPriority.ContextIdle).GetAwaiter().IsCompleted
            ? Task.CompletedTask
            : YieldSlow();

        static async Task YieldSlow() {
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        }
    }

    private static async Task WithTimeout(Task work, int timeoutMs, string verb) {
        var done = await Task.WhenAny(work, Task.Delay(timeoutMs));
        if (done != work) {
            throw new TimeoutException($"step '{verb}' did not finish in {timeoutMs} ms");
        }

        await work;
    }

    private void OnLogged(LogLine line) {
        string? folder = _awaitedFolder;
        if (folder is null || !line.Message.StartsWith("First screen", StringComparison.Ordinal)) {
            return;
        }
        if (line.Message.TrimEnd('\\', '/').EndsWith(folder, StringComparison.OrdinalIgnoreCase)) {
            _firstScreenSeen = true;
        }
    }


    // --- Selection, keys, settings ------------------------------------

    private FileSystemEntry Entry(string name) {
        return _vm.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"no entry named '{name}' in {_vm.CurrentPath}");
    }

    private void SelectEntries(string[] names) {
        var entries = names.Select(Entry).ToList();
        var list = _window.FileList;
        Selector active = _vm.ViewMode switch {
            ViewMode.Details => list.DetailsView,
            ViewMode.Tiles => list.TilesView,
            ViewMode.LargeIcons => list.IconsView,
            _ => list.GalleryView,
        };
        // Through the control, not the view model: the list's own
        // SelectionChanged is what keeps SelectedEntries and the preview in
        // step, and that is the path a click takes.
        var items = active switch {
            ListBox lb => lb.SelectedItems,
            DataGrid dg => dg.SelectedItems,
            _ => throw new InvalidOperationException("unknown list control"),
        };
        items.Clear();
        foreach (var entry in entries) {
            items.Add(entry);
        }
        list.FocusList();
    }

    private void RaiseKey(string keyName, int repeat) {
        var key = Enum.Parse<Key>(keyName, ignoreCase: true);
        var target = Keyboard.FocusedElement ?? _window.FileList;
        var source = PresentationSource.FromDependencyObject((DependencyObject)target)
            ?? PresentationSource.FromVisual(_window)
            ?? throw new InvalidOperationException("window has no presentation source");

        for (int i = 0; i < repeat; i++) {
            Raise(Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent);
            Raise(Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent);
        }

        void Raise(RoutedEvent preview, RoutedEvent main) {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = preview };
            target.RaiseEvent(args);
            if (!args.Handled) {
                var bubble = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = main };
                target.RaiseEvent(bubble);
            }
        }
    }

    private void SetSetting(string name, string value) {
        var property = _vm.Settings.GetType().GetProperty(name)
            ?? throw new InvalidDataException($"no setting '{name}'");
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object converted = type.IsEnum
            ? Enum.Parse(type, value, ignoreCase: true)
            : Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
        property.SetValue(_vm.Settings, converted);
    }

    private void ApplyDialogPolicy(JsonElement step) {
        if (step.Bool("default") is { } fallback) {
            _dialogs.DefaultAnswer = fallback;
        }
        if (step.Str("kind") is { } kind) {
            _dialogs.Answer(Enum.Parse<DialogKind>(kind, ignoreCase: true), step.Bool("accept") ?? true);
        }
        if (step.Str("conflict") is { } conflict) {
            _dialogs.Conflict = Enum.Parse<ConflictResolution>(conflict, ignoreCase: true);
        }
        if (step.TryGetProperty("prompt", out var prompt)) {
            _dialogs.PromptAnswer = prompt.ValueKind == JsonValueKind.String ? prompt.GetString() : null;
        }
        if (step.TryGetProperty("folder", out var folder)) {
            _dialogs.FolderAnswer = folder.ValueKind == JsonValueKind.String ? _context.Expand(folder.GetString()!) : null;
        }
    }

    private void FileSystemOp(JsonElement step) {
        string path = _context.Expand(step.Require("path"));
        if (!IsInSandbox(path)) {
            throw new InvalidOperationException($"fs step outside the sandbox: {path}");
        }

        switch (step.Require("op").ToLowerInvariant()) {
            case "create":
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, new byte[step.Int("bytes", 0)]);
                break;
            case "mkdir":
                Directory.CreateDirectory(path);
                break;
            case "append":
                File.AppendAllText(path, step.Str("text") ?? "x");
                break;
            case "delete":
                if (Directory.Exists(path)) {
                    Directory.Delete(path, recursive: true);
                } else {
                    File.Delete(path);
                }
                break;
            default:
                throw new InvalidDataException($"unknown fs op '{step.Str("op")}'");
        }
    }


    // --- Assertions ----------------------------------------------------

    private void AssertLog(JsonElement step, int logStart) {
        var lines = step.Str("scope") == "all" ? _log.All() : _log.Since(logStart);
        if (step.Str("contains") is { } text && !lines.Any(l => l.Message.Contains(text, StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException($"log has no line containing '{text}'");
        }
        if (step.Str("regex") is { } pattern && !lines.Any(l => Regex.IsMatch(l.Message, pattern))) {
            throw new InvalidOperationException($"log has no line matching /{pattern}/");
        }
        if (step.Str("absent") is { } forbidden) {
            var hit = lines.FirstOrDefault(l => l.Message.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) {
                throw new InvalidOperationException($"log contains '{forbidden}': {hit.Message}");
            }
        }
        if (step.Bool("noErrors") == true) {
            var bad = lines.FirstOrDefault(l => l.Level != "INFO" && !l.Message.StartsWith("HARNESS", StringComparison.Ordinal));
            if (bad is not null) {
                throw new InvalidOperationException($"log has {bad.Level}: {bad.Message}");
            }
        }
    }

    private void AssertEntries(JsonElement step) {
        var names = _vm.Entries.Select(e => e.Name).ToList();
        int count = names.Count;
        if (step.TryGetProperty("count", out var exact) && exact.GetInt32() != count) {
            throw new InvalidOperationException($"expected {exact.GetInt32()} entries, found {count}");
        }
        if (step.TryGetProperty("min", out var min) && count < min.GetInt32()) {
            throw new InvalidOperationException($"expected at least {min.GetInt32()} entries, found {count}");
        }
        if (step.TryGetProperty("max", out var max) && count > max.GetInt32()) {
            throw new InvalidOperationException($"expected at most {max.GetInt32()} entries, found {count}");
        }
        foreach (string name in step.Strings("contains")) {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"entry '{name}' missing in {_vm.CurrentPath}");
            }
        }
        foreach (string name in step.Strings("absent")) {
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"entry '{name}' still present in {_vm.CurrentPath}");
            }
        }
        var selected = step.Strings("selected");
        if (selected.Length > 0) {
            var actual = _vm.SelectedEntries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string name in selected) {
                if (!actual.Contains(name)) {
                    throw new InvalidOperationException($"'{name}' is not selected (selected: {string.Join(", ", actual)})");
                }
            }
        }
    }


    // --- Sandbox guard, screenshots -----------------------------------

    private void EnsureInSandbox(string command) {
        string current = _vm.CurrentPath ?? "";
        if (!IsInSandbox(current)) {
            throw new InvalidOperationException($"refusing '{command}' outside the sandbox: '{current}'");
        }
    }

    private bool IsInSandbox(string path) {
        string root = _context.SandboxRoot.TrimEnd('\\', '/') + "\\";

        return Path.GetFullPath(path).TrimEnd('\\', '/').StartsWith(root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private string SaveScreenshot(string name) {
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string path = Path.Combine(_context.ScreenshotsDir, $"{++_shots:00}-{safe}.png");
        Screenshot.Save(_window, path);
        _log.Info($"HARNESS screenshot {Path.GetFileName(path)}");

        return path;
    }

    private string? TrySaveScreenshot(string name) {
        try {
            return SaveScreenshot(name);
        } catch (Exception ex) {
            _log.Warn($"HARNESS screenshot failed: {ex.Message}");

            return null;
        }
    }

    private static string Describe(JsonElement step) {
        string verb = step.Str("do") ?? "?";
        string? arg = step.Str("path") ?? step.Str("name") ?? step.Str("mode") ?? step.Str("key") ?? step.Str("text");
        if (arg is null && step.TryGetProperty("names", out var names)) {
            arg = string.Join(",", names.EnumerateArray().Select(n => n.GetString()));
        }

        return arg is null ? verb : $"{verb} {arg}";
    }
}
