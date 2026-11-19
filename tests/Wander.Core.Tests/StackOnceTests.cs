using System.Runtime.InteropServices;
using Wander.Core.Logging;

namespace Wander.Core.Tests;

/// <summary>Audit 2026-11-18, L12: a stack once per place and kind of fault; an expected failure, its message only.</summary>
public class StackOnceTests {

    [Fact]
    public void TheFirstFaultOfAKind_GoesWithItsStack_TheNextOnesWithout() {
        var once = new StackOnce();

        string first = once.Describe("FilterTextExtractor", Thrown());
        string second = once.Describe("FilterTextExtractor", Thrown());

        Assert.Contains(nameof(Thrown), first);
        Assert.DoesNotContain(nameof(Thrown), second);
        Assert.Contains("broken filter", second);
    }

    [Fact]
    public void TheSameFaultElsewhere_GoesWithItsStackAgain() {
        var once = new StackOnce();
        once.Describe("FilterTextExtractor", Thrown());

        Assert.Contains(nameof(Thrown), once.Describe("PdfTextExtractor", Thrown()));
    }

    [Fact]
    public void AnExpectedFailure_IsItsMessageOnly() {
        var once = new StackOnce();

        Assert.Equal("held", once.Describe("x", new IOException("held")));
        Assert.Equal("denied", once.Describe("x", new UnauthorizedAccessException("denied")));
        Assert.Equal("com", once.Describe("x", new COMException("com")));
    }

    private static Exception Thrown() {
        try {
            throw new NullReferenceException("broken filter");
        } catch (Exception ex) {
            return ex;
        }
    }
}
