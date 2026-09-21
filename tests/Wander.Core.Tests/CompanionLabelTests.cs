using Wander.Core.Companions;

namespace Wander.Core.Tests;

public class CompanionLabelTests {

    [Fact]
    public void NoCompanions_NoLabel() {
        Assert.Equal("", CompanionLabel.For("IMG_2784.CR3", null));
        Assert.Equal("", CompanionLabel.For("IMG_2784.CR3", Array.Empty<string>()));
    }

    [Fact]
    public void SidecarNamedAfterTheStem_AddsItsExtension() {
        Assert.Equal("(+.xmp)", CompanionLabel.For("IMG_2784.CR3", new[] { @"D:\Photos\IMG_2784.xmp" }));
    }

    [Fact]
    public void SidecarNamedAfterTheWholeName_AddsWhatFollowsIt() {
        Assert.Equal("(+.pp3)", CompanionLabel.For("IMG.CR2", new[] { @"D:\Photos\IMG.CR2.pp3" }));
        Assert.Equal("(+.out.pp3)", CompanionLabel.For("IMG_3157_rt.jpg", new[] { "IMG_3157_rt.jpg.out.pp3" }));
        Assert.Equal("(+.meta)", CompanionLabel.For("Sprite.png", new[] { "Sprite.png.meta" }));
    }

    [Fact]
    public void Several_AreListedOnce_InOrder() {
        Assert.Equal(
            "(+.xmp, .pp3)",
            CompanionLabel.For("IMG.CR2", new[] { "IMG.xmp", "IMG.CR2.pp3", "img.XMP" }));
    }

    [Fact]
    public void NamedOtherwise_FallsBackToTheExtension_OrTheName() {
        Assert.Equal("(+.json)", CompanionLabel.For("a.png", new[] { "b.json" }));
        Assert.Equal("(+notes)", CompanionLabel.For("a.png", new[] { "notes" }));
    }
}
