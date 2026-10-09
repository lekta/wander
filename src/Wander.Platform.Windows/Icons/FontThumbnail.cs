using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Wander.Core.FileSystem;
using Wander.Core.Preview;

namespace Wander.Platform.Windows.Icons;

/// <summary>
/// A tile for a font file: two lines of three letters, Latin and Cyrillic,
/// set in the face itself, dark on a white page the caller frames as a
/// cover. A letter the font does not have is left out, a line with none of
/// them too; a font with neither (icons, symbols - a symbol font's
/// pictures stand in for letters by code page) shows the first three
/// characters it has. The shell's own tile drew a box or another font's
/// letters for what a face lacked (2026-10-09).
///
/// <para>
/// Drawn by GDI: the file is added for this process only
/// (<c>FR_PRIVATE</c>) and taken away again, the face asked for by the
/// family, weight and slant its header gives (<see cref="FontHeader"/>),
/// and the letters set by glyph index, so nothing can come from a fallback
/// font. A face GDI does not hand back under that name - it is not the
/// file's - draws nothing: null puts the caller back on the shell's tile.
/// The same family installed in Windows may be the one drawn; it is the
/// same letters in all but name.
/// </para>
/// </summary>
internal static class FontThumbnail {
    /// <summary>The em of each of two lines, of the page's side.</summary>
    private const double TwoLines = 0.36;

    /// <summary>The em of a single line, of the page's side.</summary>
    private const double OneLine = 0.5;

    /// <summary>The letters' colour, 0x00BBGGRR: near-black, as print on a page.</summary>
    private const uint Ink = 0x00202020;

    /// <summary>How many characters a font without letters is asked about before it is given up on.</summary>
    private const int MaxAsked = 512;

    private static readonly string[] _lines = { "Abc", "Абв" };

    private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc" };


    public static bool Supports(string path) {
        return _extensions.Contains(Path.GetExtension(path));
    }


    /// <summary>A PNG of the page, <paramref name="side"/> pixels square, or null.</summary>
    public static byte[]? Render(string path, int side) {
        if (!Supports(path)) {
            return null;
        }

        FontHeader? header;
        try {
            using var file = SharedRead.Open(path);
            header = FontHeader.Read(file);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return null;
        }
        // LOGFONT keeps 31 characters of a face name: a longer one cannot be asked for.
        if (header is null || header.Family.Length >= LF_FACESIZE) {
            return null;
        }

        if (AddFontResourceExW(path, FR_PRIVATE, IntPtr.Zero) == 0) {
            return null;
        }
        try {
            return Draw(header, side);
        } finally {
            RemoveFontResourceExW(path, FR_PRIVATE, IntPtr.Zero);
        }
    }


    private static byte[]? Draw(FontHeader header, int side) {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) {
            return null;
        }

