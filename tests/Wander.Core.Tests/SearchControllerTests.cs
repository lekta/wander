using System.ComponentModel;
using Wander.Core.Companions;
using Wander.Core.FileSystem;

namespace Wander.Core.Tests;

public class SearchControllerTests {
    // --- Test fixture entries ----------------------------------------
    // Mixed-case names so the case-insensitive contract gets exercised.
    private static FileSystemEntry Entry(string name) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\folder\" + name,
            Kind: EntryKind.File,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false);
    }

    private static readonly FileSystemEntry _apple = Entry("apple.txt");
    private static readonly FileSystemEntry _banana = Entry("BANANA.txt");
    private static readonly FileSystemEntry _pineapple = Entry("Pineapple.txt");
    private static readonly FileSystemEntry _pear = Entry("pear.md");


    /// <summary>
    /// Subscribe and await the next <c>FilteredChanged</c> emission. Times
    /// out to keep a misbehaving controller from hanging the test runner.
    /// </summary>
    private static async Task<IReadOnlyList<FileSystemEntry>> WaitForNextFilteredAsync(SearchController sc, TimeSpan? timeout = null) {
        var tcs = new TaskCompletionSource<IReadOnlyList<FileSystemEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(IReadOnlyList<FileSystemEntry> result) {
            sc.FilteredChanged -= Handler;
            tcs.TrySetResult(result);
        }
        sc.FilteredChanged += Handler;
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout ?? TimeSpan.FromSeconds(2)));
        if (winner != tcs.Task) {
            sc.FilteredChanged -= Handler;
            throw new TimeoutException("FilteredChanged did not fire in time");
        }
        return await tcs.Task;
    }


    // --- Basic projection --------------------------------------------

    [Fact]
    public async Task SetSource_EmptyQuery_PublishesEverything() {
        var sc = new SearchController();
        var next = WaitForNextFilteredAsync(sc);

        sc.SetSource(new[] { _apple, _banana, _pineapple });

        var result = await next;
        Assert.Equal(new[] { _apple, _banana, _pineapple }, result);
    }

    [Fact]
    public async Task Query_FiltersByCaseInsensitiveContains() {
        var sc = new SearchController();
        sc.SetSource(new[] { _apple, _banana, _pineapple, _pear });

        var next = WaitForNextFilteredAsync(sc);
        sc.Query = "apple";

        var result = await next;
        Assert.Equal(new[] { _apple, _pineapple }, result);
    }

    [Fact]
    public async Task Query_NoMatches_PublishesEmptyList() {
        var sc = new SearchController();
        sc.SetSource(new[] { _apple, _banana });

        var next = WaitForNextFilteredAsync(sc);
        sc.Query = "xyz";

        var result = await next;
        Assert.Empty(result);
    }

    [Fact]
    public async Task ClearingQuery_RepublishesEverything() {
        var sc = new SearchController();
        // Subscribe BEFORE SetSource so the synchronous empty-query push
        // can't slip past us into the void.
        var initial = WaitForNextFilteredAsync(sc);
        sc.SetSource(new[] { _apple, _banana });
        await initial;

        var withFilter = WaitForNextFilteredAsync(sc);
        sc.Query = "apple";
        await withFilter;

        var cleared = WaitForNextFilteredAsync(sc);
        sc.Query = "";
        var result = await cleared;
        Assert.Equal(new[] { _apple, _banana }, result);
    }


    // --- HasQuery / PropertyChanged ----------------------------------

    [Fact]
    public void HasQuery_FlipsWithQuery() {
        var sc = new SearchController();
        Assert.False(sc.HasQuery);

        sc.Query = "x";
        Assert.True(sc.HasQuery);

        sc.Query = "";
        Assert.False(sc.HasQuery);
    }

    [Fact]
    public void Query_Setter_RaisesPropertyChanged() {
        var sc = new SearchController();
        var changes = new List<string?>();
        sc.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        sc.Query = "x";

        Assert.Contains(nameof(SearchController.Query), changes);
        Assert.Contains(nameof(SearchController.HasQuery), changes);
    }

    [Fact]
    public void Query_SameValue_DoesNotRaisePropertyChanged() {
        var sc = new SearchController();
        sc.Query = "x";
        int fired = 0;
        sc.PropertyChanged += (_, _) => fired++;

        sc.Query = "x";

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Query_NullValue_NormalisedToEmpty() {
        var sc = new SearchController();
        sc.Query = null!;
        Assert.Equal("", sc.Query);
        Assert.False(sc.HasQuery);
    }


    // --- Reset --------------------------------------------------------

    [Fact]
    public async Task Reset_ClearsQuery_WithoutFiringFilteredAgain() {
        var sc = new SearchController();
        sc.SetSource(new[] { _apple });
        // Let the "apple" pass land before counting: subscribing while it is
        // still in flight measured the race, not Reset. Under load it drained
        // first and the test went green; run alone it always fired once.
        var settled = WaitForNextFilteredAsync(sc);
        sc.Query = "apple";
        await settled;

        int filteredFires = 0;
        sc.FilteredChanged += _ => filteredFires++;

        sc.Reset();

        Assert.Equal("", sc.Query);
        Assert.False(sc.HasQuery);
        // Reset is the "folder is about to change, don't fire" path —
        // it cancels in-flight work and does NOT publish a new projection.
        Assert.Equal(0, filteredFires);
    }

    [Fact]
    public void Reset_OnAlreadyEmptyQuery_IsIdempotent() {
        var sc = new SearchController();
        var ex = Record.Exception(() => sc.Reset());
        Assert.Null(ex);
        Assert.Equal("", sc.Query);
    }


    // --- Source rotation ---------------------------------------------

    [Fact]
    public async Task SetSource_WithActiveQuery_RefiltersAgainstNewSource() {
        var sc = new SearchController();
        sc.SetSource(new[] { _apple, _banana });
        // The query setter races the SetSource above on the thread pool;
        // wait for the "apple" filter to settle before we rotate the source.
        var firstApple = WaitForNextFilteredAsync(sc);
        sc.Query = "apple";
        await firstApple;

        // Now hand in a new source; the active query reapplies.
        var next = WaitForNextFilteredAsync(sc);
        sc.SetSource(new[] { _pineapple, _pear });
        var result = await next;

        Assert.Equal(new[] { _pineapple }, result);
    }

    [Fact]
    public async Task RapidQueryChanges_OnlyLatestSurvives() {
        // A common keystroke race: type quickly, expect the final filter to
        // win. Earlier filters may be cancelled silently.
        var sc = new SearchController();
        var initial = WaitForNextFilteredAsync(sc);
        sc.SetSource(new[] { _apple, _banana, _pineapple, _pear });
        await initial;

        var lastResults = new List<IReadOnlyList<FileSystemEntry>>();
        sc.FilteredChanged += r => lastResults.Add(r);

        sc.Query = "a";
        sc.Query = "ap";
        sc.Query = "app";
        sc.Query = "appl";
        sc.Query = "apple";

        // Wait for the queue to drain; 500ms is way more than enough for
        // pure in-memory filtering on 4 items.
        await Task.Delay(500);

        // We never demand that exactly one fired, but the LAST one observed
        // must be the "apple" projection.
        Assert.NotEmpty(lastResults);
        var final = lastResults.Last();
        Assert.Equal(new[] { _apple, _pineapple }, final);
    }


    // --- Rating filter ------------------------------------------------

    private static FileSystemEntry Photo(string name, int? rank = null, int? color = null, EntryKind kind = EntryKind.File) {
        return new FileSystemEntry(
            Name: name,
            FullPath: @"C:\folder\" + name,
            Kind: kind,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            IsHidden: false,
            IsReadOnly: false,
            IsSystem: false,
            LinksToDirectory: false,
            Rating: rank is null && color is null ? null : new SidecarRating(rank, color));
    }


    [Fact]
    public async Task RatingFilter_KeepsWhatReachesTheThreshold() {
        var keep = Photo("keep.jpg", rank: 4);
        var drop = Photo("drop.jpg", rank: 1);
        var unrated = Photo("unrated.jpg");
        var sc = new SearchController();
        sc.SetSource(new[] { keep, drop, unrated });

        var next = WaitForNextFilteredAsync(sc);
        sc.RatingFilter = new RatingFilter(3, null);

        Assert.Equal(new[] { keep }, await next);
    }

    [Fact]
    public async Task RatingFilter_KeepsFoldersRegardless() {
        // Hiding the way back out of a folder of three-star photos is not
        // what "show me three stars" asked for.
        var folder = Photo("selects", kind: EntryKind.Directory);
        var photo = Photo("a.jpg", rank: 1);
        var sc = new SearchController();
        sc.SetSource(new[] { folder, photo });

        var next = WaitForNextFilteredAsync(sc);
        sc.RatingFilter = new RatingFilter(3, null);

        Assert.Equal(new[] { folder }, await next);
    }

    [Fact]
    public async Task RatingFilter_AndTheNameFilter_BothApply() {
        var match = Photo("beach-1.jpg", rank: 5);
        var wrongName = Photo("forest-1.jpg", rank: 5);
        var wrongRank = Photo("beach-2.jpg", rank: 1);
        var sc = new SearchController();
        sc.SetSource(new[] { match, wrongName, wrongRank });
        sc.Query = "beach";

        var next = WaitForNextFilteredAsync(sc);
        sc.RatingFilter = new RatingFilter(3, null);

        Assert.Equal(new[] { match }, await next);
    }

    [Fact]
    public async Task RatingFilter_ClearedBackToNone_PublishesEverythingAgain() {
        var sc = new SearchController();
        sc.SetSource(new[] { Photo("a.jpg", rank: 5), Photo("b.jpg") });
        sc.RatingFilter = new RatingFilter(3, null);

        var next = WaitForNextFilteredAsync(sc);
        sc.RatingFilter = RatingFilter.None;

        Assert.Equal(2, (await next).Count);
        Assert.False(sc.HasRatingFilter);
    }

    [Fact]
    public void Reset_ClearsTheRatingFilterToo() {
        // Navigation calls Reset; a filter that survived the move would hide
        // rows in a folder the user has not looked at yet.
        var sc = new SearchController();
        sc.RatingFilter = new RatingFilter(4, 2);

        sc.Reset();

        Assert.Equal(RatingFilter.None, sc.RatingFilter);
        Assert.False(sc.HasRatingFilter);
    }

    [Fact]
    public void RatingFilter_RaisesItsProperties() {
        var sc = new SearchController();
        var seen = new List<string?>();
        sc.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        sc.RatingFilter = new RatingFilter(2, null);

        Assert.Contains(nameof(SearchController.RatingFilter), seen);
        Assert.Contains(nameof(SearchController.HasRatingFilter), seen);
    }
}
