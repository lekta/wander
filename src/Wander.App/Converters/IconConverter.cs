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
        if (value is not string path || string.IsNullOrEmpty(path)) {
            return null;
        }

        if (!ServiceLocator.IsRegistered<IIconProvider>()) {
            return null;
        }

        var size = ParseSize(parameter as string);
        byte[]? bytes = ServiceLocator.Get<IIconProvider>().GetIcon(path, size);
        if (bytes is null) {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }


    private static IconSize ParseSize(string? p) {
        return p switch {
            "Small" => IconSize.Small,
            "Large" => IconSize.Large,
            _ => IconSize.Normal,
        };
    }
}
