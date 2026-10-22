using Wander.Core.FileSystem;
using Wander.Core.Listing;
using Wander.Core.Search;

namespace Wander.Core.Tests;

/// <summary>Shooting parameters in the box, written as photographers write them (decision 2026-10-15).</summary>
public class ShotQueryTests {
    private static readonly ShotFacts _portrait = new(Iso: 200, FNumber: 1.8, ExposureSeconds: 1.0 / 250, FocalLengthMm: 85);
    private static readonly ShotFacts _night = new(Iso: 6400, FNumber: 2.79, ExposureSeconds: 0.5, FocalLengthMm: 24);


    [Theory]
    [InlineData("f/2.8", ShotParameter.Aperture)]
    [InlineData("F/2,8", ShotParameter.Aperture)]
    [InlineData("f<4", ShotParameter.Aperture)]
    [InlineData("f/1.4..2.8", ShotParameter.Aperture)]
    [InlineData("iso800", ShotParameter.Iso)]
    [InlineData("ISO>1600", ShotParameter.Iso)]
    [InlineData("iso100-400", ShotParameter.Iso)]
    [InlineData("50mm", ShotParameter.FocalLength)]
    [InlineData("24..70mm", ShotParameter.FocalLength)]
    [InlineData(">200mm", ShotParameter.FocalLength)]
    [InlineData("1/250", ShotParameter.Shutter)]
    [InlineData(">=1/1000", ShotParameter.Shutter)]
    [InlineData("1/60..2s", ShotParameter.Shutter)]
    [InlineData("2s", ShotParameter.Shutter)]
    public void Words_ThatReadAsAParameter_AreOne(string word, ShotParameter parameter) {
        Assert.Equal(parameter, ShotQuery.TryParse(word)?.Parameter);
    }

    [Theory]
    [InlineData("photos")]
    [InlineData("isolated")]
    [InlineData("comm")]
    [InlineData("f")]
    [InlineData("f/")]
    [InlineData("800")]
    [InlineData("/img")]
    [InlineData("a/b")]
    public void OtherWords_StayTheName(string word) {
        Assert.Null(ShotQuery.TryParse(word));
    }


    [Fact]
    public void Extract_TakesTheParametersOut_AndLeavesTheMaskAndType() {
        var (rest, query) = ShotQuery.Extract("отпуск f/2.8 iso>800 /img");

        Assert.Equal("отпуск /img", rest);
        Assert.Equal("f/2.8 iso>800", query.Text);
        Assert.Equal(2, query.Conditions.Count);
    }

    [Fact]
    public void Extract_Quotes_TakeAWordBackToTheName() {
        var (rest, query) = ShotQuery.Extract("\"50mm\" review");

        Assert.Equal("50mm review", rest);
        Assert.True(query.IsEmpty);
    }

    [Fact]
    public void Extract_WithoutParameters_LeavesTheHeadAsItWas() {
        var (remainder, query) = ShotQuery.Extract("my  photo");

        Assert.Equal("my  photo", remainder);
        Assert.True(query.IsEmpty);
    }


    [Theory]
    [InlineData("f/2.8", false, true)]
    [InlineData("f<2", true, false)]
    [InlineData("f/1.4..2.8", true, true)]
    [InlineData("iso>1600", false, true)]
    [InlineData("iso100-400", true, false)]
    [InlineData("85mm", true, false)]
    [InlineData("24..70mm", false, true)]
    [InlineData("1/250", true, false)]
    [InlineData(">=1/30", false, true)]
    [InlineData("f/1.8 85mm", true, false)]
    public void Matches_ComparesWithASlackForHowCamerasRecord(string words, bool portrait, bool night) {
        var query = ShotQuery.Extract(words).Query;

        Assert.Equal(portrait, query.Matches(_portrait));
        Assert.Equal(night, query.Matches(_night));
    }

    [Fact]
    public void Matches_APhotoThatDoesNotSay_IsNoMatch() {
        var query = ShotQuery.Extract("iso800").Query;

        Assert.False(query.Matches(null));
        Assert.False(query.Matches(new ShotFacts(null, 2.8, null, null)));
    }


