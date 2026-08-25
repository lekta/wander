using System.Text;
using Wander.Core.Companions;

namespace Wander.Core.Tests;

public class UnityMetaSidecarTests {
    private const string TextureMeta =
        "fileFormatVersion: 2\n" +
        "guid: 3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c\n" +
        "TextureImporter:\n" +
        "  internalIDToNameTable: []\n" +
        "  externalObjects: {}\n" +
        "  serializedVersion: 12\n" +
        "  mipmaps:\n" +
        "    mipMapMode: 0\n";

    private const string FolderMeta =
        "fileFormatVersion: 2\n" +
        "guid: aaaabbbbccccddddeeeeffff00001111\n" +
        "folderAsset: yes\n" +
        "DefaultImporter:\n" +
        "  externalObjects: {}\n";


    private static byte[] Utf8(string text) {
        return new UTF8Encoding(false).GetBytes(text);
    }


    [Fact]
    public void Read_ExtractsGuidAndImporter() {
        var info = UnityMetaSidecar.Read(Utf8(TextureMeta));

        Assert.Equal("3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c", info.Guid);
        Assert.Equal("TextureImporter", info.Importer);
        Assert.False(info.IsFolderAsset);
    }

    [Fact]
    public void Read_RecognisesFolderAssets() {
        var info = UnityMetaSidecar.Read(Utf8(FolderMeta));

        Assert.True(info.IsFolderAsset);
        Assert.Equal("DefaultImporter", info.Importer);
    }

    [Fact]
    public void Read_IgnoresNestedKeys() {
        // "mipMapMode" and friends are indented importer settings; only the
        // top level is ours to read.
        var info = UnityMetaSidecar.Read(Utf8("fileFormatVersion: 2\n  guid: nested\n"));

        Assert.Null(info.Guid);
    }

    [Fact]
    public void Read_SurvivesCrLfAndBom() {
        byte[] content = new UTF8Encoding(true).GetPreamble()
            .Concat(Utf8(TextureMeta.Replace("\n", "\r\n"))).ToArray();

        Assert.Equal("3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c", UnityMetaSidecar.Read(content).Guid);
    }

    [Fact]
    public void Read_ReturnsNulls_ForSomethingThatIsNotAMeta() {
        var info = UnityMetaSidecar.Read(Utf8("hello, world\n"));

        Assert.Null(info.Guid);
        Assert.Null(info.Importer);
    }
}
