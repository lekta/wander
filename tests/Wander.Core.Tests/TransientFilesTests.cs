using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class TransientFilesTests {

    [Fact]
    public void OurOwnScratchFile_IsTransient() {
        Assert.True(TransientFiles.IsTransient(@"C:\shoot\IMG_1.CR3.pp3.wander-tmp"));
    }

    [Fact]
    public void WindowsReplaceBackup_IsTransient() {
        // File.Replace leaves one of these beside the target for a few
        // milliseconds. Treating it as a real file made every rating written
        // into a sidecar look like a file appearing and vanishing in the
        // folder — which is a full re-listing, which is the folder jumping
        // under the cursor.
        Assert.True(TransientFiles.IsTransient(@"C:\shoot\IMG_1.CR3.pp3~RF55bf7a.TMP"));
        Assert.True(TransientFiles.IsTransient(@"C:\shoot\IMG_1.CR3.pp3~rf55BF7A.tmp"));
    }

    [Fact]
    public void OrdinaryFiles_AreNot() {
        Assert.False(TransientFiles.IsTransient(@"C:\shoot\IMG_1.CR3"));
        Assert.False(TransientFiles.IsTransient(@"C:\shoot\IMG_1.CR3.pp3"));
        Assert.False(TransientFiles.IsTransient(@"C:\shoot\notes.tmp"));
    }

    [Fact]
    public void SomethingThatMerelyLooksThePart_IsNot() {
        // The hex check is what keeps a real file out of the deny-list; a
        // false positive costs one missed refresh of that file.
        Assert.False(TransientFiles.IsTransient(@"C:\shoot\report~RFinal.TMP"));
    }
}
