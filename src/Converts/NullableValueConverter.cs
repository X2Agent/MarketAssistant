using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converts;

/// <summary>
/// 可空值转换器 - 用于格式化可能为 null 的数值
/// </summary>
public class NullableValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 参数格式：format|fallback
        // 例如："{0:F2}元|待定" 或 "+{0:F1}%|--"
        var parameterStr = parameter as string ?? "{0}|--";
        var parts = parameterStr.Split('|');
        var format = parts.Length > 0 ? parts[0] : "{0}";
        var fallback = parts.Length > 1 ? parts[1] : "--";

        if (value == null)
            return fallback;

        // 处理 decimal?
        if (value is decimal decimalValue)
        {
            return string.Format(culture, format, decimalValue);
        }

        // 处理 float?
        if (value is float floatValue)
        {
            return string.Format(culture, format, floatValue);
        }

        // 处理 int?
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
