using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 从分析师的结构化 JSON 输出中提取机器可读信号快照（<see cref="AnalystSignalSnapshot"/>），
/// 作为回测与分析师绩效统计（M3）的数据基础。提取失败（非 JSON、作者未知、失败标记）返回 null，
/// 绝不因信号提取问题阻断报告生成。
/// </summary>
public static class AnalystSignalParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 从单条分析师消息提取信号快照；解析失败或作者不产出方向性信号时返回 null。
    /// </summary>
    public static AnalystSignalSnapshot? Parse(string? analystName, string? content)
    {
        if (string.IsNullOrWhiteSpace(analystName) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (AnalystFailureMessages.IsFailureMarker(content))
        {
            return null;
        }

        return analystName switch
        {
            AnalystNames.Technical => MapTechnical(analystName, content),
            AnalystNames.Fundamental => MapFundamental(analystName, content),
            AnalystNames.Financial => MapFinancial(analystName, content),
            AnalystNames.Sentiment => MapSentiment(analystName, content),
            AnalystNames.News => MapNews(analystName, content),
            _ => null
        };
    }

    /// <summary>
    /// 从消息集合中批量提取信号快照（按消息顺序，跳过 Coordinator 与系统消息）。
    /// </summary>
    public static List<AnalystSignalSnapshot> ExtractSignals(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var snapshots = new List<AnalystSignalSnapshot>();
        foreach (var message in messages)
        {
            var snapshot = Parse(message.AuthorName, message.Text);
            if (snapshot != null)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    private static AnalystSignalSnapshot? MapTechnical(string analystName, string content)
    {
        var result = TryDeserialize<TechnicalAnalysisResult>(content);
        if (result is null)
        {
            return null;
        }

        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            Rating = result.Strategy?.TechnicalRating,
            Operation = result.Strategy?.OperationDirection,
            TargetPriceLow = result.Strategy?.TargetPriceLow,
            TargetPriceHigh = result.Strategy?.TargetPriceHigh,
            StopLossPrice = result.Strategy?.StopLossPrice
        };
    }

    private static AnalystSignalSnapshot? MapFundamental(string analystName, string content)
    {
        var result = TryDeserialize<FundamentalAnalysisResult>(content);
        if (result is null)
        {
            return null;
        }

        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            Rating = result.GrowthValue?.InvestmentRating
        };
    }

    private static AnalystSignalSnapshot? MapFinancial(string analystName, string content)
    {
        var result = TryDeserialize<FinancialAnalysisResult>(content);
        if (result is null)
        {
            return null;
        }

        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            Rating = result.FinancialRating
        };
    }

    private static AnalystSignalSnapshot? MapSentiment(string analystName, string content)
    {
        var result = TryDeserialize<MarketSentimentAnalysisResult>(content);
        if (result is null)
        {
            return null;
        }

        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            Operation = result.ShortTermStrategy?.OperationRecommendation
        };
    }

    private static AnalystSignalSnapshot? MapNews(string analystName, string content)
    {
        var result = TryDeserialize<NewsEventAnalysisResult>(content);
        if (result is null)
        {
            return null;
        }

        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            Rating = MapEventNatureToRating(result.EventAnalysis?.EventNature ?? EventNature.Neutral)
        };
    }

    /// <summary>
    /// 反序列化分析师 JSON 输出；任何解析失败均返回 null（信号提取绝不阻断报告生成）。
    /// </summary>
    private static TResult? TryDeserialize<TResult>(string content)
        where TResult : class
    {
        try
        {
            return LlmJsonExtractor.Deserialize<TResult>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 事件性质到投资评级的等价映射（用于绩效统计时统一方向口径）。
    /// </summary>
    private static InvestmentRating MapEventNatureToRating(EventNature eventNature) => eventNature switch
    {
        EventNature.MajorPositive => InvestmentRating.StrongBuy,
        EventNature.Positive => InvestmentRating.Buy,
        EventNature.Negative => InvestmentRating.Reduce,
        EventNature.MajorNegative => InvestmentRating.StrongSell,
        _ => InvestmentRating.Hold
    };
}