    [Theory]
    [InlineData("f/2.8 /img", "f/2.8", "img")]
    [InlineData("1/250", "1/250", "")]
    [InlineData("отпуск/vid", "отпуск", "vid")]
    [InlineData("2024/img", "2024", "img")]
    [InlineData("1/2", "1/2", "")]
    public void SplitType_LeavesAParameter_sSlashAlone(string head, string name, string type) {
        Assert.Equal((name, type), SearchExpression.SplitType(head));
    }

    [Theory]
    [InlineData("f/2")]
    [InlineData("f/")]
    [InlineData(">=1/")]
    [InlineData("1/1000..1/250")]
    public void TypeSpan_IsNotOpenedByAParameter(string expression) {
        Assert.Null(SearchExpression.TypeSpan(expression));
    }


    [Fact]
    public async Task Filter_InAFolderOfPhotos_AppliesTheParameters() {
        var a = Photo("a.cr3");
        var b = Photo("b.cr3");
        var facts = new Dictionary<string, ShotFacts> { [a.FullPath] = _portrait, [b.FullPath] = _night };
        var sc = new SearchController { ShotFacts = new ShotFactsCache(path => facts.GetValueOrDefault(path)) };
        sc.SetSource(new[] { a, b, Photo("notes.txt") });

        var next = NextAsync(sc);
        sc.Filter("", TypeFilter.None, Shot("iso>1600"));

        Assert.Equal(new[] { b }, await next);
        Assert.True(sc.IsShotApplied);
    }

    [Fact]
    public async Task Filter_WhereNoPhotoSays_ReadsTheWordsAsTheName() {
        var lens = Photo("50mm review.txt");
        var sc = new SearchController { ShotFacts = new ShotFactsCache(_ => null) };
        sc.SetSource(new[] { lens, Photo("other.txt") });

        var next = NextAsync(sc);
        sc.Filter("review", TypeFilter.None, Shot("50mm review"));

        Assert.Equal(new[] { lens }, await next);
        Assert.False(sc.IsShotApplied);
    }


    [Fact]
    public async Task Replace_APhotoChangedSinceItWasRead_IsReadOnThePoolNotHere() {
        var a = Photo("a.cr3");
        var b = Photo("b.cr3");
        int caller = Environment.CurrentManagedThreadId;
        bool replacing = false;
        bool readHere = false;
        var sc = new SearchController {
            ShotFacts = new ShotFactsCache(path => {
                readHere |= replacing && Environment.CurrentManagedThreadId == caller;

                return path == b.FullPath ? _night : _portrait;
            }),
        };
        sc.SetSource(new[] { a, b });
        var applied = NextAsync(sc);
        sc.Filter("", TypeFilter.None, Shot("iso>1600"));
        Assert.Equal(new[] { b }, await applied);

        // The photo was saved again: its facts are not the ones read.
        var changed = b with { ModifiedUtc = new DateTime(2026, 10, 20) };
        var next = NextAsync(sc);
        replacing = true;
        sc.Replace(new[] { changed });
        replacing = false;

        Assert.Equal(new[] { changed }, await next);
        Assert.False(readHere);
    }


    private static ShotFilter Shot(string head) {
        var query = ShotQuery.Extract(head).Query;

        return new ShotFilter(query, head, TypeFilter.None);
    }

    private static FileSystemEntry Photo(string name) {
        return new FileSystemEntry(name, @"C:\shoot\" + name, EntryKind.File, 1, DateTime.MinValue, false, false, false, false);
    }

    private static async Task<IReadOnlyList<FileSystemEntry>> NextAsync(SearchController sc) {
        var tcs = new TaskCompletionSource<IReadOnlyList<FileSystemEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(IReadOnlyList<FileSystemEntry> rows, bool rearranged) {
            sc.FilteredChanged -= Handler;
            tcs.TrySetResult(rows);
        }
        sc.FilteredChanged += Handler;
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        return winner == tcs.Task ? await tcs.Task : throw new TimeoutException("FilteredChanged did not fire in time");
    }
}
