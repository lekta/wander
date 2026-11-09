using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Wander.Core.Listing;

namespace Wander.Platform.Windows.Icons;

/// <summary>One file on a folder's picture: its own picture at the same step, what it is, and whether it was found a level down.</summary>
internal sealed record FolderCard(Bitmap Picture, FolderContent Content, bool Inside);


/// <summary>
/// Draws a folder's own picture (PLAN F1, decisions 2026-11-06): the
/// folder in Wander's own vector - the shell's is not cut into layers - with
/// its files' pictures in it. Two looks, as the fitting has them
/// (https://claude.ai/artifact/5GtGMNp5U1Aa8tn72DJMGs, Folder.dc.html, the
/// numbers below are its percentages of the box): cards slid in one behind
/// the other, down and to the right, the first file in front and the
/// folder's front flap over them all; or, in the gallery, up to four
/// pictures laid out as the folder's face. A folder with nothing to show
/// is the empty folder of the same drawing.
///
/// <para>
/// A program's icon and a model are drawn on transparency and go in bare
/// (second fitting, 2026-11-06): no card and no backdrop - a backdrop came
/// out as a square of the theme inside the folder - fitted whole into the
/// biggest square of their place, over a shadow that follows their outline.
/// </para>
///
/// <para>
/// Hairlines - the cards' edges, their corners and shadows - are sized in
/// pixels of the view the look is for (<c>nominal</c>), as the fitting
/// sizes them on screen; the rest scales with the box.
/// </para>
/// </summary>
internal static class FolderThumbnail {
    private const float CardWidth = 52;
    private const float CardHeight = 46;

    /// <summary>The top of the front flap over the cards, and over nothing.</summary>
    private const float CascadeFlap = 60;

    private const float EmptyFlap = 32;

    /// <summary>The bottom of the folder; the front flap is drawn from its top down to here.</summary>
    private const float Bottom = 83;

    // The gallery's face: where the pictures go, and the gap between two.
    private const float CellsLeft = 9.5f;
    private const float CellsTop = 23.5f;
    private const float CellsWidth = 81;
    private const float CellsHeight = 61;
    private const float CellGap = 1.8f;

    private static readonly Color _back = Color.FromArgb(0xF0, 0xB2, 0x3A);
    private static readonly Color _flapTop = Color.FromArgb(0xFF, 0xDB, 0x73);
    private static readonly Color _flapBottom = Color.FromArgb(0xFF, 0xC8, 0x44);
    private static readonly Color _flapEdge = Color.FromArgb(0xFF, 0xEB, 0xAE);
    private static readonly Color _face = Color.FromArgb(0xFF, 0xCD, 0x52);

    // The silhouette's cards (second fitting, 2026-11-09): solid, nothing
    // faded - a faded card reads as a hidden file.
    private static readonly Color _silhouetteCard = Color.FromArgb(0xEC, 0xEE, 0xF1);
    private static readonly Color _silhouetteCell = Color.FromArgb(0xFF, 0xF3, 0xCF);

    /// <summary>A bare icon's shadow: its outline in black at this share of its opacity.</summary>
    private static readonly ColorMatrix _shade = new(new[] {
        new float[] { 0, 0, 0, 0, 0 },
        new float[] { 0, 0, 0, 0, 0 },
        new float[] { 0, 0, 0, 0, 0 },
        new float[] { 0, 0, 0, 0.35f, 0 },
        new float[] { 0, 0, 0, 0, 1 },
    });

    /// <summary>Where the cards of a cascade stand, by how many there are: the back one first.</summary>
    private static readonly PointF[][] _spots = {
        new[] { new PointF(24, 25) },
        new[] { new PointF(17, 23), new PointF(33, 28) },
        new[] { new PointF(11, 22), new PointF(23, 26), new PointF(35, 30) },
    };


    /// <summary>The on-screen side a look is drawn for: a tile's icon, a big icon, a gallery picture - the fitting's three sizes.</summary>
    public static int Nominal(bool grid, bool medium) {
        return grid ? 216 : medium ? 48 : 96;
    }


    /// <summary>The picture, <paramref name="side"/> pixels square, as PNG.</summary>
    public static byte[] Paint(IReadOnlyList<FolderCard> cards, bool grid, int side, int nominal) {
        return Draw(side, nominal, art => {
            if (grid && cards.Count > 0) {
                art.Face(cards.Count, (i, cell) => art.Cell(cards[i], cell));
            } else {
                art.Cascade(cards.Count, (i, box) => art.Card(cards[i], box));
            }
        });
    }

