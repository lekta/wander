using Wander.Core.Actions;
using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class DeleteConfirmationTests {
    private static readonly FakeTextSource _text = new(new Dictionary<string, string> {
        ["ConfirmRecycleTitle"] = "Отправить в корзину?",
        ["ConfirmDeleteTitle"] = "Удалить безвозвратно?",
        ["RecycleUnavailableTitle"] = "Не помещается в корзину",
        ["ConfirmRecycleOne"] = "Отправить в корзину {0} «{1}»?",
        ["ConfirmDeleteOne"] = "Удалить безвозвратно {0} «{1}»?",
        ["RecycleUnavailableOne"] = "Корзина не принимает {0} «{1}»",
        ["ConfirmRecycleMany"] = "Отправить в корзину {0}?",
        ["ConfirmDeleteMany"] = "Удалить безвозвратно {0}?",
        ["RecycleUnavailableMany"] = "Корзина не принимает {0}",
        ["KindFolder"] = "папку",
        ["KindFile"] = "файл",
        ["ConfirmIrreversible"] = "Это действие нельзя отменить.",
        ["RecycleUnavailableIrreversible"] = "Удалить безвозвратно?",
        ["RecycleUnavailableTooLong"] = "Путь длиннее 259 знаков.",
        ["RecycleUnavailableNoBin"] = "У этого диска нет корзины.",
        ["RecycleUnavailableRestRecycled"] = "Остальные {0} — уже в корзине.",
        ["ConfirmDeleteReadOnly"] = "Только для чтения: {0}",
        ["ConfirmDeleteTotal"] = "Всего: {0}",
        ["ConfirmDeleteTotalCounting"] = "Всего: {0} — считается",
        ["ConfirmDeleteInside"] = "Внутри: {0}",
        ["ConfirmDeleteCounting"] = "считается",
        ["ConfirmDeleteEmpty"] = "пусто",
        ["ConfirmDeleteUnreadable"] = "нет доступа",
        ["ConfirmDeleteAmount"] = "{0} · {1}",
        ["ConfirmDeleteFilesAndFolders"] = "{0}, {1}",
        ["MenuCaptionFileCount"] = "{0:N0} файл|{0:N0} файла|{0:N0} файлов",
        ["MenuCaptionFolderCount"] = "{0:N0} папка|{0:N0} папки|{0:N0} папок",
        [CountCaption.ItemsKey] = "{0} элемент|{0} элемента|{0} элементов",
        [CountCaption.FilesKey] = "{0} файл|{0} файла|{0} файлов",
        [CountCaption.ExtensionKey] = "{0} {1}",
    });


    [Fact]
    public void Rows_AreFoldersFirst_ThenFiles_EachInTheOrderGiven() {
        var fs = Disk();
        var question = Ask(fs, File(fs, "b.txt", 1), Dir(fs, "z"), File(fs, "a.txt", 1), Dir(fs, "y"));

        Assert.Equal(new[] { "z", "y", "b.txt", "a.txt" }, question.Rows.Select(r => r.Name));
        Assert.Same(question.Rows, question.TableRows);
        Assert.True(question.ShowsHeader);
        Assert.True(question.ShowsInside);
    }

    [Fact]
    public void OneFile_IsDescribedInWords_ByName_Path_AndSize() {
        var fs = Disk();
        var question = Ask(fs, File(fs, "IMG_7483.CR3", 300));

        Assert.True(question.IsSingle);
        Assert.Empty(question.TableRows);
        Assert.False(question.ShowsInside);
        Assert.False(question.IsCounting);
        Assert.Null(question.Warning);
        Assert.Equal("Отправить в корзину?", question.Title);
        Assert.Equal("Отправить в корзину файл «IMG_7483.CR3»?", question.Headline);
        Assert.Equal(@"C:\x\IMG_7483.CR3", question.Path);
        Assert.Equal("300 B", question.Summary(Bytes));
    }

    [Fact]
    public async Task OneFolder_SaysWhatIsInside_OnceCounted() {
        var fs = Disk();
        var folder = Dir(fs, "Отпуск");
        fs.Directories.Add(@"C:\x\Отпуск\day1");
        fs.Directories.Add(@"C:\x\Отпуск\day2");
        fs.Files[@"C:\x\Отпуск\day1\a.jpg"] = new byte[100];
        fs.Files[@"C:\x\Отпуск\day2\b.jpg"] = new byte[50];
        var question = Ask(fs, DeleteQuestionKind.Permanent, folder);

        Assert.Equal("Удалить безвозвратно папку «Отпуск»?", question.Headline);
        Assert.Equal("Это действие нельзя отменить.", question.Warning);
        Assert.True(question.IsCounting);
        Assert.Equal("Внутри: считается", question.Summary(Bytes));

        await question.CountAsync(_ => { }, CancellationToken.None);

        Assert.False(question.IsCounting);
        Assert.Equal("Внутри: 2 файла, 2 папки · 150 B", question.Summary(Bytes));
    }

    [Fact]
    public async Task EmptyFolder_IsEmpty_AndHasNoSize() {
        var fs = Disk();
        var question = Ask(fs, Dir(fs, "export"));

        await question.CountAsync(_ => { }, CancellationToken.None);

        Assert.Equal("Внутри: пусто", question.Summary(Bytes));
        Assert.Equal("пусто", question.InsideOf(question.Rows[0]));
        Assert.Null(question.Rows[0].Size);
    }

    [Fact]
    public async Task UnreadableFolder_SaysSo_InsteadOfZeros() {
        var fs = Disk();
        var folder = Dir(fs, "locked");
        fs.EnumerateFailures.Add(folder.FullPath);
        var question = Ask(fs, folder, File(fs, "a.txt", 10));

        await question.CountAsync(_ => { }, CancellationToken.None);

        Assert.Equal("нет доступа", question.InsideOf(question.Rows[0]));
        Assert.Null(question.Rows[0].Size);
        Assert.False(question.Rows[0].IsCounting);
    }

    [Fact]
    public async Task SeveralItems_AreTotalled_InsidesOfFoldersIncluded() {
        var fs = Disk();
        var folder = Dir(fs, "docs");
        fs.Directories.Add(@"C:\x\docs\old");
        fs.Files[@"C:\x\docs\old\c.txt"] = new byte[5];
        fs.Files[@"C:\x\docs\d.txt"] = new byte[7];
        var question = Ask(fs, folder, File(fs, "a.txt", 10), File(fs, "b.txt", 20));

        Assert.Equal("Отправить в корзину 3 элемента?", question.Headline);
        Assert.Null(question.Path);
        Assert.Equal("Всего: 2 файла, 1 папка · 30 B — считается", question.Summary(Bytes));
        Assert.Equal("", question.InsideOf(question.Rows[0]));
        Assert.True(question.Rows[0].IsCounting);

        var told = new List<DeleteRow?>();
        await question.CountAsync(told.Add, CancellationToken.None);

        Assert.Equal(new DeleteRow?[] { question.Rows[0] }, told);
        Assert.Equal("2 файла, 1 папка", question.InsideOf(question.Rows[0]));
        Assert.Equal(12L, question.Rows[0].Size);
        Assert.Equal("Всего: 4 файла, 2 папки · 42 B", question.Summary(Bytes));
    }

    [Fact]
    public void FilesOnly_HaveNoInsideColumn_AndNoFolderCount() {
        var fs = Disk();
        var question = Ask(fs, File(fs, "a.mp4", 10), File(fs, "b.mp4", 20));

        Assert.False(question.ShowsInside);
        Assert.Equal("Отправить в корзину 2 MP4?", question.Headline);
        Assert.Equal("Всего: 2 файла · 30 B", question.Summary(Bytes));
    }

    [Fact]
    public async Task Companions_FollowTheirFile_OnRowsOfTheirOwn_AndGoIntoTheTotals() {
        var fs = Disk();
        fs.Files[@"C:\x\a.cr3.xmp"] = new byte[3];
        fs.Files[@"C:\x\b.cr3.xmp"] = new byte[4];
        var a = File(fs, "a.cr3", 100) with { Companions = new[] { @"C:\x\a.cr3.xmp" } };
        var b = File(fs, "b.cr3", 200) with { Companions = new[] { @"C:\x\b.cr3.xmp", @"C:\x\b.cr3.gone" } };
        var question = Ask(fs, a, b);

        Assert.Equal(new[] { "a.cr3", "a.cr3.xmp", "b.cr3", "b.cr3.xmp", "b.cr3.gone" }, question.Rows.Select(r => r.Name));
        Assert.Equal(3, question.Companions);
        Assert.True(question.Rows[1].IsCompanion);
        Assert.True(question.Rows[1].IsCounting);
        Assert.Null(question.Rows[1].Entry);

        var told = new List<DeleteRow?>();
        await question.CountAsync(told.Add, CancellationToken.None);

        Assert.Equal(new DeleteRow?[] { null }, told);
        Assert.Equal(3L, question.Rows[1].Size);
        Assert.Null(question.Rows[4].Size);
        Assert.False(question.IsCounting);
        Assert.Equal("Всего: 5 файлов · 307 B", question.Summary(Bytes));
    }

    [Fact]
    public void OneFile_WithCompanions_TablesOnlyTheCompanions() {
        var fs = Disk();
        var a = File(fs, "a.cr3", 100) with { Companions = new[] { @"C:\x\a.cr3.xmp" } };
        var question = Ask(fs, a);

        Assert.True(question.IsSingle);
        Assert.Equal(new[] { "a.cr3.xmp" }, question.TableRows.Select(r => r.Name));
        Assert.False(question.ShowsHeader);
        Assert.Equal("Отправить в корзину файл «a.cr3»?", question.Headline);
        Assert.Equal("100 B", question.Summary(Bytes));
    }

    [Fact]
    public void Companions_StayBehind_WhenTheSettingIsOff() {
        var fs = Disk();
        var a = File(fs, "a.cr3", 100) with { Companions = new[] { @"C:\x\a.cr3.xmp" } };
        var question = new DeleteConfirmation(new[] { a }, DeleteQuestionKind.Recycle, withCompanions: false, fs, _text);

        Assert.Equal(0, question.Companions);
        Assert.Single(question.Rows);
        Assert.False(question.IsCounting);
    }

    [Fact]
    public void ReadOnlyItems_AreCounted_AndSaidInOneLine() {
        var fs = Disk();
        var question = Ask(fs, File(fs, "a.txt", 1) with { IsReadOnly = true }, File(fs, "b.txt", 1), File(fs, "c.txt", 1) with { IsReadOnly = true });

        Assert.Equal(2, question.ReadOnly);
        Assert.Equal("Только для чтения: 2", question.ReadOnlyLine);
        Assert.True(question.Rows[0].IsReadOnly);
        Assert.False(question.Rows[1].IsReadOnly);
        Assert.Null(Ask(fs, File(fs, "d.txt", 1)).ReadOnlyLine);
    }

    [Fact]
    public void Refused_NamesWhy_WhatIsInTheBinAlready_AndThatItIsForGood() {
        var fs = Disk();
        var question = new DeleteConfirmation(
            new[] { Dir(fs, "deep"), File(fs, "a.txt", 1) with { IsReadOnly = true } }, DeleteQuestionKind.Refused, withCompanions: true, fs, _text) {
            Reasons = new[] { RecycleUnavailableReason.PathTooLong, RecycleUnavailableReason.PathTooLong, RecycleUnavailableReason.NoBin },
            Recycled = 11,
        };

        Assert.True(question.Permanent);
        Assert.Equal("Не помещается в корзину", question.Title);
        Assert.Equal("Корзина не принимает 2 элемента", question.Headline);
        Assert.Equal(new[] { "Путь длиннее 259 знаков.", "У этого диска нет корзины." }, question.ReasonLines);
        Assert.Equal("Остальные 11 — уже в корзине.", question.RestLine);
        Assert.Equal("Удалить безвозвратно?", question.Warning);
        Assert.Null(question.ReadOnlyLine);
    }

    [Fact]
    public void Refused_WithNothingInTheBin_SaysNothingAboutIt() {
        var fs = Disk();
        var question = new DeleteConfirmation(new[] { Dir(fs, "deep") }, DeleteQuestionKind.Refused, withCompanions: true, fs, _text) {
            Reasons = new[] { RecycleUnavailableReason.NoBin },
        };

        Assert.Equal("Корзина не принимает папку «deep»", question.Headline);
        Assert.Null(question.RestLine);
        Assert.Null(Ask(fs, File(fs, "a.txt", 1)).RestLine);
    }

    [Fact]
    public async Task Counting_StopsQuietly_WhenTheWindowCloses() {
        var fs = new CancellingFileSystem();
        fs.Directories.Add(@"C:\x");
        var first = Dir(fs, "first");
        var second = Dir(fs, "second");
        fs.CancelAt = second.FullPath;
        var question = Ask(fs, first, second);

        var told = new List<DeleteRow?>();
        await question.CountAsync(told.Add, fs.Cancel.Token);

        Assert.Equal(new DeleteRow?[] { question.Rows[0] }, told);
        Assert.False(question.Rows[0].IsCounting);
        Assert.True(question.Rows[1].IsCounting);
        Assert.True(question.IsCounting);
    }

    [Fact]
    public async Task Counting_DoesNotStart_WhenAlreadyCancelled() {
        var fs = Disk();
        var question = Ask(fs, Dir(fs, "a"), Dir(fs, "b"));
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        var told = new List<DeleteRow?>();
        await question.CountAsync(told.Add, cancel.Token);

        Assert.Empty(told);
        Assert.All(question.Rows, r => Assert.True(r.IsCounting));
    }


    private static string Bytes(long bytes) {
        return bytes + " B";
    }

    private static FakeFileSystem Disk() {
        var fs = new FakeFileSystem();
        fs.Directories.Add(@"C:\x");

        return fs;
    }

    private static DeleteConfirmation Ask(FakeFileSystem fs, params FileSystemEntry[] items) {
        return Ask(fs, DeleteQuestionKind.Recycle, items);
    }

    private static DeleteConfirmation Ask(FakeFileSystem fs, DeleteQuestionKind kind, params FileSystemEntry[] items) {
        return new DeleteConfirmation(items, kind, withCompanions: true, fs, _text);
    }

    private static FileSystemEntry File(FakeFileSystem fs, string name, int size) {
        string path = @"C:\x\" + name;
        fs.Files[path] = new byte[size];

        return new FileSystemEntry(name, path, EntryKind.File, size, DateTime.UnixEpoch, false, false, false, false);
    }

    private static FileSystemEntry Dir(FakeFileSystem fs, string name) {
        string path = @"C:\x\" + name;
        fs.Directories.Add(path);

        return new FileSystemEntry(name, path, EntryKind.Directory, null, DateTime.UnixEpoch, false, false, false, false);
    }


    /// <summary>Closes the window - cancels the token - as the walk reaches one folder.</summary>
    private sealed class CancellingFileSystem : FakeFileSystem {
        public CancellationTokenSource Cancel { get; } = new();

        public string? CancelAt { get; set; }


        public override IReadOnlyList<FileSystemEntry> Enumerate(string path, SortOptions? sort = null, CancellationToken ct = default) {
            if (string.Equals(path, CancelAt, StringComparison.OrdinalIgnoreCase)) {
                Cancel.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            return base.Enumerate(path, sort, ct);
        }
    }
}
