using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MarketAssistant.Converts
{
    /// <summary>
    /// 布尔值到颜色的转换器
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                if (parameter is string paramString)
                {
                    var colors = paramString.Split(',');
                    if (colors.Length >= 2)
                    {
                        // 尝试解析颜色字符串
                        if (Brush.Parse(colors[0].Trim()) is SolidColorBrush trueBrush &&
                            Brush.Parse(colors[1].Trim()) is SolidColorBrush falseBrush)
                        {
                            return boolValue ? trueBrush : falseBrush;
                        }
                    }
                }
                // 默认颜色
                return boolValue ? Brushes.Green : Brushes.Red;
            }
            return Brushes.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}