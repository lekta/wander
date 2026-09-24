using System.Text;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Undo;

namespace Wander.Core.Actions;

/// <summary>One selected item after the action ran over it.</summary>
/// <param name="Output">The declared output's path, when the action declares one.</param>
/// <param name="ExitCode">The program's exit code; 0 for a built-in action and for one that never started.</param>
/// <param name="ErrorTail">What the program said on stderr before it failed, for the report.</param>
public sealed record ActionItemResult(
    string Path, string? Output, BatchItemStatus Status, int ExitCode, string ErrorTail, Exception? Error);


/// <summary>
/// Runs a <see cref="CustomAction"/> over a selection: one process per file
/// in turn, or one process for all of them, or a built-in handler. Carries
/// the obligations of every operation in Wander that it can carry - a line
/// in the log per item, progress and cancellation through
/// <see cref="OperationTracker"/>, the system-path guard on the folder an
/// output would land in, and an undo step for the outputs the action
/// declared. What it cannot carry is the program's own effect: a process
/// is not undone, which is why only a declared output is.
///
/// <para>
/// Files run one after another, never in parallel: an encoder takes the
/// whole machine, and two of them take it twice as long. Cancelling kills
/// the process of the moment, sends its half-written output to the recycle
/// bin, and marks the rest cancelled.
/// </para>
/// </summary>
public sealed class ExternalActionRunner {
    private readonly IFileSystem _fs;
    private readonly IRecycleBin _bin;
    private readonly UndoService _undo;
    private readonly OperationTracker _tracker;
    private readonly IProcessRunner _processes;
    private readonly IReadOnlyList<IBuiltinAction> _builtins;
    private readonly ILogger _log;
    private readonly Func<string> _tempFolder;
    private readonly PathClaims _claims;


    /// <param name="tempFolder">Where the <c>{list}</c> file is written; asked for per run, the setting may change.</param>
    /// <param name="claims">Where a run claims its inputs and outputs; null keeps them to itself.</param>
    public ExternalActionRunner(
        IFileSystem fs, IRecycleBin bin, UndoService undo, OperationTracker tracker,
        IProcessRunner processes, IReadOnlyList<IBuiltinAction> builtins, ILogger log, Func<string> tempFolder,
        PathClaims? claims = null) {
        _fs = fs;
        _bin = bin;
        _undo = undo;
        _tracker = tracker;
        _processes = processes;
        _builtins = builtins;
        _log = log;
        _tempFolder = tempFolder;
        _claims = claims ?? new PathClaims();
    }


    /// <param name="outputFolder">
    /// Where a declared output goes; beside its source when null. A folder
    /// the user picked, so the outputs of a whole selection land together.
    /// </param>
    /// <returns>One result per path, in the order given.</returns>
    public async Task<IReadOnlyList<ActionItemResult>> RunAsync(
        CustomAction action, IReadOnlyList<string> paths, CancellationToken ct, string? outputFolder = null) {

        if (paths.Count == 0) {
            return Array.Empty<ActionItemResult>();
        }

        string title = action.DisplayTitle;
        _log.Info($"Action '{title}': {paths.Count} item(s), {(action.RunPerFile ? "per file" : "one command")}"
            + (outputFolder is null ? string.Empty : $", output into {outputFolder}"));

        using var busy = _undo.BeginOperation();
        using var operation = _tracker.Begin(OperationVerbs.RunAction, paths.Count, token: ct);
        // The inputs for the whole run; each output from the moment its name
        // is chosen (RunItemAsync). A delete of either meanwhile names the
        // action instead of pulling the file out from under the program.
        using var claim = _claims.Claim(paths, ClaimKind.UserOperation, OperationVerbs.RunAction);

        IReadOnlyList<ActionItemResult> results = action.RunPerFile
            ? await RunPerFileAsync(action, paths, outputFolder, operation, ct).ConfigureAwait(false)
            : await RunOnceAsync(action, paths, outputFolder, operation, ct).ConfigureAwait(false);

        var created = results
            .Where(r => r.Status == BatchItemStatus.Ok && r.Output is not null && Exists(r.Output))
            .Select(r => (IUndoableAction)new CreateAction(_bin, r.Output!))
            .ToList();
        if (created.Count == 1) {
            _undo.Push(created[0]);
        } else if (created.Count > 1) {
            _undo.Push(new CompositeAction($"Run '{title}'", created));
        }

        int ok = results.Count(r => r.Status == BatchItemStatus.Ok);
        int failed = results.Count(r => r.Status == BatchItemStatus.Failed);
        int cancelled = results.Count(r => r.Status == BatchItemStatus.Cancelled);
        _log.Info($"Action '{title}' done: {ok} ok, {failed} failed, {cancelled} cancelled");

        return results;
    }


