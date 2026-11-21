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
        // 装箱后的 Nullable<T> 要么是 null 要么是 T，无需单独处理
        return value is not null && !(value is string text && string.IsNullOrWhiteSpace(text));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("NullableVisibilityConverter does not support ConvertBack");
    }
}
