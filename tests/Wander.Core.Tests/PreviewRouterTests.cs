using Wander.Core.Preview;

namespace Wander.Core.Tests;

/// <summary>
/// The routing table of the preview pane. Almost every test here is about
/// an extension that belongs to two lists at once — those are the only
/// cases where the answer depends on the order of the rules rather than on
/// a single list, and they are exactly what a reordering would break
/// silently.
/// </summary>
public class PreviewRouterTests {
    [Theory]
    [InlineData(".png", PreviewRoute.Image)]
    [InlineData(".jpeg", PreviewRoute.Image)]
    [InlineData(".cr3", PreviewRoute.Image)]
    [InlineData(".mp4", PreviewRoute.Video)]
    [InlineData(".flac", PreviewRoute.Audio)]
    [InlineData(".obj", PreviewRoute.Model)]
    [InlineData(".pdf", PreviewRoute.Web)]
    [InlineData(".fb2", PreviewRoute.Book)]
    [InlineData(".rtf", PreviewRoute.Document)]
    [InlineData(".md", PreviewRoute.Markdown)]
    [InlineData(".markdown", PreviewRoute.Markdown)]
    [InlineData(".cs", PreviewRoute.Code)]
    [InlineData(".prefab", PreviewRoute.MaybeText)]
    [InlineData(".txt", PreviewRoute.Text)]
    [InlineData(".lnk", PreviewRoute.Shortcut)]
    [InlineData(".exe", PreviewRoute.Unsupported)]
    [InlineData(".zip", PreviewRoute.Unsupported)]
    public void Extension_DecidesTheRoute(string extension, PreviewRoute expected) {
        Assert.Equal(expected, PreviewRouter.ForExtension(extension));
    }


    /// <summary>
    /// Both are pictures by <c>ImageFormats</c> — the gallery counts them —
    /// and both are multi-frame containers. Animation wins, or an animated
    /// GIF shows its first frame and stops.
    /// </summary>
    [Theory]
    [InlineData(".gif")]
    [InlineData(".webp")]
    public void AnimatedPictures_GoToTheCompositor_NotTheDecoder(string extension) {
        Assert.Equal(PreviewRoute.Animation, PreviewRouter.ForExtension(extension));
    }


    /// <summary>
    /// An SVG is a picture to the listing and a source file to the pane:
    /// there is no raster to decode, and the markup is what a reader wants
    /// to see. It is in the code list, but the image rule is asked first,
    /// so this is the one overlap the order settles the other way.
    /// </summary>
    [Fact]
    public void Svg_IsCode() {
        Assert.DoesNotContain(".svg", (IEnumerable<string>)Wander.Core.Icons.ImageFormats.All);
        Assert.Equal(PreviewRoute.Code, PreviewRouter.ForExtension(".svg"));
    }


    /// <summary>
    /// A <c>.mtl</c> sits beside a model and names its materials — text,
    /// not geometry, and the mesh reader has no rule for it.
    /// </summary>
    [Fact]
    public void MaterialLibrary_IsText_NotModel() {
        Assert.Equal(PreviewRoute.Text, PreviewRouter.ForExtension(".mtl"));
    }


    /// <summary>
    /// A file with no extension is read rather than refused: READMEs and
    /// LICENSEs are the common case.
    /// </summary>
    [Fact]
    public void NoExtension_IsText() {
        Assert.Equal(PreviewRoute.Text, PreviewRouter.ForExtension(""));
        Assert.Equal(PreviewRoute.Text, PreviewRouter.Route(@"C:\src\LICENSE"));
    }


    [Fact]
    public void Extension_IsMatchedRegardlessOfCase() {
        Assert.Equal(PreviewRoute.Image, PreviewRouter.ForExtension(".JPG"));
        Assert.Equal(PreviewRoute.Shortcut, PreviewRouter.Route(@"C:\Users\me\Desktop\App.LNK"));
    }


    [Fact]
    public void Route_TakesTheExtensionOffThePath() {
        Assert.Equal(PreviewRoute.Code, PreviewRouter.Route(@"D:\Dev\Wander\src\App.xaml"));
        Assert.Equal(PreviewRoute.Unsupported, PreviewRouter.Route(@"D:\Downloads\archive.7z"));
    }


    /// <summary>
    /// Whether a file opens as a folder is not something an extension can
    /// answer - a .7z whose association went to 7-Zip does not - so the
    /// caller brings the answer and the table takes it as a fact.
    /// </summary>
    [Fact]
    public void Archive_IsRoutedByTheCallersAnswer_NotByExtension() {
        Assert.Equal(PreviewRoute.Archive, PreviewRouter.Route(@"D:\Downloads\pack.7z", isArchive: true));
        Assert.Equal(PreviewRoute.Unsupported, PreviewRouter.Route(@"D:\Downloads\pack.7z"));
    }

    [Fact]
    public void Archive_WinsOverAnyExtension() {
        // An archive whose name says nothing about it: .nupkg is a zip, and
        // a file with no extension at all can be one too.
        Assert.Equal(PreviewRoute.Archive, PreviewRouter.Route(@"D:\pkg\lib.nupkg", isArchive: true));
        Assert.Equal(PreviewRoute.Archive, PreviewRouter.Route(@"D:\pkg\dump", isArchive: true));
    }
}
