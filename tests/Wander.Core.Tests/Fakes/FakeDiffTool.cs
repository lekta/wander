using Wander.Core.Preview;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// Answers with <see cref="Answer"/> and records what it was asked. With
/// <see cref="Hangs"/> it waits for the token, the way a program that never
/// exits would.
/// </summary>
internal sealed class FakeDiffTool : IDiffTool {
    public bool IsAvailable { get; set; } = true;

    public DiffToolRun Answer { get; set; } = new(0, Array.Empty<byte>(), false, "");

    public bool Hangs { get; set; }

    public List<IReadOnlyList<string>> Calls { get; } = new();


    public async Task<DiffToolRun> RunAsync(IReadOnlyList<string> arguments, int maxOutput, CancellationToken ct) {
        Calls.Add(arguments);
        if (Hangs) {
            await Task.Delay(Timeout.Infinite, ct);
        }

        return Answer;
    }
}
