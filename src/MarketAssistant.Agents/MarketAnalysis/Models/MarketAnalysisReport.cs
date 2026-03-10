using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 最终的市场分析报告
/// </summary>
public sealed class MarketAnalysisReport
{
    private readonly string _assetSymbol = string.Empty;

    /// <summary>
    /// 分析标的代码
    /// </summary>
    public string AssetSymbol
    {
        get => _assetSymbol;
        init => _assetSymbol = value;
    }

    /// <summary>
    /// 兼容旧字段名，内部统一使用 AssetSymbol。
    /// </summary>
    [Obsolete("Use AssetSymbol instead.")]
    [JsonPropertyName("stockSymbol")]
    public string StockSymbol
    {
        get => _assetSymbol;
        init => _assetSymbol = value;
    }

    /// <summary>
    /// 各专业分析师的消息
    /// </summary>
    public List<ChatMessage> AnalystMessages { get; init; } = new();

    /// <summary>
    /// Coordinator 的综合分析结果（结构化数据）
    /// 这是经过 AI 智能聚合、冲突解决、搜索验证后的最终投资建议
    /// 唯一包含结构化数据的部分，用于前端 UI 展示
    /// </summary>
    public CoordinatorResult CoordinatorResult { get; init; } = new();

    /// <summary>
    /// 报告创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

