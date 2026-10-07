using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// Visible when <c>value.ToString()</c> is the converter parameter,
/// Collapsed otherwise. Works with any enum (or any value where ToString
/// is the discriminator).
///
/// <para>
/// The parameter may name several values with <c>|</c> between them -
/// <c>Video|Audio</c> - for an element that is up for more than one kind:
/// one binding in place of a style with a trigger per kind.
/// </para>
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is null || parameter is not string target) {
            return Visibility.Collapsed;
        }

        string actual = value.ToString() ?? "";
        bool matches = target.Contains('|')
            ? target.Split('|').Contains(actual, StringComparer.Ordinal)
            : string.Equals(actual, target, StringComparison.Ordinal);

        return matches ? Visibility.Visible : Visibility.Collapsed;
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
