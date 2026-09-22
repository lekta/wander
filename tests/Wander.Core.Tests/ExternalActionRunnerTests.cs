using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class ExternalActionRunnerTests {
    private const string Folder = @"C:\videos";
    private const string A = @"C:\videos\a.mov";
    private const string B = @"C:\videos\b.mov";
    private const string Temp = @"C:\tmp";

    private static readonly CustomAction _encode = new() {
        Id = "encode", Title = "To MP4", Program = "ffmpeg",
        Arguments = "-i {path} {out}", Output = "{name}.mp4", RunPerFile = true,
    };


    private static (ExternalActionRunner Runner, FakeFileSystem Fs, FakeRecycleBin Bin, UndoService Undo, FakeProcessRunner Processes, List<IBuiltinAction> Builtins) Setup() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Folder);
        fs.Files[A] = new byte[] { 1 };
        fs.Files[B] = new byte[] { 2 };
        var bin = new FakeRecycleBin(fs);
        var undo = new UndoService();
        var processes = new FakeProcessRunner();
        var builtins = new List<IBuiltinAction>();
        var runner = new ExternalActionRunner(
            fs, bin, undo, new OperationTracker(), processes, builtins, NullLogger.Instance, () => Temp);

        return (runner, fs, bin, undo, processes, builtins);
    }


    [Fact]
    public async Task PerFile_RunsOneProcessPerItem_InOrder_InTheFilesFolder() {
        var (runner, _, _, _, processes, _) = Setup();

        var results = await runner.RunAsync(_encode, new[] { A, B }, CancellationToken.None);

        Assert.Equal(2, processes.Requests.Count);
        Assert.Equal("ffmpeg", processes.Requests[0].Program);
        Assert.Equal(@"-i ""C:\videos\a.mov"" ""C:\videos\a.mp4""", processes.Requests[0].Arguments);
        Assert.Equal(@"-i ""C:\videos\b.mov"" ""C:\videos\b.mp4""", processes.Requests[1].Arguments);
        Assert.All(processes.Requests, r => Assert.Equal(Folder, r.WorkingDirectory));
        Assert.All(results, r => Assert.Equal(BatchItemStatus.Ok, r.Status));
        Assert.Equal(@"C:\videos\a.mp4", results[0].Output);
    }

    [Fact]
    public async Task TheInputAndTheOutput_AreClaimed_WhileTheProgramRuns() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(Folder);
        fs.Files[A] = new byte[] { 1 };
        var processes = new FakeProcessRunner();
        var claims = new PathClaims();
        var runner = new ExternalActionRunner(
            fs, new FakeRecycleBin(fs), new UndoService(), new OperationTracker(), processes,
            new List<IBuiltinAction>(), NullLogger.Instance, () => Temp, claims);
        bool claimedDuring = false;
        processes.OnRun = (request, _) => claimedDuring =
            claims.IsClaimed(A, ClaimKind.UserOperation) && claims.IsClaimed(OutputOf(request), ClaimKind.UserOperation);

        await runner.RunAsync(_encode, new[] { A }, CancellationToken.None);

        Assert.True(claimedDuring);
        Assert.Equal(0, claims.Count);
    }

    [Fact]
    public async Task DeclaredOutput_ThatAppeared_IsOneUndoStep_ToTheBin() {
        var (runner, fs, bin, undo, processes, _) = Setup();
        processes.OnRun = (request, _) => fs.Files[OutputOf(request)] = new byte[] { 9 };

        await runner.RunAsync(_encode, new[] { A, B }, CancellationToken.None);

        Assert.Equal(1, undo.Depth);
        Assert.True(fs.FileExists(@"C:\videos\a.mp4"));
        undo.Undo();
        Assert.Contains(@"Recycle:C:\videos\a.mp4", bin.CallLog);
        Assert.Contains(@"Recycle:C:\videos\b.mp4", bin.CallLog);
        Assert.False(fs.FileExists(@"C:\videos\a.mp4"));
    }

    [Fact]
    public async Task OutputThatNeverAppeared_LeavesNothingToUndo() {
        var (runner, _, _, undo, _, _) = Setup();

        await runner.RunAsync(_encode, new[] { A }, CancellationToken.None);

        Assert.Equal(0, undo.Depth);
    }

    [Fact]
    public async Task NonZeroExit_IsAFailure_WithTheErrorTail() {
        var (runner, _, _, _, processes, _) = Setup();
        processes.Results.Enqueue(new ProcessResult(1, "Unknown encoder 'x265'\n", TimeSpan.Zero, false));

        var results = await runner.RunAsync(_encode, new[] { A, B }, CancellationToken.None);

        Assert.Equal(BatchItemStatus.Failed, results[0].Status);
        Assert.Equal(1, results[0].ExitCode);
        Assert.Contains("x265", results[0].ErrorTail);
        // One failure does not stop the batch.
        Assert.Equal(BatchItemStatus.Ok, results[1].Status);
    }

    [Fact]
    public async Task Cancel_KillsTheCurrent_RecyclesItsHalfOutput_SkipsTheRest() {
        var (runner, fs, bin, _, processes, _) = Setup();
        using var cts = new CancellationTokenSource();
        processes.OnRun = (request, _) => {
            fs.Files[OutputOf(request)] = new byte[] { 0 };
            cts.Cancel();
        };

        var results = await runner.RunAsync(_encode, new[] { A, B }, cts.Token);

        Assert.Single(processes.Requests);
        Assert.Equal(BatchItemStatus.Cancelled, results[0].Status);
        Assert.Equal(BatchItemStatus.Cancelled, results[1].Status);
        Assert.Contains(@"Recycle:C:\videos\a.mp4", bin.CallLog);
        Assert.False(fs.FileExists(@"C:\videos\a.mp4"));
    }

    [Fact]
    public async Task OutputFolder_PutsEveryOutputThere_UniqueThereToo() {
        var (runner, fs, _, _, processes, _) = Setup();
        fs.Directories.Add(@"C:\out");
        fs.Files[@"C:\out\a.mp4"] = new byte[] { 7 };

        var results = await runner.RunAsync(_encode, new[] { A, B }, CancellationToken.None, outputFolder: @"C:\out");

        Assert.Equal(@"C:\out\a (1).mp4", results[0].Output);
        Assert.Equal(@"C:\out\b.mp4", results[1].Output);
        Assert.Contains(@"""C:\out\a (1).mp4""", processes.Requests[0].Arguments);
        // The program still runs where its input is.
        Assert.All(processes.Requests, r => Assert.Equal(Folder, r.WorkingDirectory));
    }

    [Fact]
    public async Task OutputFolder_ThatIsProtected_IsRefused() {
        var (runner, _, _, _, processes, _) = Setup();

        var results = await runner.RunAsync(_encode, new[] { A }, CancellationToken.None, outputFolder: @"C:\Windows\Temp");

        Assert.Empty(processes.Requests);
        Assert.Equal(BatchItemStatus.Failed, results[0].Status);
    }

    [Fact]
    public async Task OutputNeverLandsOnAnExistingFile() {
        var (runner, fs, _, _, processes, _) = Setup();
        fs.Files[@"C:\videos\a.mp4"] = new byte[] { 7 };

        var results = await runner.RunAsync(_encode, new[] { A }, CancellationToken.None);

        Assert.Equal(@"C:\videos\a (1).mp4", results[0].Output);
        Assert.Contains(@"""C:\videos\a (1).mp4""", processes.Requests[0].Arguments);
    }

    [Fact]
    public async Task OneCommand_RunsOnce_WithEveryPath_AndReportsEachOfThem() {
        var (runner, _, _, _, processes, _) = Setup();
        var action = new CustomAction { Id = "zip", Title = "Zip", Program = "7z", Arguments = "a out.7z {paths}", RunPerFile = false };

        var results = await runner.RunAsync(action, new[] { A, B }, CancellationToken.None);

        var request = Assert.Single(processes.Requests);
        Assert.Equal(@"a out.7z ""C:\videos\a.mov"" ""C:\videos\b.mov""", request.Arguments);
        Assert.Equal(2, results.Count);
        Assert.Equal(new[] { A, B }, results.Select(r => r.Path));
        Assert.All(results, r => Assert.Equal(BatchItemStatus.Ok, r.Status));
    }

    [Fact]
    public async Task ListFile_IsWrittenForTheRun_AndRemovedAfter() {
        var (runner, fs, _, _, processes, _) = Setup();
        var action = new CustomAction { Id = "l", Title = "List", Program = "tool", Arguments = "@{list}", RunPerFile = false };
        string? listPath = null;
        processes.OnRun = (request, _) => {
            listPath = request.Arguments.Trim('@', '"');
            Assert.True(fs.FileExists(listPath));
            Assert.Equal(A + Environment.NewLine + B + Environment.NewLine, System.Text.Encoding.UTF8.GetString(fs.Files[listPath]));
        };

        await runner.RunAsync(action, new[] { A, B }, CancellationToken.None);

        Assert.NotNull(listPath);
        Assert.StartsWith(Temp, listPath);
        Assert.False(fs.FileExists(listPath!));
    }

    [Fact]
    public async Task Builtin_IsFoundByName_AndGetsTheUniqueOutput() {
        var (runner, _, _, _, processes, builtins) = Setup();
        var handler = new RecordingBuiltin("image-convert");
        builtins.Add(handler);
        var action = new CustomAction {
            Id = "jpeg", Title = "To JPEG", Kind = ActionKind.Builtin, Program = "image-convert",
            Arguments = "format=jpeg;quality=85", Output = "{name}.jpg",
        };

        var results = await runner.RunAsync(action, new[] { A }, CancellationToken.None);

        Assert.Empty(processes.Requests);
        Assert.Equal((A, @"C:\videos\a.jpg", "format=jpeg;quality=85"), Assert.Single(handler.Calls));
        Assert.Equal(BatchItemStatus.Ok, results[0].Status);
    }

    [Fact]
    public async Task UnknownBuiltin_FailsTheItem_NotTheProcess() {
        var (runner, _, _, _, _, _) = Setup();
        var action = new CustomAction { Id = "x", Title = "X", Kind = ActionKind.Builtin, Program = "nope", Output = "{name}.x" };

        var results = await runner.RunAsync(action, new[] { A }, CancellationToken.None);

        Assert.Equal(BatchItemStatus.Failed, results[0].Status);
        Assert.IsType<InvalidOperationException>(results[0].Error);
    }

    [Fact]
    public async Task OutputIntoAProtectedFolder_IsRefused() {
        var (runner, fs, _, _, processes, _) = Setup();
        const string system = @"C:\Windows\System32\x.mov";
        fs.Files[system] = new byte[] { 1 };

        var results = await runner.RunAsync(_encode, new[] { system }, CancellationToken.None);

        Assert.Empty(processes.Requests);
        Assert.Equal(BatchItemStatus.Failed, results[0].Status);
        Assert.IsType<IOException>(results[0].Error);
    }

    [Fact]
    public async Task ProgramThatCannotStart_FailsTheItem() {
        var (runner, _, _, _, processes, _) = Setup();
        processes.OnRun = (_, _) => throw new System.ComponentModel.Win32Exception("The system cannot find the file specified");

        var results = await runner.RunAsync(_encode, new[] { A }, CancellationToken.None);

        Assert.Equal(BatchItemStatus.Failed, results[0].Status);
        Assert.Contains("cannot find", results[0].ErrorTail);
    }

    [Fact]
    public async Task EmptySelection_DoesNothing() {
        var (runner, _, _, undo, processes, _) = Setup();

        var results = await runner.RunAsync(_encode, Array.Empty<string>(), CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(processes.Requests);
        Assert.Equal(0, undo.Depth);
    }


    /// <summary>The quoted last argument of an encode request - where the fake "writes" the output.</summary>
    private static string OutputOf(ProcessRequest request) {
        int quote = request.Arguments.LastIndexOf('"', request.Arguments.Length - 2);

        return request.Arguments[(quote + 1)..^1];
    }


    private sealed class RecordingBuiltin : IBuiltinAction {
        public RecordingBuiltin(string name) {
            Name = name;
        }

        public string Name { get; }

        public List<(string Input, string? Output, string Arguments)> Calls { get; } = new();

        public Task RunAsync(string input, string? output, string arguments, CancellationToken ct) {
            Calls.Add((input, output, arguments));

            return Task.CompletedTask;
        }
    }
}
