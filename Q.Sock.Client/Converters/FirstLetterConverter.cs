using System.Globalization;
using Avalonia.Data.Converters;

namespace Q.Sock.Client.Converters;

public class FirstLetterConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && !string.IsNullOrEmpty(text))
            return text[0].ToString().ToUpperInvariant();

        return "?";
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        throw new NotImplementedException();
    }
}
