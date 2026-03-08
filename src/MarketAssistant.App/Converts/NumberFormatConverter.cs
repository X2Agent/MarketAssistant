using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converts;

public class NumberFormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string strValue && double.TryParse(strValue, out double number))
        {
            return FormatNumber(number);
        }
        if (value is double doubleValue)
        {
            return FormatNumber(doubleValue);
        }
        if (value is int intValue)
        {
            return FormatNumber(intValue);
        }
        if (value is long longValue)
        {
            return FormatNumber(longValue);
        }

        return value;
    }

    private string FormatNumber(double number)
    {
        if (number >= 100000000)
        {
            return $"{(number / 100000000):F2}亿";
        }
        if (number >= 10000)
        {
            return $"{(number / 10000):F2}万";
        }
        return number.ToString("N0");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
