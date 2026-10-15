using System.Security.Cryptography;
using Wander.Core.FileSystem;
using Wander.Core.Operations;

namespace Wander.Core.Tests;

public class StreamCopyTests {
    [Fact]
    public void Copy_MovesEveryByte_ReportsThem_AndHashesWhatWentAcross() {
        byte[] data = Enumerable.Range(0, 10_000).Select(i => (byte)(i * 7)).ToArray();
        using var from = new MemoryStream(data);
        using var to = new MemoryStream();
        long reported = 0;

        byte[] hash = StreamCopy.Copy(from, to, new Counter(n => reported += n), default, bufferSize: 4096);

        Assert.Equal(data, to.ToArray());
        Assert.Equal(data.Length, reported);
        Assert.Equal(SHA256.HashData(data), hash);
    }

    [Fact]
    public void Hash_OfTheCopy_IsTheHashOfTheCopy() {
        byte[] data = new byte[5000];
        new Random(42).NextBytes(data);
        using var from = new MemoryStream(data);
        using var to = new MemoryStream();
        byte[] copied = StreamCopy.Copy(from, to, null, default, bufferSize: 1000);

        to.Position = 0;

        Assert.Equal(copied, StreamCopy.Hash(to, default, bufferSize: 1000));
    }

    [Fact]
    public void ACopyThatCameOutDifferent_HashesDifferently() {
        byte[] data = new byte[3000];
        new Random(1).NextBytes(data);
        using var from = new MemoryStream(data);
        using var to = new MemoryStream();
        byte[] copied = StreamCopy.Copy(from, to, null, default);

        byte[] landed = to.ToArray();
        landed[1234] ^= 0x01;

        Assert.NotEqual(copied, StreamCopy.Hash(new MemoryStream(landed), default));
    }

    [Fact]
    public void Copy_StopsBetweenBuffers_WhenCancelled() {
        using var from = new MemoryStream(new byte[100_000]);
        using var to = new MemoryStream();
        using var cts = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() => StreamCopy.Copy(
            from, to, new Counter(_ => cts.Cancel()), cts.Token, bufferSize: 1000));

        Assert.Equal(1000, to.Length);
    }


    private sealed class Counter : IProgress<long> {
        private readonly Action<long> _report;


        public Counter(Action<long> report) {
            _report = report;
        }


        public void Report(long value) => _report(value);
    }
}


public class CopyVerificationTests {
    [Theory]
    [InlineData(VerifyPolicy.Removable, VolumeKind.Removable, false, true)]
    [InlineData(VerifyPolicy.Removable, VolumeKind.Fixed, false, false)]
    [InlineData(VerifyPolicy.Removable, VolumeKind.Unknown, true, true)]
    [InlineData(VerifyPolicy.Removable, VolumeKind.Network, false, false)]
    [InlineData(VerifyPolicy.Always, VolumeKind.Fixed, false, true)]
    [InlineData(VerifyPolicy.Never, VolumeKind.Removable, true, false)]
    public void ACopyIsCheckedBySettingAndSource(VerifyPolicy policy, VolumeKind source, bool device, bool expected) {
        Assert.Equal(expected, CopyVerification.ShouldVerify(policy, source, device));
    }

    [Fact]
    public void TwoCopies_AreVerifiedOnlyTogether_AndNameWindowsWhenEitherWentThroughIt() {
        var own = new CopyReport(CopyEngine.Own, true);
        var system = new CopyReport(CopyEngine.System, true);
        var unchecked_ = new CopyReport(CopyEngine.Own, false);

        Assert.Equal(own, CopyReport.None.And(own));
        Assert.Equal(new CopyReport(CopyEngine.System, true), own.And(system));
        Assert.Equal(new CopyReport(CopyEngine.Own, false), own.And(unchecked_));
    }

    [Fact]
    public void ACopyThatDidNotReadBack_IsTriedAgain() {
        Assert.True(TransientIo.IsTransient(new CopyMismatchException(@"F:\a.cr3", @"D:\a.cr3")));
    }
}
