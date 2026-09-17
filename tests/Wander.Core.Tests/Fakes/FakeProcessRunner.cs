using Wander.Core.Actions;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// Records every request and answers by script: the next result in the
/// queue, or a default success. <see cref="OnRun"/> is where a test
/// "writes the output file" or cancels the token mid-run, the way a real
/// program or a click on Cancel would.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner {
    public List<ProcessRequest> Requests { get; } = new();

    public Queue<ProcessResult> Results { get; } = new();

    /// <summary>Called with each request before the result is returned.</summary>
    public Action<ProcessRequest, CancellationToken>? OnRun { get; set; }


    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct) {
        Requests.Add(request);
        OnRun?.Invoke(request, ct);

        if (ct.IsCancellationRequested) {
            return Task.FromResult(new ProcessResult(-1, string.Empty, TimeSpan.Zero, WasKilled: true));
        }

        return Task.FromResult(Results.Count > 0
            ? Results.Dequeue()
            : new ProcessResult(0, string.Empty, TimeSpan.Zero, WasKilled: false));
    }

    public void KillAll() {
    }
}
