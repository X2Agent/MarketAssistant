using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Avalonia.Converts;

/// <summary>
/// 布尔值反转转换器（InvertBoolConverter 别名）
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

