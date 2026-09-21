using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class SharedReadTests : IDisposable {
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wander-shared-read-tests", Guid.NewGuid().ToString("N"));


    public SharedReadTests() {
        Directory.CreateDirectory(_dir);
    }


    public void Dispose() {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AFileOpenHere_CanStillBeRenamedAndDeleted_AndTheReadGoesOn() {
        // The point of the whole class: a preview reading the file must not
        // make the user's delete fail "in use" by Wander.
        string path = Path.Combine(_dir, "a.txt");
        File.WriteAllText(path, "hello");

        using var reader = SharedRead.Open(path);
        string renamed = Path.Combine(_dir, "b.txt");
        File.Move(path, renamed);
        File.Delete(renamed);

        Assert.False(File.Exists(renamed));
        Assert.Equal('h', reader.ReadByte());
    }

    [Fact]
    public void ReadAllBytes_ReadsAFileAnotherProgramIsWriting() {
        string path = Path.Combine(_dir, "log.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(new byte[] { 1, 2, 3 }, SharedRead.ReadAllBytes(path));
    }

    [Fact]
    public void ReadAllText_AndReadLines_ReadWhatFileWould() {
        string path = Path.Combine(_dir, "mesh.mtl");
        File.WriteAllText(path, "\uFEFFnewmtl red\r\nKd 1 0 0\n");

        Assert.Equal(File.ReadAllText(path), SharedRead.ReadAllText(path));
        Assert.Equal(File.ReadLines(path), SharedRead.ReadLines(path));
    }
}
