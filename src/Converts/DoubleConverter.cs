using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converts
{
    /// <summary>
    /// 将整数值转换为0-1范围的浮点数，用于进度条显示
    /// </summary>
    public class DoubleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                // 将0-100的整数值转换为0-1的浮点数
                return intValue / 100.0;
            }

            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double doubleValue)
            {
                // 将0-1的浮点数转换为0-100的整数值
                return (int)(doubleValue * 100);
            }

            return 0;
        }
    }
}