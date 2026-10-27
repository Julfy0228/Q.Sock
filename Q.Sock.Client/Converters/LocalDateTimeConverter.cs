using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Q.Sock.Client.Converters;

public sealed class LocalDateTimeConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) =>
        value switch
        {
            DateTimeOffset timestamp => timestamp.ToLocalTime(),
            DateTime timestamp => new DateTimeOffset(
                DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToLocalTime()
            ),
            _ => value,
        };

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
