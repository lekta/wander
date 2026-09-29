using System.Globalization;
using System.Text.RegularExpressions;

namespace Wander.Site;

/// <summary>
/// The size an SVG icon declares on its root element - the icons of
/// docs/icons are drawn by hand from the app's XAML, 16 by 16, and get the
/// same width and height on the page as the screenshots do.
/// </summary>
internal static class Svg {
    private static readonly Regex _root = new(@"<svg\b[^>]*>", RegexOptions.Singleline);
    private static readonly Regex _width = new(@"\swidth=""(\d+)""");
    private static readonly Regex _height = new(@"\sheight=""(\d+)""");


    public static (int Width, int Height)? Size(string path) {
        Match root = _root.Match(File.ReadAllText(path));
        if (!root.Success) {
            return null;
        }

        Match width = _width.Match(root.Value);
        Match height = _height.Match(root.Value);
        if (!width.Success || !height.Success) {
            return null;
        }

        return (int.Parse(width.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(height.Groups[1].Value, CultureInfo.InvariantCulture));
    }
}
