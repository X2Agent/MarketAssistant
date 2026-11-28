using AdaptiveCards;
using Avalonia.Data;
using Avalonia.Data.Converters;
using MarketAssistant.Infrastructure.AdaptiveCards.Parsers;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarketAssistant.Infrastructure.AdaptiveCards;

/// <summary>
/// 自适应卡片转换器
/// 在边界处一次性从分析师回复中提取 JSON（容忍前后说明文字与 markdown 代码块），
/// 再使用责任链模式尝试将其解析为特定类型的自适应卡片
/// </summary>
public class AdaptiveCardConverter(
    IEnumerable<IJsonToAdaptiveCardParser> parsers,
    ILogger<AdaptiveCardConverter> logger) : IValueConverter
{
    private readonly List<IJsonToAdaptiveCardParser> _parsers = [.. parsers];

    /// <summary>
    /// 将消息文本转换为卡片。没有可识别的结构化结果时返回 null；
    /// 命中某个解析器却解析失败时记录 Warning 并同样返回 null，由调用方回退为原文展示。
    /// </summary>
    public AdaptiveCard? Convert(string text)
    {
        if (!LlmJsonExtractor.TryExtractJson(text, out var json))
        {
            return null;
        }

        foreach (var parser in _parsers)
        {
            try
            {
                if (parser.TryParse(json, out var card))
                {
                    return card;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Parser} 解析分析结果失败，回退为原文显示", parser.GetType().Name);
            }
        }

        return null;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            return Convert(text);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
