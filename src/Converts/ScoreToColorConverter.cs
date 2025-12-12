using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace MarketAssistant.Converts;

/// <summary>
/// 根据分数返回颜色的转换器
/// </summary>
public class ScoreToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double score)
        {
            return GetColor(score);
        }
        if (value is float fScore)
        {
            return GetColor(fScore);
        }
        if (value is decimal dScore)
        {
            return GetColor((double)dScore);
        }

        // 默认颜色
        return Brushes.Gray;
    }

    private IBrush GetColor(double score)
    {
        // 0-10分制
        if (score >= 8) return Brushes.ForestGreen; // 优秀
        if (score >= 6) return Brushes.Orange;      // 良好/中等
        if (score >= 4) return Brushes.DarkOrange;  // 一般
        return Brushes.Red;                         // 差
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
