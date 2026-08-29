using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

public sealed class MarketAnalysisReport
{
    private readonly string _assetSymbol = string.Empty;

    public string AssetSymbol
    {
        get => _assetSymbol;
        init => _assetSymbol = value;
    }

    public List<ChatMessage> AnalystMessages { get; init; } = new();

    /// <summary>
    /// Coordinator 的综合分析结果（结构化数据）
    /// 这是经过 AI 智能聚合、冲突解决、搜索验证后的最终投资建议
    /// 唯一包含结构化数据的部分，用于前端 UI 展示
    /// </summary>
    public CoordinatorResult CoordinatorResult { get; init; } = new();

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

