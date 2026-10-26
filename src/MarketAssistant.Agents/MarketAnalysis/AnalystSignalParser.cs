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

        using var root = TryParseRoot(content);
        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            // 源字段是非空枚举（缺省即 0 = StrongBuy / Buy），必须"JSON 里确实给出"才映射，
            // 否则模型漏字段会被伪造成最强看多信号
            Rating = HasJsonPath(root, "strategy", "technicalRating") ? result.Strategy?.TechnicalRating : null,
            Operation = HasJsonPath(root, "strategy", "operationDirection") ? result.Strategy?.OperationDirection : null,
            TargetPriceLow = result.Strategy?.TargetPriceLow,
            TargetPriceHigh = result.Strategy?.TargetPriceHigh,
            StopLossPrice = result.Strategy?.StopLossPrice,
            Confidence = NormalizeConfidence(result.Strategy?.ConfidencePercentage),
            Horizon = result.Strategy?.HoldingPeriod
        };
    }

    private static AnalystSignalSnapshot? MapFundamental(string analystName, string content)
    {
        var result = TryDeserialize<FundamentalAnalysisResult>(content);
        if (result is null)
        {
            return null;
        }

        using var root = TryParseRoot(content);
        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            Rating = HasJsonPath(root, "growthValue", "investmentRating") ? result.GrowthValue?.InvestmentRating : null,
            Confidence = NormalizeConfidence(result.GrowthValue?.ConfidencePercentage)
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
            Rating = result.FinancialRating,
            Confidence = NormalizeConfidence(result.ConfidencePercentage)
        };
    }

    private static AnalystSignalSnapshot? MapSentiment(string analystName, string content)
    {
        var result = TryDeserialize<MarketSentimentAnalysisResult>(content);
        if (result is null)
        {
            return null;
        }

        using var root = TryParseRoot(content);
        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            Operation = HasJsonPath(root, "shortTermStrategy", "operationRecommendation")
                ? result.ShortTermStrategy?.OperationRecommendation
                : null,
            Confidence = NormalizeConfidence(result.ShortTermStrategy?.ConfidencePercentage)
        };
    }

    private static AnalystSignalSnapshot? MapNews(string analystName, string content)
    {
        var result = TryDeserialize<NewsEventAnalysisResult>(content);
        if (result is null)
        {
            return null;
        }

        using var root = TryParseRoot(content);
        return new AnalystSignalSnapshot
        {
            AnalystName = analystName,
            // 事件性质缺省为 0 = MajorPositive；未给出时保持 null，
            // 不能用 Neutral 兜底成 Hold（"没有判断"不等于"中性"）
            Rating = HasJsonPath(root, "eventAnalysis", "eventNature") && result.EventAnalysis is { } eventAnalysis
                ? MapEventNatureToRating(eventAnalysis.EventNature)
                : null,
            Confidence = NormalizeConfidence(result.EventAnalysis?.ConfidencePercentage),
            Horizon = result.ImpactEvaluation?.ImpactDuration
        };
    }

    /// <summary>
    /// 置信度归一化到 0-100；模型越界输出（如 150 / -5）钳制而非丢弃，缺失保持 null。
    /// 回测按置信度分桶（0-59/60-79/80-100）校准分析师绩效，越界值会污染分桶。
    /// </summary>
    private static float? NormalizeConfidence(float? confidence)
        => confidence.HasValue ? Math.Clamp(confidence.Value, 0, 100) : null;

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
    /// 解析原始消息的 JSON 结构（不做类型绑定），用于判定"模型是否真的给出了某个字段"。
    /// 非空枚举源字段的缺省值是有效枚举值，只能靠"字段存在性"区分"未提供"与"给了默认值"。
    /// </summary>
    private static JsonDocument? TryParseRoot(string content)
    {
        if (!LlmJsonExtractor.TryExtractJson(content, out var json))
            return null;

        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 按路径（大小写不敏感）判断 JSON 中是否存在该字段；任一层缺失即为不存在。
    /// </summary>
    private static bool HasJsonPath(JsonDocument? document, params string[] path)
    {
        if (document is null)
            return false;

        var current = document.RootElement;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !TryGetProperty(current, segment, out var next))
                return false;

            current = next;
        }

        return true;
    }

    /// <summary>大小写不敏感地查找属性（模型常把 schema 字段名写成 camelCase 或 PascalCase）。</summary>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
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
