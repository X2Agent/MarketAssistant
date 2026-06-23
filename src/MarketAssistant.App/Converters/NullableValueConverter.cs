using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converters;

/// <summary>
/// �ɿ�ֵת���� - ���ڸ�ʽ������Ϊ null ����ֵ
/// </summary>
public class NullableValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ������ʽ��format|fallback
        // ���磺"{0:F2}Ԫ|����" �� "+{0:F1}%|--"
        var parameterStr = parameter as string ?? "{0}|--";
        var parts = parameterStr.Split('|');
        var format = parts.Length > 0 ? parts[0] : "{0}";
        var fallback = parts.Length > 1 ? parts[1] : "--";

        if (value == null)
            return fallback;

        // ���� decimal?
        if (value is decimal decimalValue)
        {
            return string.Format(culture, format, decimalValue);
        }

        // ���� float?
        if (value is float floatValue)
        {
            return string.Format(culture, format, floatValue);
        }

        // ���� int?
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
