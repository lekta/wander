using Wander.Core.FileSystem;
using Wander.Core.Logging;
using Wander.Core.Operations;
using Wander.Core.Shell;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class DeviceImportServiceTests {
    private const string Camera = @"device:Pixel 6\Storage\DCIM\Camera";
    private const string Shot1 = @"device:Pixel 6\Storage\DCIM\Camera\PXL_1.jpg";
    private const string Shot2 = @"device:Pixel 6\Storage\DCIM\Camera\PXL_2.jpg";
    private const string Raw = @"device:Pixel 6\Storage\DCIM\Camera\Raw";
    private const string Shot3 = @"device:Pixel 6\Storage\DCIM\Camera\Raw\PXL_3.dng";

    private const string Target = @"D:\Photos";

    private static readonly DateTime _shot = new(2026, 6, 12, 13, 13, 38, DateTimeKind.Utc);


    private sealed class Scene {
        public Scene() {
            Fs.Directories.Add(Target);
            Device.AddFolder(Camera);
            Device.AddFolder(Raw);
            Device.AddFile(Shot1, "first picture", _shot);
            Device.AddFile(Shot2, "second picture", _shot.AddMinutes(1));
            Device.AddFile(Shot3, "a raw one", _shot.AddMinutes(2));
            Bin = new FakeRecycleBin(Fs);
            // The clock moves only when the import waits: ten minutes are a
            // few hundred waits, and no time passes in the test.
            Service = new DeviceImportService(
                Device, Fs, Bin, Undo, Tracker, NullLogger.Instance,
                delay: (span, _) => {
                    Now += span;

                    return Task.CompletedTask;
                },
                now: () => Now);
            Tracker.Changed += (_, _) => {
                foreach (var op in Tracker.Snapshot()) {
                    Pauses.Add(op.PausedReason);
                }
            };
        }


        public FakeFileSystem Fs { get; } = new();

        public FakeDeviceContent Device { get; } = new();

        public FakeRecycleBin Bin { get; }

        public UndoService Undo { get; } = new();

        public OperationTracker Tracker { get; } = new(TimeSpan.Zero);

        public DeviceImportService Service { get; }

        public DateTime Now { get; set; } = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>What every change of the running operation said it waited for.</summary>
        public List<string?> Pauses { get; } = new();


        public Task<IReadOnlyList<BatchItemResult>> Import(params string[] sources) {
            return Service.ImportAsync(sources, Target, new FakeConflictResolver(), verify: true, CancellationToken.None);
        }

        public Task<IReadOnlyList<BatchItemResult>> Import(IConflictResolver resolver, params string[] sources) {
            return Service.ImportAsync(sources, Target, resolver, verify: true, CancellationToken.None);
        }
    }


    // --- A plain copy ----------------------------------------------------

    [Fact]
    public async Task AFile_LandsWhole_DatedAsOnTheDevice_AndReadBack() {
        var scene = new Scene();

        var results = await scene.Import(Shot1);

        var result = Assert.Single(results);
        Assert.Equal(BatchItemStatus.Ok, result.Status);
        Assert.Equal(@"D:\Photos\PXL_1.jpg", result.FinalDestination);
        Assert.True(result.Verified);
        Assert.Equal("first picture", System.Text.Encoding.UTF8.GetString(scene.Fs.Files[@"D:\Photos\PXL_1.jpg"]));
        Assert.Equal(_shot, scene.Fs.ModifiedTimes[@"D:\Photos\PXL_1.jpg"]);
        Assert.True(Assert.Single(scene.Fs.CopyOptionsSeen).Verify);
    }

    [Fact]
    public async Task AFolder_ComesWithEverythingInIt_AndUndoTakesTheFolder() {
        var scene = new Scene();

        var results = await scene.Import(Camera);

        Assert.Equal(BatchItemStatus.Ok, Assert.Single(results).Status);
        Assert.True(scene.Fs.FileExists(@"D:\Photos\Camera\PXL_1.jpg"));
        Assert.True(scene.Fs.FileExists(@"D:\Photos\Camera\PXL_2.jpg"));
        Assert.True(scene.Fs.FileExists(@"D:\Photos\Camera\Raw\PXL_3.dng"));

        scene.Undo.Undo();

        // One step for the folder: everything under it goes with it.
        Assert.Equal(new[] { @"Recycle:D:\Photos\Camera" }, scene.Bin.CallLog);
    }

    [Fact]
    public async Task TheRecord_CountsTheBytesAndTheReadBack() {
        var scene = new Scene();

        await scene.Import(Shot1, Shot2);

        var record = Assert.Single(scene.Tracker.Finished);
        Assert.Equal(OperationOutcome.Done, record.Outcome);
        Assert.Equal(2, record.Verified);
        Assert.Equal("first picture".Length + "second picture".Length, record.Bytes);
        Assert.Equal(OperationVerbs.Copy, record.Verb);
    }

    [Fact]
    public async Task AFileGoneFromTheDevice_FailsAlone() {
        var scene = new Scene();

        var results = await scene.Import(@"device:Pixel 6\Storage\DCIM\Camera\gone.jpg", Shot1);

        Assert.Equal(BatchItemStatus.Failed, results[0].Status);
        Assert.IsType<FileNotFoundException>(results[0].Error);
        Assert.Equal(BatchItemStatus.Ok, results[1].Status);
    }

    [Fact]
    public async Task APhoneUnpluggedBeforeTheCopy_SaysSo_RatherThanAMissingFile() {
        var scene = new Scene();
        scene.Device.State = DeviceState.Gone;

        var result = Assert.Single(await scene.Import(Shot1));

        Assert.Equal(BatchItemStatus.Failed, result.Status);
        Assert.True(DeviceErrors.IsGone(result.Error!));
        Assert.IsNotType<FileNotFoundException>(result.Error);
    }

    [Fact]
    public async Task ATransientError_IsTriedAgain() {
        var scene = new Scene();
        scene.Device.ReadFailures[Shot1] = new Queue<Exception>(new[] {
            new IOException("crc", unchecked((int)0x80070017)),
            new IOException("crc", unchecked((int)0x80070017)),
        });

        var result = Assert.Single(await scene.Import(Shot1));

        Assert.Equal(BatchItemStatus.Ok, result.Status);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task APermanentError_FailsAtOnce() {
        var scene = new Scene();
        scene.Device.ReadFailures[Shot1] = new Queue<Exception>(new[] { new UnauthorizedAccessException("no") });

        var result = Assert.Single(await scene.Import(Shot1));

        Assert.Equal(BatchItemStatus.Failed, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Empty(scene.Pauses.OfType<string>());
    }


    // --- The device goes away --------------------------------------------

    [Fact]
    public async Task UnpluggedMidFile_Pauses_AndOnItsReturnCopiesTheFileAgainWhole() {
        var scene = new Scene();
        scene.Device.BreakAfter[Shot2] = 4;
        scene.Device.BackAfter = 3;

        var results = await scene.Import(Shot1, Shot2);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Ok, r.Status));
        Assert.Equal("second picture", System.Text.Encoding.UTF8.GetString(scene.Fs.Files[@"D:\Photos\PXL_2.jpg"]));
        // The done file is not read again; the broken one is, from the start.
        Assert.Equal(new[] { Shot1, Shot2, Shot2 }, scene.Device.Opened);
        Assert.Contains(DeviceImportService.PausedUnplugged, scene.Pauses);
        Assert.Null(scene.Pauses[^1]);
        Assert.Equal("first picture".Length + "second picture".Length, scene.Tracker.Finished[0].Bytes);
    }

    [Fact]
    public async Task ALockedPhone_IsWaitedForWithItsOwnReason() {
        var scene = new Scene();
        scene.Device.BreakAfter[Shot1] = 2;
        scene.Device.BackAfter = 1;
        scene.Device.BackAs = DeviceState.Locked;

        // Back locked first: the pause names the lock; then a look later it
        // comes back ready - the fake's second return.
        var result = Assert.Single(await scene.Import(Shot1));

        Assert.Equal(BatchItemStatus.Ok, result.Status);
        Assert.Contains(DeviceImportService.PausedLocked, scene.Pauses);
    }

    [Fact]
    public async Task NotBackInTenMinutes_FailsThatFileAndTheRest_WithoutReadingThem() {
        var scene = new Scene();
        scene.Device.BreakAfter[Shot1] = 4;
        var started = scene.Now;

        var results = await scene.Import(Shot1, Shot2);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Failed, r.Status));
        Assert.Equal(new[] { Shot1 }, scene.Device.Opened);
        Assert.True(scene.Now - started >= DeviceImportService.WaitForDevice);
        Assert.False(scene.Fs.FileExists(@"D:\Photos\PXL_1.jpg"));
        Assert.True(OperationRetry.Plan(scene.Tracker.Finished[0]) is { Items.Count: 2 });
    }

    [Fact]
    public async Task Cancelled_WhileWaiting_StopsTheWait() {
        var scene = new Scene();
        scene.Device.BreakAfter[Shot1] = 4;
        using var cts = new CancellationTokenSource();
        var service = new DeviceImportService(
            scene.Device, scene.Fs, scene.Bin, scene.Undo, scene.Tracker, NullLogger.Instance,
            delay: (_, ct) => {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();

                return Task.CompletedTask;
            },
            now: () => scene.Now);

        var results = await service.ImportAsync(new[] { Shot1, Shot2 }, Target, new FakeConflictResolver(), verify: true, cts.Token);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Cancelled, r.Status));
        Assert.Empty(scene.Tracker.Snapshot());
    }


    // --- Names taken at the target ---------------------------------------

    [Fact]
    public async Task TheSamePhotoCopiedBefore_IsShownAsIdentical() {
        var scene = new Scene();
        scene.Fs.Files[@"D:\Photos\PXL_1.jpg"] = System.Text.Encoding.UTF8.GetBytes("first picture");
        scene.Fs.ModifiedTimes[@"D:\Photos\PXL_1.jpg"] = _shot;
        var resolver = new FakeConflictResolver(ConflictResolution.Skip);

        var result = Assert.Single(await scene.Import(resolver, Shot1));

        Assert.Equal(BatchItemStatus.Skipped, result.Status);
        var conflict = Assert.Single(resolver.Conflicts);
        Assert.True(ConflictVerdict.Of(conflict).Identical);
        Assert.Empty(scene.Device.Opened);
    }

    [Fact]
    public async Task Replace_SendsTheOldToTheBin_AndUndoBringsItBack() {
        var scene = new Scene();
        scene.Fs.Files[@"D:\Photos\PXL_1.jpg"] = System.Text.Encoding.UTF8.GetBytes("old");

        var result = Assert.Single(await scene.Import(new FakeConflictResolver(ConflictResolution.Replace), Shot1));

        Assert.Equal(BatchItemStatus.Replaced, result.Status);
        Assert.Equal("first picture", System.Text.Encoding.UTF8.GetString(scene.Fs.Files[@"D:\Photos\PXL_1.jpg"]));
        Assert.Contains(@"Recycle:D:\Photos\PXL_1.jpg", scene.Bin.CallLog);
    }

    [Fact]
    public async Task AFolderMergedAgain_TakesOnlyWhatIsNew_AndReplacesNothing() {
        var scene = new Scene();
        scene.Fs.Directories.Add(@"D:\Photos\Camera");
        // Copied last time: the same file.
        scene.Fs.Files[@"D:\Photos\Camera\PXL_1.jpg"] = System.Text.Encoding.UTF8.GetBytes("first picture");
        scene.Fs.ModifiedTimes[@"D:\Photos\Camera\PXL_1.jpg"] = _shot;
        // Another file under a name the phone uses too.
        scene.Fs.Files[@"D:\Photos\Camera\PXL_2.jpg"] = System.Text.Encoding.UTF8.GetBytes("something else");

        var result = Assert.Single(await scene.Import(new FakeConflictResolver(ConflictResolution.Merge), Camera));

        Assert.Equal(BatchItemStatus.Merged, result.Status);
        Assert.DoesNotContain(Shot1, scene.Device.Opened);
        Assert.Equal("something else", System.Text.Encoding.UTF8.GetString(scene.Fs.Files[@"D:\Photos\Camera\PXL_2.jpg"]));
        Assert.Equal("second picture", System.Text.Encoding.UTF8.GetString(scene.Fs.Files[@"D:\Photos\Camera\PXL_2 (1).jpg"]));
        Assert.True(scene.Fs.FileExists(@"D:\Photos\Camera\Raw\PXL_3.dng"));

        scene.Undo.Undo();

        // What came in, one by one; the folder that was there stays.
        Assert.Contains(@"Recycle:D:\Photos\Camera\PXL_2 (1).jpg", scene.Bin.CallLog);
        Assert.Contains(@"Recycle:D:\Photos\Camera\Raw", scene.Bin.CallLog);
        Assert.DoesNotContain(@"Recycle:D:\Photos\Camera", scene.Bin.CallLog);
        Assert.DoesNotContain(@"Recycle:D:\Photos\Camera\Raw\PXL_3.dng", scene.Bin.CallLog);
    }

    [Fact]
    public async Task CancelInTheWindow_CopiesNothing() {
        var scene = new Scene();
        scene.Fs.Files[@"D:\Photos\PXL_1.jpg"] = System.Text.Encoding.UTF8.GetBytes("old");

        var results = await scene.Import(new FakeConflictResolver(ConflictResolution.Cancel), Shot1, Shot2);

        Assert.All(results, r => Assert.Equal(BatchItemStatus.Cancelled, r.Status));
        Assert.Empty(scene.Device.Opened);
        Assert.Empty(scene.Tracker.Finished);
    }

    [Fact]
    public async Task IntoTheWindowsTree_IsRefused() {
        var scene = new Scene();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windows)) {
            return;
        }

        await Assert.ThrowsAsync<IOException>(() =>
            scene.Service.ImportAsync(new[] { Shot1 }, windows, new FakeConflictResolver(), verify: true, CancellationToken.None));
    }
}
