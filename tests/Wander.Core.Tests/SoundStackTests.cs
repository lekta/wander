using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class SoundStackTests {
    private static readonly FileSystemEntry _kick = File("kick.wav");
    private static readonly FileSystemEntry _snare = File("snare.flac");
    private static readonly FileSystemEntry _hat = File("hat.mp3");
    private static readonly FileSystemEntry[] _listing = { _kick, _snare, _hat };


    [Fact]
    public void SeveralSounds_InListingOrder() {
        var stack = SoundStack.Of(new[] { _hat, _kick, _snare }, _listing);

        Assert.Equal(new[] { _kick, _snare, _hat }, stack);
    }

    [Fact]
    public void OneSound_NoStack() {
        Assert.Null(SoundStack.Of(new[] { _kick }, _listing));
    }

    [Fact]
    public void ASoundAndAPicture_NoStack() {
        Assert.Null(SoundStack.Of(new[] { _kick, File("cover.jpg") }, _listing));
    }

    [Fact]
    public void AFolderNamedLikeASound_NoStack() {
        var folder = new FileSystemEntry("loops.wav", @"D:\sfx\loops.wav", EntryKind.Directory, null, DateTime.MinValue, false, false, false, false);

        Assert.Null(SoundStack.Of(new[] { _kick, folder }, _listing));
    }

    [Fact]
    public void ARowNotInTheListing_GoesLast() {
        var elsewhere = File("zap.ogg");

        var stack = SoundStack.Of(new[] { elsewhere, _snare }, _listing);

        Assert.Equal(new[] { _snare, elsewhere }, stack);
    }


    [Fact]
    public void Shares_AgainstTheLongest() {
        var shares = SoundStack.Shares(new TimeSpan?[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(4) });

        Assert.Equal(new[] { 0.25, 1.0, 0.5 }, shares);
    }

    [Fact]
    public void Shares_UnknownLengthTakesTheWholeWidth() {
        Assert.Equal(new[] { 1.0, 1.0 }, SoundStack.Shares(new TimeSpan?[] { TimeSpan.FromSeconds(3), null }));
        Assert.Equal(new[] { 1.0, 1.0 }, SoundStack.Shares(new TimeSpan?[] { null, null }));
    }


    private static FileSystemEntry File(string name) {
        return new FileSystemEntry(name, @"D:\sfx\" + name, EntryKind.File, 1, DateTime.MinValue, false, false, false, false);
    }
}
