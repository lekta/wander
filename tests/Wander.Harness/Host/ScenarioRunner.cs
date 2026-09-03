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
    private readonly Dictionary<string, FileStream> _locked = new(StringComparer.OrdinalIgnoreCase);
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
        foreach (var held in _locked.Values) {
            held.Dispose();
        }
        _locked.Clear();
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
            case "preview-format":
                await PreviewFormatAsync(step);
                break;
            case "preview": {
                    bool on = step.Bool("on") ?? true;
                    if (_vm.IsPreviewVisible != on) {
                        _vm.TogglePreviewCommand.Execute(null);
                    }
                    await WaitIdleAsync(step);
                    break;
                }
            case "tree-expand":
                await TreeExpandAsync(step);
                break;
            case "bookmark":
                Bookmark(step);
                await WaitIdleAsync(step);
                break;
            case "search":
                await SearchAsync(step);
                break;
            case "soak":
                await SoakAsync(step);
                break;
            case "assert-log":
                AssertLog(step, logStart);
                break;
            case "assert-entries":
                AssertEntries(step);
                break;
            case "assert-path":
                AssertPath(step);
                break;
            case "assert-pane":
                AssertPane(step);
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
                Invoke(name, _vm.DeleteCommand);
                break;
            case "permanent-delete":
                EnsureInSandbox(name);
                Invoke(name, _vm.PermanentDeleteCommand);
                break;
            case "copy":
                Invoke(name, _vm.CopyCommand);
                break;
            case "cut":
                Invoke(name, _vm.CutCommand);
                break;
            case "paste":
                EnsureInSandbox(name);
                Invoke(name, _vm.PasteCommand);
                break;
            case "new-folder":
                EnsureInSandbox(name);
                Invoke(name, _vm.NewFolderCommand);
                break;
            case "refresh":
                Invoke(name, _vm.RefreshCommand);
                break;
            case "undo":
                EnsureInSandbox(name);
                Invoke(name, _vm.UndoCommand);
                break;
            case "up":
                Invoke(name, _vm.UpCommand);
                break;
            case "back":
                Invoke(name, _vm.BackCommand);
                break;
            case "forward":
                Invoke(name, _vm.ForwardCommand);
                break;
            case "clear-search":
                Invoke(name, _vm.ClearSearchCommand);
                break;
            default:
                throw new InvalidDataException($"unknown command '{name}'");
        }
    }

    /// <summary>
    /// Runs a command the way the window does - through its CanExecute. A
    /// disabled menu row and a hotkey in a place that refuses it both do
    /// nothing, and a scenario asking for one has to see that same nothing
    /// rather than the guts of a command the user could never have fired.
    /// The refusal is written into the report: silence would look like the
    /// command had run.
    /// </summary>
    private void Invoke(string name, RelayCommand command) {
        if (!command.CanExecute(null)) {
            _report.Note($"command '{name}' is not available here - not run");
            _log.Info($"HARNESS command '{name}' unavailable, skipped");

            return;
        }

        command.Execute(null);
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

    /// <summary>
    /// The control the current view mode is showing. Rows are counted and
    /// selected through it rather than through the view model, because the
    /// two do not hold the same thing: the name filter is a view filter, so
    /// <c>Entries</c> keeps the whole folder while the control shows what
    /// is left of it.
    /// </summary>
    private Selector ActiveList() {
        var list = _window.FileList;

        return _vm.ViewMode switch {
            ViewMode.Details => list.DetailsView,
            ViewMode.Tiles => list.TilesView,
            ViewMode.LargeIcons => list.IconsView,
            _ => list.GalleryView,
        };
    }

    private void SelectEntries(string[] names) {
        var entries = names.Select(Entry).ToList();
        var list = _window.FileList;
        var active = ActiveList();
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
            bool handled = Raise(Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent);
            Raise(Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent);
            _log.Info($"HARNESS key {key} on {target.GetType().Name}: handled={handled}");
            if (!handled) {
                InvokeKeyBinding(key);
            }
        }

        bool Raise(RoutedEvent preview, RoutedEvent main) {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = preview };
            target.RaiseEvent(args);
            if (args.Handled) {
                return true;
            }

            var bubble = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = main };
            target.RaiseEvent(bubble);

            return bubble.Handled;
        }
    }

    /// <summary>
    /// The window's own <c>InputBindings</c>, replayed by hand. WPF matches
    /// those inside the input manager, off a real keystroke; a routed event
    /// raised on an element bubbles past them, so Enter, F5, Ctrl+C and the
    /// rest of the hotkeys would do nothing in a scenario that pressed
    /// them. Runs only when nobody handled the key, which is exactly the
    /// point at which WPF would have reached the bindings.
    /// </summary>
    private void InvokeKeyBinding(Key key) {
        var modifiers = Keyboard.Modifiers;
        foreach (var binding in _window.InputBindings.OfType<KeyBinding>()) {
            if (binding.Key != key || binding.Modifiers != modifiers || binding.Command is not { } command) {
                continue;
            }

            if (command.CanExecute(binding.CommandParameter)) {
                command.Execute(binding.CommandParameter);
            } else {
                _report.Note($"hotkey '{key}' is not available here - not run");
            }

            return;
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
            // Missing is not an error: this is the step a scenario opens
            // with to clear what a previous run left when it died halfway,
            // and it has to work on a clean sandbox too.
            case "delete":
                if (Directory.Exists(path)) {
                    Directory.Delete(path, recursive: true);
                } else if (File.Exists(path)) {
                    File.Delete(path);
                }
                break;
            // "The file is in use" needs somebody using it. Held here
            // rather than by the sandbox builder: a run against a sandbox
            // it did not rebuild would find the handle long gone, and the
            // check would quietly pass by testing nothing.
            case "lock":
                _locked[path] = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                break;
            case "unlock":
                if (_locked.Remove(path, out var held)) {
                    held.Dispose();
                }
                break;
            default:
                throw new InvalidDataException($"unknown fs op '{step.Str("op")}'");
        }
    }


    // --- Panels, search, soak ------------------------------------------

    /// <summary>
    /// Opens one of the two panels down to a folder, a level at a time.
    /// Level by level rather than in one call because a branch reads its
    /// children off the disk when it opens: asking for a path six levels
    /// down before any of them has loaded finds nothing, which is why the
    /// controller's own <c>ExpandTo</c> is not what a scenario wants.
    /// </summary>
    private async Task TreeExpandAsync(JsonElement step) {
        string path = Normalise(_context.Expand(step.Require("path")));
        bool bookmarks = string.Equals(step.Str("panel"), "bookmark", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<TreeNodeViewModel> level = bookmarks
            ? _vm.Bookmarks.Items.ToList()
            : _vm.Trees.Roots.ToList();

        TreeNodeViewModel? node = null;
        while (true) {
            var next = level.FirstOrDefault(n => IsUnderOrEqual(path, n.FullPath));
            if (next is null) {
                throw new InvalidOperationException(
                    $"'{path}' is not reachable in the {(bookmarks ? "bookmarks" : "drives")} panel " +
                    $"(rows: {string.Join(", ", level.Select(n => n.Name))})");
            }

            node = next;
            if (Normalise(node.FullPath) == path) {
                break;
            }

            node.IsExpanded = true;
            await WaitIdleAsync(step);
            level = node.Children.ToList();
        }

        // The row itself is expanded too unless the scenario only wanted it
        // brought into view - "expanded" in this panel means "its children
        // are visible", which is what a chevron click does.
        if (step.Bool("expand") != false) {
            node.IsExpanded = true;
            await WaitIdleAsync(step);
        }
        _vm.Trees.Select(node);
    }

    /// <summary>
    /// Selects the file with this extension, if the folder has one, and
    /// shoots it. Optional by design: the formats that need a real encoder
    /// are copied in from <c>tests\Fixtures</c> by extension, and a machine
    /// where nobody has supplied one still has to run the scenario - the
    /// hole is a line in the report rather than a failed step.
    ///
    /// <para>
    /// First match by name, so this is for extensions the generators do not
    /// write: ask for .pdf in the docs folder and you get the generated
    /// manual.pdf, which has a step of its own already.
    /// </para>
    /// </summary>
    private async Task PreviewFormatAsync(JsonElement step) {
        string extension = step.Require("ext");
        var entry = _vm.Entries
            .Where(e => e.Kind == EntryKind.File
                && string.Equals(Path.GetExtension(e.Name), extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (entry is null) {
            _report.Note($"no {extension} in {_vm.CurrentPath}: no fixture for it, that branch of the pane was not opened");
            _log.Info($"HARNESS no {extension} here, step skipped");

            return;
        }

        SelectEntries(new[] { entry.Name });
        await WaitIdleAsync(step);
        SaveScreenshot(step.Str("name") ?? extension.TrimStart('.'));
    }

    private void Bookmark(JsonElement step) {
        string path = _context.Expand(step.Require("path"));
        switch (step.Require("op").ToLowerInvariant()) {
            case "add":
                _vm.Bookmarks.Add(path);
                break;
            case "remove":
                _vm.Bookmarks.Remove(path);
                break;
            default:
                throw new InvalidDataException($"unknown bookmark op '{step.Str("op")}'");
        }
    }

    /// <summary>
    /// Sets up a search the way the search window does and runs it, then
    /// waits for the pass to finish rather than for the log to go quiet: a
    /// deep search over a big folder is minutes of work that says nothing
    /// while it runs.
    /// </summary>
    private async Task SearchAsync(JsonElement step) {
        var search = _vm.ContentSearch;
        search.NameQuery = step.Str("name") ?? "";
        search.TextQuery = step.Str("text") ?? "";
        search.SearchSubfolders = step.Bool("subfolders") ?? false;
        search.SearchBinaries = step.Bool("binaries") ?? false;
        search.RunNow();

        var clock = Stopwatch.StartNew();
        int timeoutMs = step.Int("searchTimeoutMs", 60_000);
        while (search.IsRunning) {
            if (clock.ElapsedMilliseconds > timeoutMs) {
                throw new TimeoutException($"search still running after {timeoutMs} ms");
            }

            await YieldAsync();
            await Task.Delay(50);
        }

        _log.Info($"HARNESS search finished in {clock.ElapsedMilliseconds} ms, {_vm.Entries.Count} rows");
        await WaitIdleAsync(step);
    }

    /// <summary>
    /// Walks the sandbox at random for a while, taking a sample a minute.
    /// What it is looking for is not a crash but a shape: memory and
    /// handles that keep climbing for as long as it runs. The limits below
    /// are measured from the first sample, once the caches have filled -
    /// growth in the first minute is the app warming up, growth in the
    /// tenth is a leak.
    /// </summary>
    private async Task SoakAsync(JsonElement step) {
        var folders = Directory
            .EnumerateDirectories(_context.SandboxRoot, "*", SearchOption.TopDirectoryOnly)
            .SelectMany(d => new[] { d }.Concat(SafeChildren(d)))
            .ToList();
        if (folders.Count == 0) {
            throw new InvalidOperationException($"nothing to walk in {_context.SandboxRoot}");
        }

        var modes = new[] { ViewMode.Details, ViewMode.Tiles, ViewMode.LargeIcons, ViewMode.Gallery };
        var random = new Random(step.Int("seed", 20260902));
        var clock = Stopwatch.StartNew();
        long minutes = Math.Max(1, step.Int("minutes", 5));
        long lastSample = 0;
        var samples = new List<Sample> { _metrics.Take("soak-0") };

        while (clock.ElapsedMilliseconds < minutes * 60_000) {
            string folder = folders[random.Next(folders.Count)];
            _awaitedFolder = folder.TrimEnd('\\', '/');
            _firstScreenSeen = false;
            _vm.NavigateTo(folder, NavigationSource.External);
            await WaitIdleAsync(step);

            if (random.Next(4) == 0) {
                _vm.ViewMode = modes[random.Next(modes.Length)];
                await WaitIdleAsync(step);
            }
            if (random.Next(8) == 0 && _vm.Entries.Count > 0) {
                SelectEntries(new[] { _vm.Entries[random.Next(_vm.Entries.Count)].Name });
                await YieldAsync();
            }

            if (clock.ElapsedMilliseconds - lastSample >= 60_000) {
                lastSample = clock.ElapsedMilliseconds;
                samples.Add(_metrics.Take($"soak-{samples.Count}"));
            }
        }

        samples.Add(_metrics.Take($"soak-{samples.Count}"));

        // The baseline is the end of the first minute, not the start of the
        // run: the first minute is the thumbnail caches filling, and every
        // soak would fail on that alone. A run too short to have one falls
        // back to the start and is measuring warm-up - which is why the
        // limits in soak.json come with a length.
        int baseIndex = samples.Count > 2 ? 1 : 0;
        var first = samples[baseIndex];
        var last = samples[^1];
        long grewMb = (last.WorkingSet - first.WorkingSet) / (1024 * 1024);
        int grewHandles = last.Handles - first.Handles;
        _report.Note(
            $"soak {minutes} min over {folders.Count} folders, measured from soak-{baseIndex}: " +
            $"working set {first.WorkingSet / (1024 * 1024)} -> {last.WorkingSet / (1024 * 1024)} MB (+{grewMb}), " +
            $"handles {first.Handles} -> {last.Handles} (+{grewHandles}), " +
            $"LOH {first.LohBytes / (1024 * 1024)} -> {last.LohBytes / (1024 * 1024)} MB, " +
            $"gen2 {last.Gen2 - first.Gen2}");

        int maxMb = step.Int("maxWorkingSetGrowthMb", 150);
        int maxHandles = step.Int("maxHandleGrowth", 500);
        if (grewMb > maxMb || grewHandles > maxHandles) {
            throw new InvalidOperationException(
                $"no plateau: working set +{grewMb} MB (limit {maxMb}), handles +{grewHandles} (limit {maxHandles})");
        }
    }

    private static IEnumerable<string> SafeChildren(string dir) {
        try {
            return Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly).Take(20).ToList();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return Array.Empty<string>();
        }
    }

    private static string Normalise(string path) {
        return path.TrimEnd('\\', '/').ToUpperInvariant();
    }

    private static bool IsUnderOrEqual(string path, string root) {
        if (string.IsNullOrEmpty(root)) {
            return false;
        }

        string normalised = Normalise(root);

        return path == normalised || path.StartsWith(normalised + "\\", StringComparison.Ordinal);
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
            // A scenario that provokes a failure on purpose - a locked
            // file, a permanent delete - names it in "allow". Naming it is
            // the point: an expected error stays asserted rather than
            // becoming a level the check stops looking at.
            var allowed = step.Strings("allow");
            var bad = lines.FirstOrDefault(l =>
                l.Level != "INFO"
                && !l.Message.StartsWith("HARNESS", StringComparison.Ordinal)
                && !allowed.Any(a => l.Message.Contains(a, StringComparison.OrdinalIgnoreCase)));
            if (bad is not null) {
                throw new InvalidOperationException($"log has {bad.Level}: {bad.Message}");
            }
        }
    }

    /// <summary>
    /// Rows, either as the view model holds them or as the list shows them
    /// (<c>"scope": "visible"</c>). The distinction is not academic: a name
    /// mask filters the view, not <c>Entries</c>, so counting the view model
    /// after a name search asserts nothing at all - "at least a thousand of
    /// them" was true of the unfiltered folder before the search ran.
    /// </summary>
    private void AssertEntries(JsonElement step) {
        bool visible = string.Equals(step.Str("scope"), "visible", StringComparison.OrdinalIgnoreCase);
        var names = visible
            ? ActiveList().Items.OfType<FileSystemEntry>().Select(e => e.Name).ToList()
            : _vm.Entries.Select(e => e.Name).ToList();
        int count = names.Count;
        string what = visible ? "visible rows" : "entries";
        if (step.TryGetProperty("count", out var exact) && exact.GetInt32() != count) {
            throw new InvalidOperationException($"expected {exact.GetInt32()} {what}, found {count}");
        }
        if (step.TryGetProperty("min", out var min) && count < min.GetInt32()) {
            throw new InvalidOperationException($"expected at least {min.GetInt32()} {what}, found {count}");
        }
        if (step.TryGetProperty("max", out var max) && count > max.GetInt32()) {
            throw new InvalidOperationException($"expected at most {max.GetInt32()} {what}, found {count}");
        }
        foreach (string name in step.Strings("contains")) {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"entry '{name}' missing in {_vm.CurrentPath}" + (visible ? " (visible rows)" : ""));
            }
        }
        foreach (string name in step.Strings("absent")) {
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"entry '{name}' still present in {_vm.CurrentPath}" + (visible ? " (visible rows)" : ""));
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


    /// <summary>
    /// Where the window says it is: the path itself, and the tail of the
    /// breadcrumb strip. The crumbs are asserted separately because they
    /// are computed, not copied - a path inside an archive has to cut into
    /// clickable segments like any other, and a namespace that answered
    /// with a display name would collapse the whole strip into one row.
    /// </summary>
    private void AssertPath(JsonElement step) {
        if (step.Str("is") is { } expected) {
            string actual = _vm.CurrentPath ?? "";
            if (!string.Equals(_context.Expand(expected).TrimEnd('\\', '/'), actual.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"expected to be at '{expected}', am at '{actual}'");
            }
        }

        var crumbs = step.Strings("crumbs");
        if (crumbs.Length > 0) {
            var labels = _vm.Nav.Breadcrumbs.Select(c => c.Label).ToList();
            var tail = labels.Skip(Math.Max(0, labels.Count - crumbs.Length)).ToList();
            if (!tail.SequenceEqual(crumbs, StringComparer.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"breadcrumbs end with [{string.Join(" > ", tail)}], expected [{string.Join(" > ", crumbs)}]");
            }
        }
    }


    /// <summary>
    /// The preview pane against the window it lives in. The three numbers
    /// go into the report either way: what this is about is a state.json
    /// written on a monitor and opened on a laptop, where a pane that keeps
    /// its pixel width leaves the file list a sliver.
    /// </summary>
    private void AssertPane(JsonElement step) {
        double window = _window.ActualWidth;
        double pane = _vm.IsPreviewVisible ? _vm.PreviewWidth : 0;
        double list = window - pane;
        _report.Note($"window {window:F0} px, preview pane {pane:F0} px, list about {list:F0} px");

        int minList = step.Int("minList", 0);
        if (minList > 0 && list < minList) {
            throw new InvalidOperationException(
                $"the file list has about {list:F0} px of a {window:F0} px window, less than {minList}");
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
        if (string.IsNullOrEmpty(path)) {
            return false;
        }

        string root = Path.GetFullPath(_context.SandboxRoot).TrimEnd('\\', '/');
        string full = Path.GetFullPath(path).TrimEnd('\\', '/');

        // The separator matters: a bare prefix would let a delete run in
        // "...\wander-sandbox\formats-old" while the guard believed it was
        // inside "...\wander-sandbox\formats".
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
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
