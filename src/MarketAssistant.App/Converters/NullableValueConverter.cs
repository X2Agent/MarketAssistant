using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converters;

/// <summary>
/// 可空值格式化转换器 - 当值为 null 时返回回退值
/// </summary>
public class NullableValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 参数格式：format|fallback
        // 例如："{0:F2}元|--" 或 "+{0:F1}%|--"
        var parameterStr = parameter as string ?? "{0}|--";
        var parts = parameterStr.Split('|');
        var format = parts.Length > 0 ? parts[0] : "{0}";
        var fallback = parts.Length > 1 ? parts[1] : "--";

        if (value == null)
            return fallback;

        if (value is decimal decimalValue)
        {
            return string.Format(culture, format, decimalValue);
        }

        if (value is float floatValue)
        {
            return string.Format(culture, format, floatValue);
        }

        if (value is int intValue)
        {
            return string.Format(culture, format, intValue);
        }

        return value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("NullableValueConverter does not support ConvertBack");
    }
}
