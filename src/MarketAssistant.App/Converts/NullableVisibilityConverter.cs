using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converts;

/// <summary>
/// 可空值可见性转换器 - 当值为 null 时返回 false，用于 IsVisible 绑定
/// </summary>
public class NullableVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 检查是否为 null
        if (value == null)
            return false;

        // 检查字符串是否为空
        if (value is string str && string.IsNullOrWhiteSpace(str))
            return false;

        // 对于值类型，检查是否有值
        var type = value.GetType();
        
        // 如果是 Nullable<T> 类型
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

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("NullableVisibilityConverter does not support ConvertBack");
    }
}
