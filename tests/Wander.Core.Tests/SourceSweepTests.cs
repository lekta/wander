using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

/// <summary>
/// What a folder moved off its volume loses at the source once the copy is
/// whole (audit 2026-11-18, F2): only what was copied and is still as it
/// was copied; what came or changed meanwhile stays, and says why.
/// </summary>
public class SourceSweepTests {
    private static readonly DateTime _t = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);


    [Fact]
    public void WhatWasCopiedAndIsUnchanged_Goes() {
        var copied = new[] { new SourceFile(@"D:\p\a.txt", 10, _t), new SourceFile(@"D:\p\sub\b.txt", 20, _t) };

        var plan = SourceSweep.Plan(copied, copied);

        Assert.Equal(new[] { @"D:\p\a.txt", @"D:\p\sub\b.txt" }, plan.Delete);
        Assert.Empty(plan.Keep);
    }

    [Fact]
    public void AFileThatAppearedAfterItsFolderWasWalked_Stays() {
        var copied = new[] { new SourceFile(@"D:\p\sub\b.txt", 20, _t) };
        var now = new[] { new SourceFile(@"D:\p\sub\b.txt", 20, _t), new SourceFile(@"D:\p\sub\new.jpg", 5, _t.AddMinutes(1)) };

        var plan = SourceSweep.Plan(copied, now);

        Assert.Equal(new[] { @"D:\p\sub\b.txt" }, plan.Delete);
        Assert.Equal(new[] { new SourceLeftover(@"D:\p\sub\new.jpg", LeftReason.Appeared) }, plan.Keep);
    }

    [Fact]
    public void AFileChangedAfterItWasCopied_Stays() {
        var copied = new[] { new SourceFile(@"D:\p\a.txt", 10, _t) };

        var bySize = SourceSweep.Plan(copied, new[] { new SourceFile(@"D:\p\a.txt", 11, _t) });
        var byTime = SourceSweep.Plan(copied, new[] { new SourceFile(@"D:\p\a.txt", 10, _t.AddSeconds(2)) });

        Assert.Empty(bySize.Delete);
        Assert.Equal(new[] { new SourceLeftover(@"D:\p\a.txt", LeftReason.Changed) }, bySize.Keep);
        Assert.Empty(byTime.Delete);
        Assert.Equal(LeftReason.Changed, Assert.Single(byTime.Keep).Reason);
    }

    [Fact]
    public void AFileGoneSinceTheCopy_IsNeitherDeletedNorLeft() {
        var copied = new[] { new SourceFile(@"D:\p\a.txt", 10, _t) };

        var plan = SourceSweep.Plan(copied, Array.Empty<SourceFile>());

        Assert.Empty(plan.Delete);
        Assert.Empty(plan.Keep);
    }

    [Fact]
    public void PathsMatchWithoutCase() {
        var copied = new[] { new SourceFile(@"D:\P\A.TXT", 10, _t) };

        var plan = SourceSweep.Plan(copied, new[] { new SourceFile(@"d:\p\a.txt", 10, _t) });

        Assert.Single(plan.Delete);
        Assert.Empty(plan.Keep);
    }
}
