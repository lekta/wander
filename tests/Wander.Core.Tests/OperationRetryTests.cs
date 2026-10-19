using Wander.Core.Operations;

namespace Wander.Core.Tests;

public class OperationRetryTests {
    [Fact]
    public void Plan_TakesTheFailedAndTheCancelled_Only() {
        var record = Copy(
            new OperationItem(@"C:\a.jpg", @"E:\Photos\a.jpg", BatchItemStatus.Ok, null),
            new OperationItem(@"C:\b.jpg", @"E:\Photos\b (1).jpg", BatchItemStatus.Renamed, null),
            new OperationItem(@"C:\c.jpg", @"E:\Photos\c.jpg", BatchItemStatus.Skipped, null),
            new OperationItem(@"C:\d.jpg", @"E:\Photos\d.jpg", BatchItemStatus.Failed, new IOException("boom")),
            new OperationItem(@"C:\e.jpg", @"E:\Photos\e.jpg", BatchItemStatus.Cancelled, null));

        var plan = OperationRetry.Plan(record);

        Assert.NotNull(plan);
        Assert.Equal(OperationVerbs.Copy, plan.Verb);
        Assert.False(plan.IsDelete);
        Assert.Equal(
            new[] { new RetryItem(@"C:\d.jpg", @"E:\Photos"), new RetryItem(@"C:\e.jpg", @"E:\Photos") },
            plan.Items);
    }

    [Fact]
    public void Plan_OfACopyOffAPhone_TakesTheFilesOnThePhone() {
        // The device did not come back in time (PLAN Y4d): the retry is a
        // copy off the same places on it, into the same folder.
        var record = Copy(
            new OperationItem(@"device:Pixel 6\Storage\DCIM\Camera\PXL_1.jpg", @"E:\Photos\PXL_1.jpg", BatchItemStatus.Ok, null),
            new OperationItem(@"device:Pixel 6\Storage\DCIM\Camera\PXL_2.jpg", @"E:\Photos\PXL_2.jpg", BatchItemStatus.Failed,
                new IOException("gone", unchecked((int)0x8007001F))));

        var plan = OperationRetry.Plan(record);

        Assert.Equal(new[] { new RetryItem(@"device:Pixel 6\Storage\DCIM\Camera\PXL_2.jpg", @"E:\Photos") }, plan!.Items);
    }

    [Fact]
    public void Plan_NothingFailed_IsNull() {
        var record = Copy(new OperationItem(@"C:\a.jpg", @"E:\Photos\a.jpg", BatchItemStatus.Ok, null));

        Assert.Null(OperationRetry.Plan(record));
    }

    [Fact]
    public void Plan_OfADelete_HasNoDestination() {
        var record = OperationRecord.FromItems(1, OperationVerbs.Recycle, new[] {
            new OperationItem(@"C:\a.jpg", null, BatchItemStatus.Ok, null),
            new OperationItem(@"C:\b.jpg", null, BatchItemStatus.Failed, new IOException("in use")),
        }, 0, TimeSpan.Zero, DateTime.UtcNow, null);

        var plan = OperationRetry.Plan(record);

        Assert.NotNull(plan);
        Assert.True(plan.IsDelete);
        Assert.Equal(new[] { new RetryItem(@"C:\b.jpg", null) }, plan.Items);
    }

    [Fact]
    public void Plan_OfAnOperationThatCannotBeRepeated_IsNull() {
        // An extraction or an undo has no items to say what to do again.
        var record = OperationRecord.FromItems(1, OperationVerbs.Extract, new[] {
            new OperationItem(@"C:\x.zip\a.jpg", @"E:\a.jpg", BatchItemStatus.Failed, new IOException("boom")),
        }, 0, TimeSpan.Zero, DateTime.UtcNow, @"E:\");

        Assert.Null(OperationRetry.Plan(record));
    }


    [Fact]
    public void DriveRoots_AreBothEndsOfEveryItem_AndTheTarget_InOrder_WithoutShares() {
        var record = OperationRecord.FromItems(1, OperationVerbs.Copy, new[] {
            new OperationItem(@"f:\DCIM\a.cr3", @"D:\Photos\a.cr3", BatchItemStatus.Ok, null),
            new OperationItem(@"F:\DCIM\b.cr3", @"D:\Photos\b.cr3", BatchItemStatus.Ok, null),
            new OperationItem(@"\\nas\share\c.cr3", @"D:\Photos\c.cr3", BatchItemStatus.Ok, null),
        }, 0, TimeSpan.Zero, DateTime.UtcNow, @"D:\Photos");

        Assert.Equal(new[] { @"F:\", @"D:\" }, record.DriveRoots());
    }


    private static OperationRecord Copy(params OperationItem[] items) {
        return OperationRecord.FromItems(1, OperationVerbs.Copy, items, 0, TimeSpan.Zero, DateTime.UtcNow, @"E:\Photos");
    }
}