        IntPtr font = IntPtr.Zero;
        IntPtr page = IntPtr.Zero;
        try {
            // Asked at the size of two lines, measured, then set again at
            // the size that fills the page: one line larger, a wide one less.
            double em = side * TwoLines;
            font = Face(dc, header, em, IntPtr.Zero);
            if (font == IntPtr.Zero || !IsFace(dc, header.Family)) {
                return null;
            }

            // A symbol font answers for letters with its own pictures, by
            // their code page bytes: those are not letters it has.
            var lines = GetTextCharset(dc) == SYMBOL_CHARSET
                ? new List<ushort[]>()
                : _lines.Select(line => Glyphs(dc, line)).Where(glyphs => glyphs.Length > 0).ToList();
            if (lines.Count == 0 && OwnGlyphs(dc) is { Length: > 0 } own) {
                lines.Add(own);
            }
            if (lines.Count == 0) {
                return null;
            }

            var sizes = Measure(dc, lines);
            double fit = Math.Min(side * 0.84 / sizes.Max(s => s.Width), side * 0.9 / sizes.Sum(s => s.Height));
            double wanted = Math.Min(side * (lines.Count == 1 ? OneLine : TwoLines), em * fit);
            if (Math.Abs(wanted - em) >= 1) {
                font = Face(dc, header, Math.Max(4, wanted), font);
                sizes = Measure(dc, lines);
            }

            var info = new BITMAPINFO {
                biSize = Marshal.SizeOf<BITMAPINFO>(),
                biWidth = side,
                biHeight = -side,
                biPlanes = 1,
                biBitCount = 32,
            };
            page = CreateDIBSection(dc, ref info, DIB_RGB_COLORS, out _, IntPtr.Zero, 0);
            if (page == IntPtr.Zero) {
                return null;
            }
            IntPtr previous = SelectObject(dc, page);
            PatBlt(dc, 0, 0, side, side, WHITENESS);
            _ = SetBkMode(dc, TRANSPARENT);
            _ = SetTextColor(dc, Ink);
            int y = (side - sizes.Sum(s => s.Height)) / 2;
            for (int i = 0; i < lines.Count; i++) {
                ExtTextOutW(dc, (side - sizes[i].Width) / 2, y, ETO_GLYPH_INDEX, IntPtr.Zero, lines[i], (uint)lines[i].Length, IntPtr.Zero);
                y += sizes[i].Height;
            }
            GdiFlush();
            SelectObject(dc, previous);

            using var picture = Image.FromHbitmap(page);
            using var png = new MemoryStream();
            picture.Save(png, ImageFormat.Png);

            return png.ToArray();
        } catch (Exception ex) when (ex is ExternalException or ArgumentException or OutOfMemoryException) {
            return null;
        } finally {
            if (page != IntPtr.Zero) {
                DeleteObject(page);
            }
            if (font != IntPtr.Zero) {
                DeleteObject(font);
            }
            DeleteDC(dc);
        }
    }

    /// <summary>The face at <paramref name="em"/> pixels, selected into the DC in place of <paramref name="replaced"/>, which is deleted.</summary>
    private static IntPtr Face(IntPtr dc, FontHeader header, double em, IntPtr replaced) {
        IntPtr font = CreateFontW(
            -(int)Math.Round(em), 0, 0, 0, header.Weight, header.Italic ? 1u : 0u, 0, 0,
            DEFAULT_CHARSET, OUT_TT_ONLY_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH, header.Family);
        if (font != IntPtr.Zero) {
            SelectObject(dc, font);
        }
        if (replaced != IntPtr.Zero) {
            DeleteObject(replaced);
        }

        return font;
    }

    /// <summary>Whether the face GDI selected goes by <paramref name="family"/> - the one asked for, not a substitute.</summary>
    private static bool IsFace(IntPtr dc, string family) {
        var name = new char[LF_FACESIZE];
        if (GetTextFaceW(dc, name.Length, name) <= 0) {
            return false;
        }
        int end = Array.IndexOf(name, (char)0);

        return string.Equals(new string(name, 0, end < 0 ? name.Length : end), family, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The glyphs of <paramref name="text"/>'s characters the face has, in order.</summary>
    private static ushort[] Glyphs(IntPtr dc, string text) {
        var glyphs = new ushort[text.Length];
        if (GetGlyphIndicesW(dc, text, text.Length, glyphs, GGI_MARK_NONEXISTING_GLYPHS) == GDI_ERROR) {
            return Array.Empty<ushort>();
        }

        return glyphs.Where(glyph => glyph is not (MissingGlyph or 0)).ToArray();
    }

    /// <summary>
    /// The glyphs of the first three characters past the space the face
    /// draws (<c>GetFontUnicodeRanges</c>). Each is asked on its own: a
    /// symbol font reports its whole code page, the empty slots too.
    /// </summary>
    private static ushort[] OwnGlyphs(IntPtr dc) {
        uint size = GetFontUnicodeRanges(dc, IntPtr.Zero);
        if (size == 0) {
            return Array.Empty<ushort>();
        }

        IntPtr set = Marshal.AllocHGlobal((int)size);
        try {
            if (GetFontUnicodeRanges(dc, set) == 0) {
                return Array.Empty<ushort>();
            }

            // GLYPHSET: cbThis, flAccel, cGlyphsSupported, cRanges, then
            // WCRANGE { WCHAR wcLow; USHORT cGlyphs } each.
            int ranges = Marshal.ReadInt32(set, 12);
            var picked = new List<ushort>();
            int asked = 0;
            for (int r = 0; r < ranges && picked.Count < 3 && asked < MaxAsked; r++) {
                int low = (ushort)Marshal.ReadInt16(set, 16 + (r * 4));
                int count = (ushort)Marshal.ReadInt16(set, 16 + (r * 4) + 2);
                for (int c = low; c < low + count && picked.Count < 3 && asked < MaxAsked; c++) {
                    if (c > 0x20 && !char.IsWhiteSpace((char)c) && !char.IsControl((char)c) && !char.IsSurrogate((char)c)) {
                        asked++;
                        picked.AddRange(Glyphs(dc, ((char)c).ToString()));
                    }
                }
            }

            return picked.ToArray();
        } finally {
            Marshal.FreeHGlobal(set);
        }
    }

    private static List<(int Width, int Height)> Measure(IntPtr dc, List<ushort[]> lines) {
        return lines.Select(line => GetTextExtentPointI(dc, line, line.Length, out SIZE size)
            ? (Math.Max(1, size.cx), Math.Max(1, size.cy))
            : (1, 1)).ToList();
    }


    // --- GDI ---

    private const int LF_FACESIZE = 32;
    private const uint FR_PRIVATE = 0x10;
    private const uint DEFAULT_CHARSET = 1;
    private const int SYMBOL_CHARSET = 2;
    private const uint OUT_TT_ONLY_PRECIS = 7;
    private const uint CLIP_DEFAULT_PRECIS = 0;
    private const uint ANTIALIASED_QUALITY = 4;
    private const uint DEFAULT_PITCH = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const int TRANSPARENT = 1;
    private const uint WHITENESS = 0x00FF0062;
    private const uint ETO_GLYPH_INDEX = 0x0010;
    private const uint GGI_MARK_NONEXISTING_GLYPHS = 0x0001;
    private const uint GDI_ERROR = 0xFFFFFFFF;
    private const ushort MissingGlyph = 0xFFFF;


    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE {
        public int cx;
        public int cy;
    }


    /// <summary>BITMAPINFOHEADER alone: a 32-bit DIB needs no colour table.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }


    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceExW(string name, uint fl, IntPtr res);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveFontResourceExW(string name, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight, uint bItalic, uint bUnderline,
        uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily,
        string pszFaceName);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetTextFaceW(IntPtr hdc, int c, [Out] char[] lpName);

    [DllImport("gdi32.dll")]
    private static extern int GetTextCharset(IntPtr hdc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetGlyphIndicesW(IntPtr hdc, string lpstr, int c, [Out] ushort[] pgi, uint fl);

    [DllImport("gdi32.dll")]
    private static extern uint GetFontUnicodeRanges(IntPtr hdc, IntPtr lpgs);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPointI(IntPtr hdc, ushort[] pgiIn, int cgi, out SIZE psize);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExtTextOutW(IntPtr hdc, int x, int y, uint options, IntPtr lprect, ushort[] lpString, uint c, IntPtr lpDx);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();
}
