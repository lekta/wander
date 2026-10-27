using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using Wander.Core;
using Wander.Core.Localization;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// The track's shape on its tile (decisions 2026-10-27). A track without
/// art - most of a folder of sounds - is a white page: the shape above, a
/// band along the bottom with a play mark, the length in whole seconds,
/// rounded up as under a player, and the rate (variant
/// E: one strip of facts, read at a glance). A track with art keeps it, the
/// band laid over its bottom with the mark and the shape. Places come from
/// <see cref="SoundTile"/>, which the list's live play button shares.
/// Tracks over <see cref="Waveform.ListLimit"/> keep the cover alone, or
/// the shell's icon.
/// </summary>
internal static class WaveformThumbnail {
    private static readonly Color _peak = Color.FromArgb(0x9C, 0xC3, 0xEE);
    private static readonly Color _body = Color.FromArgb(0x2B, 0x6C, 0xC4);
    private static readonly Color _bandBack = Color.FromArgb(0xF0, 0xF3, 0xF8);
    private static readonly Color _ink = Color.FromArgb(0x30, 0x30, 0x30);
    private static readonly Color _dim = Color.FromArgb(0x70, 0x70, 0x70);
    private static readonly Color _mark = Color.FromArgb(0x90, 0x90, 0x90);
    private static readonly Color _coverBand = Color.FromArgb(160, 0, 0, 0);
    private static readonly Color _coverPeak = Color.FromArgb(150, 255, 255, 255);


    /// <summary>The shape to draw on the tile, or null - too long, or not decodable.</summary>
    public static Waveform? Shape(string path) {
        return ServiceLocator.TryGet<IWaveformSource>()?.Read(path, Waveform.ListLimit, CancellationToken.None);
    }

    /// <summary>A white page <paramref name="side"/> square: the shape, and under it the band.</summary>
    public static Bitmap RenderPage(Waveform shape, int? sampleRate, int side) {
        var page = new Bitmap(side, side);
        using var g = Graphics.FromImage(page);
        g.Clear(Color.White);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var (top, height) = SoundTile.Band(side);
        int bandTop = (int)Math.Round(top);
        int pad = Math.Max(2, side / 32);
        Draw(g, shape, new Rectangle(0, pad, side, bandTop - pad * 2), _peak, _body);

        using (var back = new SolidBrush(_bandBack)) {
            g.FillRectangle(back, 0, bandTop, side, side - bandTop);
        }
        DrawMark(g, side, _mark);

        var row = new RectangleF((float)SoundTile.LengthLeft(side), bandTop, side - (float)SoundTile.LengthLeft(side) - pad, side - bandTop);
        using var length = new Font("Segoe UI Semibold", (float)(height * 0.46), GraphicsUnit.Pixel);
        using var ink = new SolidBrush(_ink);
        g.DrawString(Timecode.Format(shape.Duration, roundUp: true), length, ink, row,
            new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap });

        if (sampleRate is > 0 && RateText(sampleRate.Value) is { } rate) {
            using var small = new Font("Segoe UI", (float)(height * 0.34), GraphicsUnit.Pixel);
            using var dim = new SolidBrush(_dim);
            g.DrawString(rate, small, dim, row,
                new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap });
        }

        return page;
    }

    /// <summary>The band over the bottom of the cover at <paramref name="cover"/>: the play mark, then the shape.</summary>
    public static void DrawBand(Bitmap plate, Rectangle cover, Waveform shape) {
        using var g = Graphics.FromImage(plate);
        var (top, height) = SoundTile.Band(cover.Width);
        int bandHeight = Math.Max(6, (int)Math.Round(height));
        var band = new Rectangle(cover.X, cover.Bottom - bandHeight, cover.Width, bandHeight);
        using (var back = new SolidBrush(_coverBand)) {
            g.FillRectangle(back, band);
        }

        var state = g.Save();
        g.TranslateTransform(cover.X, cover.Bottom - (float)(top + height));
        DrawMark(g, cover.Width, Color.FromArgb(200, 255, 255, 255));
        g.Restore(state);

        int left = (int)Math.Ceiling(SoundTile.LengthLeft(cover.Width));
        int pad = Math.Max(1, bandHeight / 6);
        Draw(g, shape, new Rectangle(band.X + left, band.Y + pad, band.Width - left - pad, band.Height - pad * 2), _coverPeak, Color.White);
    }


    /// <summary>
    /// The play mark the live button lands on (<see cref="SoundTile.Button"/>):
    /// a ring with a triangle, drawn quiet - it says the tile plays, the
    /// list's button under the mouse is the loud one.
    /// </summary>
    private static void DrawMark(Graphics g, int page, Color color) {
        var (x, y, r) = SoundTile.Button(page);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, Math.Max(1f, (float)r / 7));
        using var brush = new SolidBrush(color);
        g.DrawEllipse(pen, (float)(x - r), (float)(y - r), (float)(r * 2), (float)(r * 2));

        float m = (float)(r * 0.42);
        g.FillPolygon(brush, new[] {
            new PointF((float)x - m * 0.7f, (float)y - m),
            new PointF((float)x + m * 1.05f, (float)y),
            new PointF((float)x - m * 0.7f, (float)y + m),
        });
        g.SmoothingMode = old;
    }

    /// <summary>"44,1 kHz" in the interface's language; null when there is no text source (a stand).</summary>
    private static string? RateText(int sampleRate) {
        return ServiceLocator.TryGet<ITextSource>() is { } text
            ? string.Format(CultureInfo.CurrentCulture, text.Get("PreviewAudioSampleRate"), sampleRate / 1000.0)
            : null;
    }

    /// <summary>
    /// A column per pixel, mirrored about the middle: the peak in the light
    /// colour, the RMS over it in the strong one. Lines rather than a path -
    /// at tile sizes they are the same pixels and need no smoothing.
    /// </summary>
    private static void Draw(Graphics g, Waveform shape, Rectangle area, Color peak, Color body) {
        if (area.Width <= 0 || area.Height <= 0) {
            return;
        }

        using var peakPen = new Pen(peak);
        using var bodyPen = new Pen(body);
        float middle = area.Top + area.Height / 2f;
        float half = area.Height / 2f;
        for (int x = 0; x < area.Width; x++) {
            (double p, double r) = shape.Slice(x, area.Width);
            float ph = Math.Max(0.5f, (float)(Waveform.Height(p) * half));
            float rh = Math.Max(0.5f, (float)(Waveform.Height(r) * half));
            g.DrawLine(peakPen, area.Left + x, middle - ph, area.Left + x, middle + ph);
            g.DrawLine(bodyPen, area.Left + x, middle - rh, area.Left + x, middle + rh);
        }
    }
}
