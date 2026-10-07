using System.Globalization;
using System.Windows.Data;

namespace Wander.App.Converters;

/// <summary>
/// <c>true</c> when <c>value.ToString()</c> equals the converter parameter:
/// the glue between an enum property and whatever is "checked" for one of
/// its values - a menu row, bound one way, or a group of radio buttons,
/// bound two ways.
///
/// <para>
/// The unchecking half is the part that matters for the radio group. It
/// raises <c>IsChecked = false</c> on the button that lost as well as
/// <c>true</c> on the one that won, and in an unpredictable order - so a
/// naive ConvertBack would write the losing value back over the winning
/// one. Returning <see cref="Binding.DoNothing"/> for false is what makes
/// the group behave like the single choice it looks like.
/// </para>
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is not null
            && parameter is string target
            && string.Equals(value.ToString(), target, StringComparison.Ordinal);
    }


    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not true || parameter is not string target) {
            return Binding.DoNothing;
        }

        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return type.IsEnum && Enum.TryParse(type, target, out object? parsed)
            ? parsed
            : Binding.DoNothing;
    }
}
