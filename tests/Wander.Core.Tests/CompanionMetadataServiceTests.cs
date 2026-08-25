using System.Text;
using Wander.Core.Companions;
using Wander.Core.Logging;
using Wander.Core.Tests.Fakes;
using Wander.Core.Undo;

namespace Wander.Core.Tests;

public class CompanionMetadataServiceTests {
    private const string Pp3Path = @"C:\photos\IMG_1234.CR2.pp3";
    private const string Sample = "[General]\nRank=2\nColorLabel=1\n\n[Exposure]\nCompensation=0.35\n";


    private static (CompanionMetadataService Service, FakeFileSystem Fs, UndoService Undo) Build(string? content = Sample) {
        var fs = new FakeFileSystem();
        if (content is not null) {
            fs.Files[Pp3Path] = new UTF8Encoding(false).GetBytes(content);
        }
        var undo = new UndoService();

        return (new CompanionMetadataService(fs, undo, NullLogger.Instance), fs, undo);
    }

    private static string Text(byte[] bytes) {
        return new UTF8Encoding(false).GetString(bytes);
    }


    [Fact]
    public void ReadPp3_ReturnsTheRating() {
        var (service, _, _) = Build();

        var rating = service.ReadPp3(Pp3Path);

        Assert.Equal(2, rating!.Rank);
        Assert.Equal(1, rating.ColorLabel);
    }

    [Fact]
    public void ReadPp3_ReturnsNull_WhenThereIsNoSidecar() {
        var (service, _, _) = Build(content: null);

        Assert.Null(service.ReadPp3(Pp3Path));
    }

    [Fact]
    public void SetPp3Rank_WritesAtomically_AndKeepsTheRest() {
        var (service, fs, _) = Build();

        service.SetPp3Rank(Pp3Path, 5);

        Assert.Contains($"ReplaceAtomic:{Pp3Path}", fs.CallLog);
        Assert.Equal(Sample.Replace("Rank=2", "Rank=5"), Text(fs.Files[Pp3Path]));
    }

    [Fact]
    public void SetPp3Rank_RefusesToCreateTheSidecar() {
        // An empty .pp3 appearing out of nowhere changes how RawTherapee
        // renders the photo — not something a click on a star may do.
        var (service, fs, _) = Build(content: null);

        Assert.Throws<FileNotFoundException>(() => service.SetPp3Rank(Pp3Path, 3));
        Assert.Empty(fs.Files);
    }

    [Fact]
    public void SetPp3Rank_IsUndoable() {
        var (service, fs, undo) = Build();

        service.SetPp3Rank(Pp3Path, 5);
        undo.Undo();

        Assert.Equal(2, Pp3Sidecar.Read(fs.Files[Pp3Path]).Rank);
    }

    [Fact]
    public void SetPp3Rank_Undo_DoesNotGrowTheStack() {
        // The undo write must not push a step of its own, or Ctrl+Z would
        // bounce the rating back and forth forever.
        var (service, _, undo) = Build();

        service.SetPp3Rank(Pp3Path, 5);
        undo.Undo();

        Assert.Equal(0, undo.Depth);
    }
}
