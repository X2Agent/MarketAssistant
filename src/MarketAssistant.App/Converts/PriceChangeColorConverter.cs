using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MarketAssistant.Converts
{
    /// <summary>
    /// 价格变化颜色转换器
    /// </summary>
    public class PriceChangeColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // 处理decimal类型输入
            if (value is decimal priceChange)
            {
                // 价格上涨显示红色，下跌显示绿色
                if (priceChange > 0)
                    return new SolidColorBrush(Color.FromRgb(231, 76, 60)); // 红色 #e74c3c
                else if (priceChange < 0)
                    return new SolidColorBrush(Color.FromRgb(46, 204, 113)); // 绿色 #2ecc71
                else
                    return new SolidColorBrush(Color.FromRgb(108, 117, 125)); // 灰色 #6c757d
            }
            
            // 处理字符串类型输入（如："+1.26%"或"-0.83%"）
            if (value is string percentageStr && !string.IsNullOrEmpty(percentageStr))
            {
                // 移除百分号和其他非数字字符，保留正负号
                string numStr = percentageStr.Replace("%", "").Trim();
                if (decimal.TryParse(numStr, out decimal percentage))
                {
                    if (percentage > 0)
                        return new SolidColorBrush(Color.FromRgb(231, 76, 60)); // 红色 #e74c3c
                    else if (percentage < 0)
                        return new SolidColorBrush(Color.FromRgb(46, 204, 113)); // 绿色 #2ecc71
                    else
                        return new SolidColorBrush(Color.FromRgb(108, 117, 125)); // 灰色 #6c757d
                }
            }

            return Brushes.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}