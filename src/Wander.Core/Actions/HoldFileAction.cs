namespace Wander.Core.Actions;

/// <summary>
/// Keeps the selected file open, exclusively, for a few seconds and lets go
/// (PLAN AI2). A debug tool with no product use: it is how "the file is
/// busy" is looked at without a second program - the clock on the icon, the
/// "Идёт: ..." line, another operation refused with an explanation, and a
/// real hold for <c>IFileBusyProbe</c> and the Restart Manager, which then
/// name Wander itself as the holder.
///
/// <para>
/// Declares no output and produces no file: what it does to its input is
/// the whole effect, and it is over when the run is. Nothing to undo.
/// </para>
///
/// <para>
/// <see cref="FileShare.None"/> on purpose - the opposite of
/// <c>SharedRead</c>, which is how Wander reads a user's file for itself.
/// This is the user's own operation on the file, and it is meant to be in
/// the way.
/// </para>
/// </summary>
public sealed class HoldFileAction : IBuiltinAction {
    /// <summary>What a preset names in <see cref="CustomAction.Program"/>.</summary>
    public const string ActionName = "hold-file";

    /// <summary>How long to hold, in seconds; the key of <see cref="CustomAction.Arguments"/>.</summary>
    public const string SecondsKey = "seconds";

    private const int DefaultSeconds = 5;
    private const int MaxSeconds = 600;


    public string Name => ActionName;


    public async Task RunAsync(string input, string? output, string arguments, CancellationToken ct) {
        int seconds = BuiltinArguments.Parse(arguments).GetInt(SecondsKey, DefaultSeconds, 1, MaxSeconds);

        using var held = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.None);
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
    }
}
