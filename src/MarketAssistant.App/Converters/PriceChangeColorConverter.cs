using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace MarketAssistant.Converters;

/// <summary>
/// 价格变化颜色转换器：上涨红、下跌绿、持平灰。
/// 颜色从设计系统资源（BullishBrush/BearishBrush/NeutralBrush）取值，取不到时回退到内置常量。
/// 传入 ConverterParameter="tag" 时返回对应约 12% 透明度的标签背景画刷（涨跌标签色块）。
/// </summary>
public class PriceChangeColorConverter : IValueConverter
{
    private const string BullishFallback = "#F44336";
    private const string BearishFallback = "#4CAF50";
    private const string NeutralFallback = "#9E9E9E";
    private const string BullishTagFallback = "#1FF44336";
    private const string BearishTagFallback = "#1F4CAF50";
    private const string NeutralTagFallback = "#149E9E9E";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        decimal? percentage = value switch
        {
            decimal priceChange => priceChange,
            string percentageStr when !string.IsNullOrEmpty(percentageStr)
                // 固定使用 InvariantCulture 解析，避免区域设置（如 de-DE 的小数逗号）导致解析失败而变灰
                => decimal.TryParse(percentageStr.Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
            _ => null
        };

        bool asTagBackground = string.Equals(parameter as string, "tag", StringComparison.OrdinalIgnoreCase);

        return percentage switch
        {
            > 0 => ResolveBrush(
                asTagBackground ? "BullishTagBackgroundBrush" : "BullishBrush",
                asTagBackground ? BullishTagFallback : BullishFallback),
            < 0 => ResolveBrush(
                asTagBackground ? "BearishTagBackgroundBrush" : "BearishBrush",
                asTagBackground ? BearishTagFallback : BearishFallback),
            _ => ResolveBrush(
                asTagBackground ? "NeutralTagBackgroundBrush" : "NeutralBrush",
                asTagBackground ? NeutralTagFallback : NeutralFallback)
        };
    }

    private static IBrush ResolveBrush(string resourceKey, string fallbackHex)
    {
        if (Application.Current?.TryGetResource(resourceKey, Application.Current.RequestedThemeVariant, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
