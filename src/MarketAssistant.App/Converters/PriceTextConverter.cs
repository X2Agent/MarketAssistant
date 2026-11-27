using System.Globalization;
using Avalonia.Data.Converters;
using MarketAssistant.Applications.Assets;

namespace MarketAssistant.Converters;

/// <summary>
/// 价格量级格式化转换器（仅用于币安交易视图）：按价格大小选择小数位（≥1000 取 2 位，≥1 取 4 位，&lt;1 取 6 位），适配低价币。
/// </summary>
public class PriceTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is decimal price ? PriceFormatter.Format(price, MarketType.Crypto) : value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
