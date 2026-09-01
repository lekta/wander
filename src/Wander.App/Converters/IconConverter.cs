using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Wander.Core;
using Wander.Core.Icons;

namespace Wander.App.Converters;

/// <summary>
/// Binds a string path → system icon as <see cref="BitmapImage"/>.
/// Pass the desired <see cref="IconSize"/> as ConverterParameter ("Small" / "Normal" / "Large").
/// </summary>
public sealed class IconConverter : IValueConverter {
    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture) {
        return value is string path ? Load(path, ParseSize(parameter as string)) : null;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }


    public static BitmapImage? Load(string path, IconSize size) {
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        byte[]? bytes = ServiceLocator.Get<IIconProvider>().GetIcon(path, size);
        return bytes is null ? null : ToImage(bytes);
    }


    /// <summary>
    /// Decodes icon bytes into a frozen bitmap. Frozen, so a background
    /// loader (<c>AsyncIcon</c>) can build it off the UI thread and hand
    /// the result over.
    /// </summary>
    public static BitmapImage ToImage(byte[] bytes) {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        image.EndInit();
        image.Freeze();
        return image;
    }


    private static IconSize ParseSize(string? p) {
        return p switch {
            "Small" => IconSize.Small,
            "Large" => IconSize.Large,
            _ => IconSize.Normal,
        };
    }
}
