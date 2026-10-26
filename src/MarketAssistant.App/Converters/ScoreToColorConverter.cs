using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System.Globalization;

namespace MarketAssistant.Converters;

/// <summary>
/// 根据分数返回颜色的转换器（对齐 Colors.axaml 语义色板）
/// </summary>
public class ScoreToColorConverter : IValueConverter
{
    private static readonly IBrush ExcellentBrush = new ImmutableSolidColorBrush(Color.Parse("#10B981"));
    private static readonly IBrush GoodBrush = new ImmutableSolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush FairBrush = new ImmutableSolidColorBrush(Color.Parse("#EA580C"));
    private static readonly IBrush PoorBrush = new ImmutableSolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush NeutralBrush = new ImmutableSolidColorBrush(Color.Parse("#9E9E9E"));

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

        return NeutralBrush;
    }

    private static IBrush GetColor(double score)
    {
        // 0-10分制
        if (score >= 8) return ExcellentBrush;
        if (score >= 6) return GoodBrush;
        if (score >= 4) return FairBrush;
        return PoorBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
