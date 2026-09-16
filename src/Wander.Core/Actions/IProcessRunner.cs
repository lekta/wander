namespace Wander.Core.Actions;

/// <param name="Program">A full path, or a name for <c>PATH</c> to resolve.</param>
/// <param name="Arguments">The command line after the program, already quoted (<see cref="CommandLine"/>).</param>
/// <param name="HideWindow">
/// No console window. Also what makes stderr ours to read: a program
/// allowed its own window writes its errors there, and the report gets
/// only the exit code.
/// </param>
public sealed record ProcessRequest(string Program, string Arguments, string WorkingDirectory, bool HideWindow);


/// <param name="ErrorTail">The last part of what the program wrote to stderr; empty when it had a window of its own.</param>
/// <param name="WasKilled">The run was cancelled and the process tree killed; the exit code then says nothing.</param>
public sealed record ProcessResult(int ExitCode, string ErrorTail, TimeSpan Elapsed, bool WasKilled);


/// <summary>
/// Starts a program and waits for it. The one place Core hands work to
/// something outside the process; the implementation lives in Platform
/// because starting, draining and killing a process tree is Windows'
/// business.
///
/// <para>
/// Cancellation kills the process and everything it started, and comes
/// back as <see cref="ProcessResult.WasKilled"/> rather than as an
/// exception: the caller has a partial output to tidy up either way. A
/// program that cannot be started at all does throw - there is no exit
/// code to report.
/// </para>
/// </summary>
public interface IProcessRunner {
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);
}


/// <summary>
/// An action Wander performs itself - encoding a picture through WIC,
/// where there is no program to start. Registered with
/// <see cref="ExternalActionRunner"/> by name, which is what a built-in
/// <see cref="CustomAction"/> carries in <see cref="CustomAction.Program"/>.
/// </summary>
public interface IBuiltinAction {
    string Name { get; }

    /// <param name="input">The selected file.</param>
    /// <param name="output">Where to write; already unique, see <see cref="OutputNames"/>.</param>
    /// <param name="arguments">The action's own <c>key=value;...</c> settings.</param>
    Task RunAsync(string input, string output, string arguments, CancellationToken ct);
}
