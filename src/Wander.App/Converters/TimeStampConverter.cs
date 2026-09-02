using System.Globalization;
using System.Windows.Data;
using Wander.App.Util;

namespace Wander.App.Converters;

/// <summary>
/// A UTC stamp for the table's "Изменён" column, through
/// <see cref="TimeFormat.FromUtc"/> — which means
/// <see cref="DateTime.MinValue"/> shows as a dash instead of the year 1.
///
/// <para>
/// A converter rather than the column's own <c>StringFormat</c>: a folder
/// inside a zip has no date at all (the shell reports none), and
/// "0001-01-01 00:00" reads as a fact about the file rather than as the
/// absence of one.
/// </para>
/// </summary>
public sealed class TimeStampConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return value is DateTime utc ? TimeFormat.FromUtc(utc) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
