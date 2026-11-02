using System.Drawing;
using System.Drawing.Drawing2D;
using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A tile for a plain text file: a page of greeked text - a grey bar for
/// each word, a heading darker and bigger, as <see cref="TextSketch"/> lays
/// them out - on a white portrait sheet the caller frames as a cover. The
/// shell has nothing for these but the type's icon, the same on every
/// file. Only the start of the file is read; one that turns out not to be
/// text keeps that icon.
/// </summary>
internal static class TextThumbnail {
    /// <summary>
    /// Bytes read: twice the encoding guess's sample, as the pane's find
    /// reads, so a character cut at the sample's end does not spoil the
    /// guess (<see cref="EncodingProbe.Detect"/>). More than a page holds.
    /// </summary>
    private const int HeadBytes = EncodingProbe.SampleSize * 2;

    /// <summary>The sheet's width, of its height: A4.</summary>
    private const double SheetAspect = 0.707;

    /// <summary>The empty edge left and right of the text, of the sheet's width.</summary>
    private const double MarginX = 0.1;

    /// <summary>The empty edge above and below the text, of the sheet's height.</summary>
    private const double MarginY = 0.07;

    /// <summary>A row of fewer pixels than this is a small sheet - the 96-px tile - and its body bars are drawn darker.</summary>
    private const double SmallRow = 4;

    private static readonly Color _body = Color.FromArgb(0xB4, 0xB9, 0xC1);
    private static readonly Color _bodySmall = Color.FromArgb(0x9A, 0xA0, 0xA9);
    private static readonly Color _heading = Color.FromArgb(0x5A, 0x60, 0x6A);

    /// <summary>Prose and logs, which the pane shows as text too; code and configs keep their type's icon.</summary>
    private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase) {
        ".txt", ".log", ".md", ".markdown",
    };


    public static bool Supports(string path) {
        return _extensions.Contains(Path.GetExtension(path));
    }


    /// <summary>The sheet, <paramref name="height"/> pixels tall, or null when the file cannot be read or is not text.</summary>
    public static Bitmap? Render(string path, int height) {
        if (!Supports(path) || ReadHead(path) is not { } text) {
            return null;
        }

        int width = Math.Max(1, (int)Math.Round(height * SheetAspect));
        var sheet = new Bitmap(width, height);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        double left = width * MarginX;
        double top = height * MarginY;
        double column = width * (1 - MarginX * 2) / TextSketch.Columns;
        double row = height * (1 - MarginY * 2) / TextSketch.Rows;
        // A small sheet's bars are a pixel or two thick: in the light grey
        // and smeared over two pixel rows they all but vanished at 96 px.
        using var body = new SolidBrush(row < SmallRow ? _bodySmall : _body);
        using var heading = new SolidBrush(_heading);
        foreach (var bar in TextSketch.Lay(text)) {
            // Rows on whole pixels, so a thin bar is one crisp line; across
            // they stay fractional and the gaps between words come out soft.
            float y = (float)Math.Round(top + bar.Top * row);
            float thickness = Math.Max(1, (float)Math.Round(bar.Height * row));
            var box = new RectangleF((float)(left + bar.Left * column), y, (float)(bar.Width * column), thickness);
            // Rounded to a pill; a word of a letter or two is a dot.
            float round = Math.Min(box.Width, box.Height);
            g.FillRoundedRectangle(bar.Rank == 0 ? body : heading, box, new SizeF(round, round));
        }

        return sheet;
    }


    /// <summary>The start of the file as text, decoded as the text pane decodes it; null when unreadable or binary.</summary>
    private static string? ReadHead(string path) {
        byte[] head;
        try {
            using var file = SharedRead.Open(path);
            head = new byte[(int)Math.Min(file.Length, HeadBytes)];
            file.ReadExactly(head);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }

        return TextProbe.LooksLikeText(head.AsSpan(0, Math.Min(head.Length, TextProbe.SampleSize)))
            ? EncodingProbe.Decode(head)
            : null;
    }
}
