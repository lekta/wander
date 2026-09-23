using Wander.Core.Logging;

namespace Wander.Core.Tests;

public class LogMaskTests {
    private const string Photo = @"C:\Users\Anna\Photos\IMG_0001.jpg";


    [Fact]
    public void APath_KeepsTheDriveTheDepthAndTheExtension_AndNoName() {
        string token = LogMask.Path(Photo);

        Assert.Matches(@"^<C:\\~[0-9a-f]{6}\\~[0-9a-f]{6}\\~[0-9a-f]{6}\\~[0-9a-f]{6}\.jpg>$", token);
        Assert.DoesNotContain("Anna", token);
        Assert.DoesNotContain("IMG_0001", token);
    }

    [Fact]
    public void ThePathIsTheSameToken_WhateverTheCase() {
        Assert.Equal(LogMask.Path(Photo), LogMask.Path(@"c:\users\ANNA\photos\img_0001.JPG"));
        Assert.NotEqual(LogMask.Path(Photo), LogMask.Path(@"C:\Users\Anna\Photos\IMG_0002.jpg"));
    }

    [Fact]
    public void AFileInsideAFolder_SharesTheFolderPart() {
        string folder = LogMask.Path(@"C:\Users\Anna\Photos");
        string file = LogMask.Path(Photo);

        Assert.StartsWith(folder.TrimEnd('>') + @"\", file);
    }

    [Fact]
    public void ABareName_IsMasked_ExtensionKept() {
        Assert.Matches(@"^<~[0-9a-f]{6}\.docx>$", LogMask.Path("Письмо юристу.docx"));
        Assert.Matches(@"^<~[0-9a-f]{6}>$", LogMask.Path("Новая папка"));
    }

    [Fact]
    public void AShare_HasItsServerAndShareMasked() {
        string token = LogMask.Path(@"\\office-nas\anna\report.pdf");

        Assert.Matches(@"^<\\\\~[0-9a-f]{6}\\~[0-9a-f]{6}\\~[0-9a-f]{6}\.pdf>$", token);
    }

    [Fact]
    public void ADriveRoot_AShellLocation_AndATokenStayAsTheyAre() {
        Assert.Equal(@"C:\", LogMask.Path(@"C:\"));
        Assert.Equal("shell:RecycleBinFolder", LogMask.Path("shell:RecycleBinFolder"));

        string token = LogMask.Path(Photo);
        Assert.Equal(token, LogMask.Path(token));
    }

    [Fact]
    public void AnExceptionMessage_KeepsItsWords_AndLosesThePath() {
        string message = @"The process cannot access the file 'C:\Users\Anna\a.txt' because it is being used by another process.";

        string scrubbed = LogMask.Scrub(message);

        Assert.StartsWith("The process cannot access the file '<C:\\", scrubbed);
        Assert.EndsWith(".txt>' because it is being used by another process.", scrubbed);
        Assert.DoesNotContain("Anna", scrubbed);
    }

    [Fact]
    public void AMove_IsSplitAtTheArrow() {
        string scrubbed = LogMask.Scrub(@"C:\Users\Anna\x.jpg -> D:\Backup\x.jpg");

        Assert.Matches(@"^<C:\\[^<>]+\.jpg> -> <D:\\[^<>]+\.jpg>$", scrubbed);
    }

    [Fact]
    public void AListOfPaths_IsMaskedPathByPath() {
        string scrubbed = LogMask.Scrub(@"C:\Users\Anna, D:\Work\Anna");

        Assert.Matches(@"^<C:\\[^<>]+>, <D:\\[^<>]+>$", scrubbed);
    }

    [Fact]
    public void APathEnds_AtTheColonAfterIt() {
        string scrubbed = LogMask.Scrub(@"Extractor failed on C:\Users\Anna\a.pdf: file is damaged");

        Assert.Matches(@"^Extractor failed on <C:\\[^<>]+\.pdf>: file is damaged$", scrubbed);
    }

    [Fact]
    public void AStackFrame_KeepsItsSourcePath() {
        const string frame = @"   at Wander.Core.X.Run() in D:\a\Wander\src\Wander.Core\X.cs:line 12";

        Assert.Equal(frame, LogMask.Scrub(frame));
    }

    [Fact]
    public void AQuotedName_IsMasked_ButAnApostropheIsNoQuote() {
        Assert.Matches(@"^Undo: Move '<~[0-9a-f]{6}\.xmp>'$", LogMask.Scrub("Undo: Move 'IMG_0003.xmp'"));
        Assert.Matches("^Delete: 1 failed - «<~[0-9a-f]{6}>» не помещается в корзину$", LogMask.Scrub("Delete: 1 failed - «long» не помещается в корзину"));
        Assert.Equal("the bin doesn't take it, it's too long", LogMask.Scrub("the bin doesn't take it, it's too long"));
    }

    [Fact]
    public void TextWithoutPaths_IsLeftAlone() {
        Assert.Equal("PERF ui.stall: 8362 ms in 1 calls, worst 8362,0 ms", LogMask.Scrub("PERF ui.stall: 8362 ms in 1 calls, worst 8362,0 ms"));
        Assert.Equal("Help: https://github.com/lekta/wander/issues", LogMask.Scrub("Help: https://github.com/lekta/wander/issues"));
        Assert.Equal(@"First screen painted in 23 ms - C:\", LogMask.Scrub(@"First screen painted in 23 ms - C:\"));
    }

    [Fact]
    public void ScrubbingTwice_ChangesNothingMore() {
        string once = LogMask.Scrub(@"Copy: C:\Users\Anna\x.jpg -> 'D:\Backup\x.jpg' [Ok], «Anna»");

        Assert.Equal(once, LogMask.Scrub(once));
        Assert.DoesNotContain("Anna", once);
    }

    [Fact]
    public void AnInterpolatedLine_HasItsValuesMasked_AndItsWordsKept() {
        var log = new Lines();
        string path = @"C:\Users\Anna\target";
        int count = 3;

        ((ILogger)log).Info($"Paste: copy {count} item(s) into {path} (open folder)");

        Assert.Single(log.Written);
        Assert.Matches(@"^Paste: copy 3 item\(s\) into <C:\\[^<>]+> \(open folder\)$", log.Written[0]);
    }

    [Fact]
    public void TwoInterpolatedPartsJoinedWithPlus_AreStillMaskedValueByValue() {
        var log = new Lines();
        string path = @"C:\Users\Anna\a.docx";
        string holder = "Word (PID 812)";

        ((ILogger)log).Info(
            $"Busy: {path} held by {holder} - " +
            $"still held after {300} ms");

        Assert.Matches(@"^Busy: <C:\\[^<>]+\.docx> held by Word \(PID 812\) - still held after 300 ms$", log.Written[0]);
    }


    private sealed class Lines : ILogger {
        public List<string> Written { get; } = new();

        public void Info(string message) => Written.Add(message);
        public void Warn(string message) => Written.Add(message);
        public void Error(string message, Exception? ex = null) => Written.Add(message);
    }
}