    private async Task<IReadOnlyList<ActionItemResult>> RunPerFileAsync(
        CustomAction action, IReadOnlyList<string> paths, string? outputFolder, IOperationHandle operation, CancellationToken ct) {

        var results = new ActionItemResult[paths.Count];
        for (int i = 0; i < paths.Count; i++) {
            string path = paths[i];
            if (ct.IsCancellationRequested) {
                results[i] = new ActionItemResult(path, null, BatchItemStatus.Cancelled, 0, string.Empty, null);
                continue;
            }

            operation.SetCurrentPath(path);
            results[i] = await RunItemAsync(action, new[] { path }, path, outputFolder, ct).ConfigureAwait(false);
            operation.Advance(path);
        }

        return results;
    }

    /// <summary>One command for the lot: the first path stands for the selection in the result.</summary>
    private async Task<IReadOnlyList<ActionItemResult>> RunOnceAsync(
        CustomAction action, IReadOnlyList<string> paths, string? outputFolder, IOperationHandle operation, CancellationToken ct) {

        operation.SetCurrentPath(paths[0]);
        var result = await RunItemAsync(action, paths, paths[0], outputFolder, ct).ConfigureAwait(false);
        foreach (string path in paths) {
            operation.Advance(path);
        }

        // Every path shares the one outcome; the output, if any, belongs to
        // the first - it is the one the template named.
        var results = new ActionItemResult[paths.Count];
        for (int i = 0; i < paths.Count; i++) {
            results[i] = i == 0 ? result : result with { Path = paths[i], Output = null };
        }

        return results;
    }

    private async Task<ActionItemResult> RunItemAsync(
        CustomAction action, IReadOnlyList<string> paths, string primary, string? outputFolder, CancellationToken ct) {

        string title = action.DisplayTitle;
        string workingDir = _fs.DirectoryExists(primary) ? primary : Path.GetDirectoryName(primary) ?? primary;
        string? output = null;

        if (action.Output.Length > 0) {
            // The output lands beside its source or where the user said,
            // and either way that is a write.
            string outputDir = outputFolder ?? Path.GetDirectoryName(primary) ?? workingDir;
            if (SystemPathGuard.IsProtected(outputDir, out string reason)) {
                _log.Warn($"Action '{title}' refused for {primary}: {reason}");

                return Failed(primary, null, new IOException(reason));
            }
            output = OutputNames.Resolve(action.Output, primary, Exists, NamesIn, outputFolder);
        }

        using var outputClaim = _claims.Claim(
            output is null ? Array.Empty<string>() : new[] { output }, ClaimKind.UserOperation, OperationVerbs.RunAction);
        var started = DateTime.UtcNow;
        try {
            if (action.Kind == ActionKind.Builtin) {
                await RunBuiltinAsync(action, primary, output, ct).ConfigureAwait(false);
                _log.Info($"Action '{title}': {primary} -> {output ?? "(no declared output)"} [Ok, {Elapsed(started)}]");

                return new ActionItemResult(primary, output, BatchItemStatus.Ok, 0, string.Empty, null);
            }

            var result = await RunProcessAsync(action, paths, workingDir, output, ct).ConfigureAwait(false);
            if (result.WasKilled) {
                DiscardPartial(output);
                _log.Info($"Action '{title}': {primary} cancelled after {Elapsed(started)}");

                return new ActionItemResult(primary, output, BatchItemStatus.Cancelled, result.ExitCode, result.ErrorTail, null);
            }
            if (result.ExitCode != 0) {
                _log.Warn($"Action '{title}': {primary} failed with exit code {result.ExitCode} after {Elapsed(started)}: {ActionReport.LastLine(result.ErrorTail)}");

                return new ActionItemResult(primary, output, BatchItemStatus.Failed, result.ExitCode, result.ErrorTail, null);
            }
            _log.Info($"Action '{title}': {primary} -> {output ?? "(no declared output)"} [Ok, {Elapsed(started)}]");

            return new ActionItemResult(primary, output, BatchItemStatus.Ok, 0, result.ErrorTail, null);
        } catch (OperationCanceledException) {
            DiscardPartial(output);
            _log.Info($"Action '{title}': {primary} cancelled after {Elapsed(started)}");

            return new ActionItemResult(primary, output, BatchItemStatus.Cancelled, 0, string.Empty, null);
        } catch (Exception ex) {
            _log.Error($"Action '{title}' failed for {primary}", ex);

            return Failed(primary, output, ex);
        }
    }

