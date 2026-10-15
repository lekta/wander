using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Wander.App.Resources;
using Wander.Core.FileSystem;

namespace Wander.App.Converters;

/// <summary>
/// A drive's capacity bar colour by how full it is (<see cref="VolumeInfo.Fill"/>):
/// the preview's volume block and the computer's rows in the list draw the
/// same three.
/// </summary>
public sealed class VolumeFillBrushConverter : IValueConverter {
    public static Brush BrushOf(VolumeFill fill) {
        return fill switch {
            VolumeFill.Full => Palette.VolumeBarFull,
            VolumeFill.Filling => Palette.VolumeBarFilling,
            _ => Palette.VolumeBarNormal,
        };
    }


    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is VolumeInfo volume ? BrushOf(volume.Fill) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
