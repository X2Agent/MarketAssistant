using System;
using System.Collections.Generic;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 分析师 Agent 的 ASCII 标识名（如 <c>FundamentalAnalyst</c>）与中文显示名（如"基本面分析师"）之间的映射。
/// 聊天侧栏消息的 <c>AuthorName</c> 是 ASCII 名，需翻译为用户可读的显示名。
/// </summary>
public static class AnalystDisplayNameMapper
{
    /// <summary>
    /// ASCII Agent Name → 中文显示名（与各分析师 YAML config.name / 类型 DisplayName 特性保持一致）
    /// </summary>
    private static readonly Dictionary<string, string> NameToDisplayName = new(StringComparer.Ordinal)
    {
        ["FundamentalAnalyst"] = "基本面分析师",
        ["FinancialAnalyst"] = "财务分析师",
        ["TechnicalAnalyst"] = "技术分析师",
        ["MarketSentimentAnalyst"] = "市场情绪分析师",
        ["NewsEventAnalyst"] = "新闻事件分析师",
        ["CryptoMetricsAnalyst"] = "项目指标分析师",
        ["CoordinatorAnalyst"] = "协调分析师",
        ["MarketAnalysisAssistant"] = "市场分析助手",
    };

    /// <summary>
    /// 将 ASCII Agent Name 翻译为中文显示名；未登记的名称原样返回
    /// </summary>
    public static string GetDisplayName(string? asciiName)
        => !string.IsNullOrEmpty(asciiName) && NameToDisplayName.TryGetValue(asciiName, out var displayName)
            ? displayName
            : asciiName ?? string.Empty;

    /// <summary>
    /// 判断给定名称是否为已登记的分析师/协调者 ASCII 名
    /// </summary>
    public static bool IsKnownAnalystName(string? asciiName)
        => !string.IsNullOrEmpty(asciiName) && NameToDisplayName.ContainsKey(asciiName);
}