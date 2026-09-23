using Wander.Core.Folders;

namespace Wander.Core.Tests;

public class ViewChoiceTests {

    [Fact]
    public void PinnedView_WinsOverEverything() {
        var decision = ViewChoice.Decide(
            pinned: ViewMode.Tiles, autoGallery: true, inRecycleBin: false,
            looksLikePictures: () => true, defaultMode: ViewMode.Details);

        Assert.Equal(new ViewDecision(ViewMode.Tiles, ViewReason.Pinned), decision);
    }

    [Fact]
    public void PicturesWithAutoGallery_GiveTheGallery() {
        var decision = ViewChoice.Decide(null, autoGallery: true, inRecycleBin: false, () => true, ViewMode.Details);

        Assert.Equal(new ViewDecision(ViewMode.Gallery, ViewReason.Pictures), decision);
    }

    [Fact]
    public void PicturesWithAutoGalleryOff_GiveTheDefault() {
        // Off means off: the setting used to leave the previous folder's
        // view on screen instead (REDESIGN, finding H13).
        var decision = ViewChoice.Decide(null, autoGallery: false, inRecycleBin: false, () => true, ViewMode.LargeIcons);

        Assert.Equal(new ViewDecision(ViewMode.LargeIcons, ViewReason.Default), decision);
    }

    [Fact]
    public void NoPictures_GiveTheDefault() {
        var decision = ViewChoice.Decide(null, autoGallery: true, inRecycleBin: false, () => false, ViewMode.LargeIcons);

        Assert.Equal(new ViewDecision(ViewMode.LargeIcons, ViewReason.Default), decision);
    }

    [Fact]
    public void RecycleBin_NeverBecomesAGallery() {
        var decision = ViewChoice.Decide(null, autoGallery: true, inRecycleBin: true, () => true, ViewMode.Details);

        Assert.Equal(new ViewDecision(ViewMode.Details, ViewReason.Default), decision);
    }

    [Fact]
    public void RecycleBin_StillHonoursAPin() {
        var decision = ViewChoice.Decide(ViewMode.Gallery, autoGallery: false, inRecycleBin: true, () => false, ViewMode.Details);

        Assert.Equal(new ViewDecision(ViewMode.Gallery, ViewReason.Pinned), decision);
    }

    [Fact]
    public void ThePictureProbe_IsNotRunWhenItCannotMatter() {
        // It costs a pass over the listing; a pinned folder and a switched
        // off gallery must not pay for it.
        int probes = 0;
        bool Probe() {
            probes++;
            return true;
        }

        ViewChoice.Decide(ViewMode.Tiles, autoGallery: true, inRecycleBin: false, Probe, ViewMode.Details);
        ViewChoice.Decide(null, autoGallery: false, inRecycleBin: false, Probe, ViewMode.Details);
        ViewChoice.Decide(null, autoGallery: true, inRecycleBin: true, Probe, ViewMode.Details);
        ViewChoice.Decide(null, autoGallery: true, inRecycleBin: false, Probe, ViewMode.Details, picturesHint: true);

        Assert.Equal(0, probes);
    }

    /// <summary>H1: desktop.ini says "pictures" - the gallery, with no pictures to count yet.</summary>
    [Fact]
    public void APicturesHint_MakesAGallery() {
        var decision = ViewChoice.Decide(null, autoGallery: true, inRecycleBin: false, () => false, ViewMode.Details, picturesHint: true);

        Assert.Equal(new ViewDecision(ViewMode.Gallery, ViewReason.Pictures), decision);
    }

    /// <summary>H1: the hint is only a hint - the setting, the bin and a pin still decide.</summary>
    [Fact]
    public void APicturesHint_DefersToTheSettingTheBinAndAPin() {
        Assert.Equal(ViewReason.Default,
            ViewChoice.Decide(null, autoGallery: false, inRecycleBin: false, () => false, ViewMode.Details, picturesHint: true).Reason);
        Assert.Equal(ViewReason.Default,
            ViewChoice.Decide(null, autoGallery: true, inRecycleBin: true, () => false, ViewMode.Details, picturesHint: true).Reason);
        Assert.Equal(new ViewDecision(ViewMode.Tiles, ViewReason.Pinned),
            ViewChoice.Decide(ViewMode.Tiles, autoGallery: true, inRecycleBin: false, () => false, ViewMode.Details, picturesHint: true));
    }
}
