using Wander.Core.Navigation;

namespace Wander.Core.Tests;

public class StartFolderTests {
    [Fact]
    public void FromArguments_ReadsBothForms_AsAFullPath() {
        Assert.Equal(@"D:\test\photos", StartFolder.FromArguments(new[] { "--smoke", "--folder", @"D:\test\photos" }));
        Assert.Equal(@"D:\test\photos", StartFolder.FromArguments(new[] { @"--FOLDER=D:\test\photos", "--smoke" }));
        Assert.Equal(@"D:\test\photos", StartFolder.FromArguments(new[] { "--folder", @"D:\test\sub\..\photos" }));
    }

    [Fact]
    public void FromArguments_DropsTheQuoteAShellLeft() {
        // cmd reads \" as a quote: "D:\test\" arrives as D:\test"
        Assert.Equal(@"D:\test", StartFolder.FromArguments(new[] { "--folder", "D:\\test\"" }));
    }

    [Fact]
    public void FromArguments_NamesNothing_WithoutAPath() {
        Assert.Null(StartFolder.FromArguments(Array.Empty<string>()));
        Assert.Null(StartFolder.FromArguments(new[] { "--smoke", "--data-dir", @"D:\data" }));
        Assert.Null(StartFolder.FromArguments(new[] { "--folder" }));
        Assert.Null(StartFolder.FromArguments(new[] { "--folder", "--smoke" }));
        Assert.Null(StartFolder.FromArguments(new[] { "--folder=" }));
    }

    [Fact]
    public void Resolve_KeepsTheFolderNamedByHand_UntilTheCommandLineNamesOne() {
        try {
            StartFolder.Override(@"D:\sandbox");

            // The harness's own command line, as the application sees it.
            StartFolder.Resolve(new[] { "run", "smoke-walk.json", "--rebuild" });
            Assert.Equal(@"D:\sandbox", StartFolder.Asked);

            StartFolder.Resolve(new[] { "--folder", @"D:\other" });
            Assert.Equal(@"D:\other", StartFolder.Asked);
        } finally {
            StartFolder.Override(null);
        }
    }
}
