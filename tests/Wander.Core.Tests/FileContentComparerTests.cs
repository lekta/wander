using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class FileContentComparerTests {
    private const string Left = @"C:\a\file.bin";
    private const string Right = @"C:\b\file.bin";
    private const int Block = 64 * 1024;


    private static FakeFileSystem With(byte[] left, byte[] right) {
        var fs = new FakeFileSystem();
        fs.Files[Left] = left;
        fs.Files[Right] = right;

        return fs;
    }

    private static byte[] Bytes(int count) {
        var bytes = new byte[count];
        Array.Fill(bytes, (byte)7);

        return bytes;
    }


    [Fact]
    public void SameBytes_AreIdentical() {
        Assert.True(FileContentComparer.AreIdentical(With(Bytes(100), Bytes(100)), Left, Right));
    }

    [Fact]
    public void Empty_IsIdenticalToEmpty() {
        Assert.True(FileContentComparer.AreIdentical(With(Array.Empty<byte>(), Array.Empty<byte>()), Left, Right));
    }

    [Fact]
    public void DifferentLength_IsNotIdentical() {
        Assert.False(FileContentComparer.AreIdentical(With(Bytes(100), Bytes(101)), Left, Right));
    }

    [Fact]
    public void OneByteOff_AtTheVeryEnd_IsCaught() {
        var right = Bytes(100);
        right[^1] = 8;

        Assert.False(FileContentComparer.AreIdentical(With(Bytes(100), right), Left, Right));
    }

    [Fact]
    public void FilesLongerThanOneBlock_AreReadToTheEnd() {
        // Two full blocks and a tail: the loop has to keep going past the
        // first block and stop on the short read, not before.
        int length = 2 * Block + 13;
        var right = Bytes(length);
        right[length - 1] = 8;

        Assert.True(FileContentComparer.AreIdentical(With(Bytes(length), Bytes(length)), Left, Right));
        Assert.False(FileContentComparer.AreIdentical(With(Bytes(length), right), Left, Right));
    }

    [Fact]
    public void ExactMultipleOfTheBlock_Terminates() {
        Assert.True(FileContentComparer.AreIdentical(With(Bytes(Block), Bytes(Block)), Left, Right));
    }

    [Fact]
    public void Cancelled_ThrowsInsteadOfAnswering() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => FileContentComparer.AreIdentical(With(Bytes(10), Bytes(10)), Left, Right, cts.Token));
    }
}
