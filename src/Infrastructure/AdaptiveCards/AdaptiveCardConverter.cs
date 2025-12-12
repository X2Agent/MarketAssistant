using AdaptiveCards;
using Avalonia.Data.Converters;
using MarketAssistant.Infrastructure.AdaptiveCards.Parsers;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarketAssistant.Infrastructure.AdaptiveCards;

/// <summary>
/// 自适应卡片转换器
/// 使用责任链模式尝试将 JSON 字符串解析为特定类型的自适应卡片
/// </summary>
public class AdaptiveCardConverter : IValueConverter
{
    private readonly List<IJsonToAdaptiveCardParser> _parsers;

    public AdaptiveCardConverter()
    {
        _parsers = new List<IJsonToAdaptiveCardParser>
        {
            new CoordinatorCardParser(),
            new FinancialCardParser(),
            new FundamentalCardParser(),
            new SentimentCardParser(),
            new NewsCardParser(),
            new TechnicalCardParser()
        };
    }

    public AdaptiveCard? Convert(string json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            foreach (var parser in _parsers)
            {
                if (parser.TryParse(json, out var card))
                {
                    return card;
                }
            }
        }
        return null;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string json)
        {
            return Convert(json);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
