using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converters;

/// <summary>
/// 可空值可见性转换器 - 当值为 null 时返回 false，用于 IsVisible 绑定
/// </summary>
public class NullableVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return false;

        if (value is string str && string.IsNullOrWhiteSpace(str))
            return false;

        var type = value.GetType();

        // 处理 Nullable<T> 类型
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var hasValueProperty = type.GetProperty("HasValue");
            if (hasValueProperty != null)
            {
                var hasValue = (bool)(hasValueProperty.GetValue(value) ?? false);
                return hasValue;
            }
        }

        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("NullableVisibilityConverter does not support ConvertBack");
    }
}
