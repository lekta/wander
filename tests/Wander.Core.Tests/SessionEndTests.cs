using Wander.Core.Diagnostics;
using Wander.Core.Logging;

namespace Wander.Core.Tests;

public class SessionEndTests {
    [Fact]
    public void TheLine_NamesTheReason_TheCode_AndTheUptime() {
        string line = SessionEnd.Line(SessionEnd.Close, 0, new TimeSpan(1, 2, 3, 4));

        Assert.Equal("Session end: close, exit 0, up 26:03:04", line);
    }

    [Fact]
    public void ALogEndingWithTheLine_EndedInWords() {
        string tail = "12:00:00.000 INFO  State written\r\n"
            + "12:00:00.100 INFO  " + SessionEnd.Line(SessionEnd.Close, 0, TimeSpan.FromMinutes(5)) + "\r\n";

        Assert.True(SessionEnd.IsIn(tail));
    }

    [Fact]
    public void LinesAfterTheEnd_DoNotHideIt() {
        // Finalizers and the domain's handler still write after OnExit.
        string tail = "12:00:00.100 INFO  " + SessionEnd.Line(SessionEnd.ExitCrash, 1, TimeSpan.Zero) + "\r\n"
            + "12:00:00.200 WARN  something on the way out\r\n";

        Assert.True(SessionEnd.IsIn(tail));
    }

    [Fact]
    public void ALogThatStopsMidSession_DidNotEndInWords() {
        string tail = "12:00:00.000 INFO  WS WindowDeactivated\r\n"
            + "12:00:05.000 INFO  SYS cpu 1%\r\n";

        Assert.False(SessionEnd.IsIn(tail));
    }

    [Fact]
    public void TheWordsInsideAnotherLine_AreNotTheEnd() {
        string tail = "12:00:00.000 INFO  Opened D:\\notes\\Session end: draft.txt\r\n";

        Assert.False(SessionEnd.IsIn(tail));
    }

    [Theory]
    [InlineData("session-20261119-120000-4242.log", 4242)]
    [InlineData("SESSION-20261119-120000-7.LOG", 7)]
    [InlineData("session-20261119-120000.log", null)]
    [InlineData("journal-20261119-120000-4242.txt", null)]
    [InlineData("layout-trace.log", null)]
    public void ThePid_IsTheLastPartOfTheName(string fileName, int? pid) {
        Assert.Equal(pid, SessionEnd.PidOf(fileName));
    }


    // --- A fault the dispatcher did not catch --------------------------

    [Fact]
    public void AFaultInTheSession_IsOffered_AndTheWindowGoesOn() {
        Assert.Equal(UiFaultAnswer.Offer, UiFault.Answer(appEnding: false, dispatcherEnding: false, headless: false));
    }

    [Fact]
    public void AFaultWithNobodyThere_ShutsTheSessionDown() {
        Assert.Equal(UiFaultAnswer.ShutDown, UiFault.Answer(appEnding: false, dispatcherEnding: false, headless: true));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void AFaultOnTheWayOut_LeavesAtOnce_RatherThanStayingAGhost(bool appEnding, bool dispatcherEnding, bool headless) {
        Assert.Equal(UiFaultAnswer.Exit, UiFault.Answer(appEnding, dispatcherEnding, headless));
    }
}
