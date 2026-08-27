using System.Text;
using Wander.Core.Preview;

namespace Wander.Core.Tests;

public class Fb2DocumentTests {
    /// <summary>Three bytes that decode as base64 and stand in for a cover image.</summary>
    private static readonly byte[] _coverBytes = { 0xFF, 0xD8, 0xFF };
    private static readonly string _coverBase64 = Convert.ToBase64String(_coverBytes);


    private static string Book(string body, string? description = null, string? binaries = null) {
        return $@"<?xml version='1.0' encoding='utf-8'?>
<FictionBook xmlns='http://www.gribuser.ru/xml/fictionbook/2.0' xmlns:l='http://www.w3.org/1999/xlink'>
  <description>{description ?? "<title-info><book-title>Название</book-title></title-info>"}</description>
  <body>{body}</body>
  {binaries ?? ""}
</FictionBook>";
    }

    private static Stream Stream(string xml) {
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }


    // --- Read ----------------------------------------------------------

    [Fact]
    public void Read_TakesTitleAndAuthor() {
        string xml = Book(
            "<section><p>Текст</p></section>",
            description: @"<title-info>
                <book-title>Мёртвые души</book-title>
                <author><first-name>Николай</first-name><last-name>Гоголь</last-name></author>
            </title-info>");

        var book = Fb2Document.Read(Stream(xml));

        Assert.NotNull(book);
        Assert.Equal("Мёртвые души", book!.Title);
        Assert.Equal("Николай Гоголь", book.Author);
    }


    [Fact]
    public void Read_ListsEveryAuthor() {
        string xml = Book(
            "<section><p>x</p></section>",
            description: @"<title-info>
                <book-title>b</book-title>
                <author><last-name>Стругацкий</last-name><first-name>Аркадий</first-name></author>
                <author><last-name>Стругацкий</last-name><first-name>Борис</first-name></author>
            </title-info>");

        var book = Fb2Document.Read(Stream(xml));

        // Name parts come out in FB2's own order, not the order they were written.
        Assert.Equal("Аркадий Стругацкий, Борис Стругацкий", book!.Author);
    }


    [Fact]
    public void Read_MapsParagraphsAndEmphasis() {
        var book = Fb2Document.Read(Stream(Book("<section><p>Раз <emphasis>два</emphasis></p></section>")));

        Assert.Contains("<p>Раз <em>два</em></p>", book!.BodyHtml);
    }


    [Fact]
    public void Read_EscapesMarkupInText() {
        var book = Fb2Document.Read(Stream(Book("<section><p>a &lt;b&gt; &amp; c</p></section>")));

        Assert.Contains("a &lt;b&gt; &amp; c", book!.BodyHtml);
        Assert.DoesNotContain("<b>", book.BodyHtml);
    }


    [Fact]
    public void Read_InlinesImagesAsDataUris() {
        string xml = Book(
            "<section><p>x</p><image l:href='#pic.jpg'/></section>",
            binaries: $"<binary id='pic.jpg' content-type='image/jpeg'>{_coverBase64}</binary>");

        var book = Fb2Document.Read(Stream(xml));

        Assert.Contains($"src='data:image/jpeg;base64,{_coverBase64}'", book!.BodyHtml);
    }


    [Fact]
    public void Read_PutsCoverInTheHeader() {
        string xml = Book(
            "<section><p>x</p></section>",
            description: @"<title-info>
                <book-title>b</book-title>
                <coverpage><image l:href='#cover.jpg'/></coverpage>
            </title-info>",
            binaries: $"<binary id='cover.jpg' content-type='image/jpeg'>{_coverBase64}</binary>");

        var book = Fb2Document.Read(Stream(xml));

        Assert.Contains("fb2-cover", book!.BodyHtml);
    }


    /// <summary>
    /// The base64 payload of a binary must never leak into the page as
    /// text — it is megabytes of noise where the story should be.
    /// </summary>
    [Fact]
    public void Read_DoesNotSpillBinaryPayloadIntoTheText() {
        string xml = Book(
            $"<section><p>x</p><binary id='stray'>{_coverBase64}</binary></section>",
            binaries: $"<binary id='pic.jpg'>{_coverBase64}</binary>");

        var book = Fb2Document.Read(Stream(xml));

        Assert.DoesNotContain($">{_coverBase64}<", book!.BodyHtml);
    }


    [Fact]
    public void Read_SkipsTheFootnotesBody() {
        string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<FictionBook xmlns='http://www.gribuser.ru/xml/fictionbook/2.0'>
  <description><title-info><book-title>b</book-title></title-info></description>
  <body><section><p>основной текст</p></section></body>
  <body name='notes'><section><p>сноска</p></section></body>
</FictionBook>";

        var book = Fb2Document.Read(Stream(xml));

        Assert.Contains("основной текст", book!.BodyHtml);
        Assert.DoesNotContain("сноска", book.BodyHtml);
    }


    /// <summary>
    /// FB2 out of a converter that forgot the namespace is still FB2, and
    /// a reader that refuses it is a reader the user has to work around.
    /// </summary>
    [Fact]
    public void Read_AcceptsAFileWithNoNamespace() {
        string xml = @"<?xml version='1.0' encoding='utf-8'?>
<FictionBook>
  <description><title-info><book-title>Без namespace</book-title></title-info></description>
  <body><section><p>текст</p></section></body>
</FictionBook>";

        var book = Fb2Document.Read(Stream(xml));

        Assert.Equal("Без namespace", book!.Title);
    }


    [Fact]
    public void Read_StopsAtTheBudget() {
        string sections = string.Concat(Enumerable.Repeat("<section><p>0123456789</p></section>", 200));

        var book = Fb2Document.Read(Stream(Book(sections)), htmlBudget: 500);

        Assert.True(book!.Truncated);
        Assert.True(book.BodyHtml.Length < 5000);
    }


    /// <summary>
    /// Plenty of books are one <c>&lt;section&gt;</c> from cover to cover. A
    /// budget only checked between top-level children would never fire on
    /// those — which is the whole set of files it exists to protect against.
    /// </summary>
    [Fact]
    public void Read_StopsInsideASingleGiantSection() {
        string paragraphs = string.Concat(Enumerable.Repeat("<p>0123456789</p>", 500));

        var book = Fb2Document.Read(Stream(Book($"<section>{paragraphs}</section>")), htmlBudget: 500);

        Assert.True(book!.Truncated);
        Assert.True(book.BodyHtml.Length < 2000);
    }


    [Fact]
    public void Read_RejectsSomethingThatIsNotAFictionBook() {
        Assert.Null(Fb2Document.Read(Stream("<html><body>hi</body></html>")));
    }


    [Fact]
    public void Read_RejectsBrokenXml() {
        Assert.Null(Fb2Document.Read(Stream("<FictionBook><body>")));
    }


    // --- ReadCover -----------------------------------------------------

    [Fact]
    public void ReadCover_ReturnsTheDeclaredCover() {
        string xml = Book(
            "<section><p>x</p></section>",
            description: @"<title-info>
                <book-title>b</book-title>
                <coverpage><image l:href='#cover.jpg'/></coverpage>
            </title-info>",
            binaries:
                "<binary id='other.jpg'>" + Convert.ToBase64String(new byte[] { 1, 2, 3 }) + "</binary>" +
                $"<binary id='cover.jpg'>{_coverBase64}</binary>");

        Assert.Equal(_coverBytes, Fb2Document.ReadCover(Stream(xml)));
    }


    /// <summary>
    /// No &lt;coverpage&gt;, but the file carries pictures: the first one is
    /// the frontispiece often enough to be a better tile than a grey icon.
    /// </summary>
    [Fact]
    public void ReadCover_FallsBackToTheFirstImage() {
        string xml = Book(
            "<section><p>x</p></section>",
            binaries: $"<binary id='first.jpg'>{_coverBase64}</binary>");

        Assert.Equal(_coverBytes, Fb2Document.ReadCover(Stream(xml)));
    }


    [Fact]
    public void ReadCover_ReturnsNullWhenThereAreNoImages() {
        Assert.Null(Fb2Document.ReadCover(Stream(Book("<section><p>x</p></section>"))));
    }


    [Fact]
    public void ReadCover_ReturnsNullOnBrokenXml() {
        Assert.Null(Fb2Document.ReadCover(Stream("<FictionBook><description>")));
    }
}