    private async Task RunBuiltinAsync(CustomAction action, string input, string? output, CancellationToken ct) {
        var handler = _builtins.FirstOrDefault(b => string.Equals(b.Name, action.Program, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No built-in action named '{action.Program}'.");

        // No output is a valid shape: an action that produces no file, only
        // an effect on its input. The handler that does need one says so.
        await handler.RunAsync(input, output, action.Arguments, ct).ConfigureAwait(false);
    }

    private async Task<ProcessResult> RunProcessAsync(
        CustomAction action, IReadOnlyList<string> paths, string workingDir, string? output, CancellationToken ct) {

        string? listFile = null;
        if (CommandLine.Uses(action.Arguments, "{list}")) {
            listFile = WriteList(paths);
        }

        try {
            string arguments = CommandLine.Expand(action.Arguments, paths, listFile, output);

            return await _processes
                .RunAsync(new ProcessRequest(action.Program, arguments, workingDir, action.HideConsole), ct)
                .ConfigureAwait(false);
        } finally {
            if (listFile is not null) {
                try {
                    _fs.DeleteFile(listFile);
                } catch (Exception ex) {
                    _log.Warn($"Could not remove list file {listFile}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>One path per line, UTF-8, in scratch space; the caller deletes it.</summary>
    private string WriteList(IReadOnlyList<string> paths) {
        string folder = _tempFolder();
        _fs.CreateDirectory(folder);
        string path = Path.Combine(folder, $"list-{Guid.NewGuid():N}.txt");
        _fs.ReplaceAtomic(path, Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, paths) + Environment.NewLine));

        return path;
    }

    /// <summary>A cancelled run's half-written output goes to the bin, not into the listing.</summary>
    private void DiscardPartial(string? output) {
        if (output is null || !Exists(output)) {
            return;
        }

        try {
            _bin.Send(output);
            _log.Info($"Partial output recycled: {output}");
        } catch (Exception ex) {
            _log.Warn($"Partial output left behind: {output} ({ex.Message})");
        }
    }

    private bool Exists(string path) {
        return _fs.FileExists(path) || _fs.DirectoryExists(path);
    }

    private IEnumerable<string> NamesIn(string folder) {
        return _fs.Enumerate(folder).Select(e => e.Name);
    }

    private static ActionItemResult Failed(string path, string? output, Exception error) {
        return new ActionItemResult(path, output, BatchItemStatus.Failed, 0, error.Message, error);
    }

    private static string Elapsed(DateTime startedUtc) {
        return $"{(DateTime.UtcNow - startedUtc).TotalSeconds:F1} s";
    }
}