    /// <summary>
    /// The folder while its picture is made (second fitting, 2026-11-09): the
    /// same layout for as many files, each a plain card with its type's icon
    /// at the size a file wears it - none on a card too small for one.
    /// </summary>
    /// <param name="types">Each file's type icon, null where the shell had none.</param>
    /// <param name="inside">Whether each file was found a level down.</param>
    public static byte[] PaintSilhouette(IReadOnlyList<Bitmap?> types, IReadOnlyList<bool> inside, bool grid, int side, int nominal) {
        return Draw(side, nominal, art => {
            if (grid) {
                art.Face(types.Count, (i, cell) => art.SilhouetteCell(nominal >= 96 ? types[i] : null, inside[i] && nominal >= 96, cell));
            } else {
                art.Cascade(types.Count, (i, box) => art.SilhouetteCard(nominal >= 64 ? types[i] : null, box));
            }
        });
    }


    private static byte[] Draw(int side, int nominal, Action<Art> paint) {
        using var canvas = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas)) {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            paint(new Art(g, side / 100f, side / (float)nominal));
        }

        using var png = new MemoryStream();
        canvas.Save(png, ImageFormat.Png);

        return png.ToArray();
    }


    /// <summary>A program's icon or a model: drawn on transparency, it goes in bare. Everything else covers its place.</summary>
    private static bool IsBare(FolderContent content) {
        return content is FolderContent.Model or FolderContent.Program;
    }

    /// <summary>A page - a document, a track's cover or shape, a font's letters: on white, as its own tile is.</summary>
    private static bool IsPage(FolderContent content) {
        return content is FolderContent.Document or FolderContent.Sound or FolderContent.Font;
    }

    /// <summary>A rectangle with round corners; square ones when the radius is nothing.</summary>
    private static GraphicsPath Rounded(RectangleF box, float radius) {
        var path = new GraphicsPath();
        float r = Math.Min(radius, Math.Min(box.Width, box.Height) / 2);
        if (r <= 0) {
            path.AddRectangle(box);

            return path;
        }

        float d = r * 2;
        path.AddArc(box.X, box.Y, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>
    /// Fills <paramref name="shape"/> with the picture covering
    /// <paramref name="into"/>. Resampled once
    /// to the size it lands at and laid as a texture, so the shape's round
    /// corners are smoothed as a path is; a clip would cut them stepped.
    /// </summary>
    private static void FillWith(Graphics g, GraphicsPath shape, RectangleF into, Bitmap picture) {
        using var sized = Sized(picture, into, cover: true, out var at);
        using var brush = new TextureBrush(sized, WrapMode.Clamp);
        brush.TranslateTransform(at.X, at.Y);
        g.FillPath(brush, shape);
    }

    /// <summary>
    /// The picture resampled to the size it lands at in <paramref name="into"/> -
    /// covering it, or fitted whole - and where its corner lands, centred.
    /// </summary>
    private static Bitmap Sized(Bitmap picture, RectangleF into, bool cover, out PointF at) {
        float scale = cover
            ? Math.Max(into.Width / picture.Width, into.Height / picture.Height)
            : Math.Min(into.Width / picture.Width, into.Height / picture.Height);
        int w = Math.Max(1, (int)Math.Round(picture.Width * scale));
        int h = Math.Max(1, (int)Math.Round(picture.Height * scale));

        var sized = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(sized)) {
            sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sg.CompositingMode = CompositingMode.SourceCopy;
            // Mirrored past its edges rather than transparent: the edge
            // pixels would otherwise come out half-faded.
            using var edges = new ImageAttributes();
            edges.SetWrapMode(WrapMode.TileFlipXY);
            sg.DrawImage(picture, new Rectangle(0, 0, w, h), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, edges);
        }
        at = new PointF(into.X + ((into.Width - w) / 2), into.Y + ((into.Height - h) / 2));

        return sized;
    }


    /// <summary>The pen of one picture: the graphics, the box's unit and the view's pixel.</summary>
    private sealed class Art {
        private readonly Graphics _g;
        private readonly float _unit;
        private readonly float _px;


        public Art(Graphics g, float unit, float px) {
            _g = g;
            _unit = unit;
            _px = px;
        }


        /// <summary>The back, up to three cards back to front - the first file in front - and the flap over them; <paramref name="card"/> draws file i in its box.</summary>
        public void Cascade(int count, Action<int, RectangleF> card) {
            using (var back = new Shape(_unit, 8, 20)
                .Q(8, 15, 13, 15).L(36, 15).Q(39, 15, 41, 17.5f).L(44, 21).L(87, 21)
                .Q(92, 21, 92, 26).L(92, 78).Q(92, 83, 87, 83).L(13, 83).Q(8, 83, 8, 78).Close())
            using (var brush = new SolidBrush(_back)) {
                _g.FillPath(brush, back);
            }

            int n = Math.Min(count, _spots.Length);
            if (n > 0) {
                var spots = _spots[n - 1];
                for (int i = 0; i < n; i++) {
                    card(n - 1 - i, Box(spots[i].X, spots[i].Y, CardWidth, CardHeight));
                }
            }

            Flap(n > 0 ? CascadeFlap : EmptyFlap);
        }

        /// <summary>The back's tab, the face, and the places on it: one whole, two side by side, three as one and two, four as a square; <paramref name="cell"/> fills place i.</summary>
        public void Face(int count, Action<int, RectangleF> cell) {
            using (var tab = new Shape(_unit, 8, 27)
                .L(8, 18).Q(8, 13, 13, 13).L(33, 13).Q(36, 13, 38, 15.5f).L(41, 19).L(87, 19).Q(92, 19, 92, 24).L(92, 27).Close())
            using (var brush = new SolidBrush(_back)) {
                _g.FillPath(brush, tab);
            }
            using (var face = Rounded(Box(7, 21, 86, 66), Px(0.06f)))
            using (var brush = new SolidBrush(_face)) {
                _g.FillPath(brush, face);
            }

            const float halfWidth = (CellsWidth - CellGap) / 2;
            const float halfHeight = (CellsHeight - CellGap) / 2;
            const float right = CellsLeft + halfWidth + CellGap;
            const float lower = CellsTop + halfHeight + CellGap;
            RectangleF[] cells = Math.Min(count, 4) switch {
                1 => new[] { Box(CellsLeft, CellsTop, CellsWidth, CellsHeight) },
                2 => new[] { Box(CellsLeft, CellsTop, halfWidth, CellsHeight), Box(right, CellsTop, halfWidth, CellsHeight) },
                3 => new[] {
                    Box(CellsLeft, CellsTop, halfWidth, CellsHeight),
                    Box(right, CellsTop, halfWidth, halfHeight), Box(right, lower, halfWidth, halfHeight),
                },
                _ => new[] {
                    Box(CellsLeft, CellsTop, halfWidth, halfHeight), Box(right, CellsTop, halfWidth, halfHeight),
                    Box(CellsLeft, lower, halfWidth, halfHeight), Box(right, lower, halfWidth, halfHeight),
                },
            };

            for (int i = 0; i < cells.Length; i++) {
                cell(i, cells[i]);
            }
        }

        /// <summary>A card of the cascade: shadow, a thin dark rim, a white edge and the picture inside it; a program or a model, bare.</summary>
        public void Card(FolderCard card, RectangleF box) {
            if (IsBare(card.Content)) {
                Bare(card.Picture, box);

                return;
            }

            using var shape = Frame(box, out var inner);
            _g.FillPath(Brushes.White, shape);
            FillWith(_g, shape, inner, card.Picture);
        }

        /// <summary>A card of the silhouette: the same frame, solid inside, its type's icon in the middle of what the flap leaves of it.</summary>
        public void SilhouetteCard(Bitmap? type, RectangleF box) {
            using (var shape = Frame(box, out _))
            using (var brush = new SolidBrush(_silhouetteCard)) {
                _g.FillPath(brush, shape);
            }

            var shown = box;
            shown.Height = Math.Min(box.Bottom, CascadeFlap * _unit) - box.Y;
            TypeIcon(type, shown);
        }

        /// <summary>A place on the gallery's face: white under a page, the picture - a program or a model bare - and a folder mark when it came from a subfolder.</summary>
        public void Cell(FolderCard card, RectangleF cell) {
            if (IsBare(card.Content)) {
                Bare(card.Picture, cell);
            } else {
                using var shape = Rounded(cell, Px(0.025f));
                if (IsPage(card.Content)) {
                    _g.FillPath(Brushes.White, shape);
                }
                FillWith(_g, shape, cell, card.Picture);
            }

            if (card.Inside) {
                SubfolderMark(cell);
            }
        }

        /// <summary>A place on the silhouette's face: solid, its type's icon in the middle, the folder mark as on the picture.</summary>
        public void SilhouetteCell(Bitmap? type, bool inside, RectangleF cell) {
            using (var shape = Rounded(cell, Px(0.025f)))
            using (var brush = new SolidBrush(_silhouetteCell)) {
                _g.FillPath(brush, shape);
            }
            TypeIcon(type, cell);
            if (inside) {
                SubfolderMark(cell);
            }
        }


        /// <summary>A length of the fitting's box (0-100) in pixels of the picture.</summary>
        private RectangleF Box(float x, float y, float w, float h) {
            return new RectangleF(x * _unit, y * _unit, w * _unit, h * _unit);
        }

        /// <summary>
        /// A hairline as the fitting rounds it: a share of the view's side
        /// in whole pixels of the view, never under one, in pixels of the
        /// picture.
        /// </summary>
        private float Px(float share) {
            double viewSide = _unit * 100 / _px;

            return (float)Math.Max(1, Math.Round(viewSide * share, MidpointRounding.AwayFromZero)) * _px;
        }

        /// <summary>
        /// A card's frame: shadow, a thin dark rim and a white edge. Returns
        /// the shape inside the edge - the caller's to fill - and its box.
        /// </summary>
        private GraphicsPath Frame(RectangleF box, out RectangleF inner) {
            float radius = Px(0.02f);
            float edge = Px(1f / 96);

            // The shadow a pixel down, softened by its own corner's width -
            // a few widening layers rather than a blur pass.
            float blur = radius;
            var dropped = box;
            dropped.Offset(0, _px);
            (float Grow, int Alpha)[] layers = { (0, 32), (blur / 2, 20), (blur, 12) };
            foreach (var (grow, alpha) in layers) {
                var layer = dropped;
                layer.Inflate(grow, grow);
                using var path = Rounded(layer, radius + grow);
                using var shade = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                _g.FillPath(shade, path);
            }

            var rim = box;
            rim.Inflate(_px / 2, _px / 2);
            using (var path = Rounded(rim, radius + (_px / 2)))
            using (var shade = new SolidBrush(Color.FromArgb(56, 0, 0, 0))) {
                _g.FillPath(shade, path);
            }
            using (var path = Rounded(box, radius)) {
                _g.FillPath(Brushes.White, path);
            }

            inner = box;
            inner.Inflate(-edge, -edge);

            return Rounded(inner, Math.Max(0, radius - edge));
        }

        /// <summary>
        /// A type's icon in the middle of a place, at the size a file wears
        /// it in a list (32 pixels of the view), smaller only where the place is.
        /// </summary>
        private void TypeIcon(Bitmap? type, RectangleF place) {
            if (type is null) {
                return;
            }

            float side = Math.Min(32 * _px, Math.Min(place.Width, place.Height) * 0.7f);
            var square = new RectangleF(place.X + ((place.Width - side) / 2), place.Y + ((place.Height - side) / 2), side, side);
            using var sized = Sized(type, square, cover: false, out var at);
            _g.DrawImage(sized, at.X, at.Y, sized.Width, sized.Height);
        }

        /// <summary>
        /// A program's icon or a model as drawn, its transparency kept: fitted
        /// whole into the biggest square in the middle of its place - an icon
        /// is square, and fitted to the place it would hang out of the
        /// folder - over its shadow.
        /// </summary>
        private void Bare(Bitmap picture, RectangleF place) {
            float side = Math.Min(place.Width, place.Height);
            var square = new RectangleF(place.X + ((place.Width - side) / 2), place.Y + ((place.Height - side) / 2), side, side);
            using var sized = Sized(picture, square, cover: false, out var at);
            Shadow(sized, at);
            _g.DrawImage(sized, at.X, at.Y, sized.Width, sized.Height);
        }

        /// <summary>
        /// The fitting's drop shadow under a bare icon: its outline in black at
        /// a third of its opacity, a view's pixel or so down, softened by its
        /// blur's width - drawn through a copy shrunk by that much and grown
        /// back, a blur's worth without a filter pass.
        /// </summary>
        private void Shadow(Bitmap icon, PointF at) {
            int shrink = Math.Max(1, (int)Math.Round(Px(0.025f)));
            int pad = shrink * 2;
            int w = icon.Width + (pad * 2);
            int h = icon.Height + (pad * 2);

            using var small = new Bitmap(Math.Max(1, w / shrink), Math.Max(1, h / shrink), PixelFormat.Format32bppArgb);
            using (var sg = Graphics.FromImage(small)) {
                sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using var black = new ImageAttributes();
                black.SetColorMatrix(_shade);
                var into = new Rectangle(pad / shrink, pad / shrink, Math.Max(1, icon.Width / shrink), Math.Max(1, icon.Height / shrink));
                sg.DrawImage(icon, into, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, black);
            }

            _g.DrawImage(small, new RectangleF(at.X - pad, at.Y - pad + Px(0.012f), w, h));
        }

        /// <summary>The small folder in a place's lower right corner: this picture is from a subfolder.</summary>
        private void SubfolderMark(RectangleF cell) {
            float side = Math.Min(cell.Width, cell.Height) * 0.36f;
            float x = cell.Right - (cell.Width * 0.05f) - (cell.Width * 0.36f) + ((cell.Width * 0.36f - side) / 2);
            float y = cell.Bottom - (cell.Height * 0.05f) - (cell.Height * 0.36f) + ((cell.Height * 0.36f - side) / 2);
            float unit = side / 24;

            using var mark = new Shape(unit, 2, 6, x, y)
                .Q(2, 4, 4, 4).L(9, 4).L(11, 6).L(20, 6).Q(22, 6, 22, 8).L(22, 18).Q(22, 20, 20, 20)
                .L(4, 20).Q(2, 20, 2, 18).Close();
            using var brush = new SolidBrush(_back);
            using var pen = new Pen(Color.White, 1.6f * unit);
            _g.FillPath(brush, mark);
            _g.DrawPath(pen, mark);
        }

        /// <summary>The front flap from <paramref name="top"/> down, lighter at the top, with its lit edge.</summary>
        private void Flap(float top) {
            using (var flap = new Shape(_unit, 8, top + 5)
                .Q(8, top, 13, top).L(87, top).Q(92, top, 92, top + 5).L(92, 78).Q(92, Bottom, 87, Bottom)
                .L(13, Bottom).Q(8, Bottom, 8, 78).Close()) {
                // A pixel past each end: a gradient brush repeats its first
                // colour on its last row otherwise.
                var span = Box(8, top, 84, Bottom - top);
                span.Inflate(0, 1);
                using var brush = new LinearGradientBrush(span, _flapTop, _flapBottom, LinearGradientMode.Vertical);
                _g.FillPath(brush, flap);
            }

            using var edge = new Pen(_flapEdge, 0.9f * _unit);
            float y = (top + 0.7f) * _unit;
            _g.DrawLine(edge, 13 * _unit, y, 87 * _unit, y);
        }
    }


    /// <summary>
    /// A path in the fitting's units, written as its SVG is: lines and
    /// quadratic curves from a start point; a quadratic is a cubic with
    /// both handles two thirds of the way to its one. Points are gathered
    /// and become a <see cref="GraphicsPath"/> - the caller's - only when
    /// the shape is closed.
    /// </summary>
    private sealed class Shape {
        private readonly List<PointF> _points = new();
        private readonly List<byte> _types = new();
        private readonly float _unit;
        private readonly float _dx;
        private readonly float _dy;


        public Shape(float unit, float x, float y, float dx = 0, float dy = 0) {
            _unit = unit;
            _dx = dx;
            _dy = dy;
            Add(At(x, y), PathPointType.Start);
        }


        public Shape L(float x, float y) {
            Add(At(x, y), PathPointType.Line);

            return this;
        }

        public Shape Q(float cx, float cy, float x, float y) {
            var from = _points[^1];
            var c = At(cx, cy);
            var to = At(x, y);
            Add(new PointF(from.X + ((c.X - from.X) * 2 / 3), from.Y + ((c.Y - from.Y) * 2 / 3)), PathPointType.Bezier);
            Add(new PointF(to.X + ((c.X - to.X) * 2 / 3), to.Y + ((c.Y - to.Y) * 2 / 3)), PathPointType.Bezier);
            Add(to, PathPointType.Bezier);

            return this;
        }

        public GraphicsPath Close() {
            _types[^1] |= (byte)PathPointType.CloseSubpath;

            return new GraphicsPath(_points.ToArray(), _types.ToArray());
        }


        private void Add(PointF point, PathPointType type) {
            _points.Add(point);
            _types.Add((byte)type);
        }


        private PointF At(float x, float y) {
            return new PointF(_dx + (x * _unit), _dy + (y * _unit));
        }
    }
}
