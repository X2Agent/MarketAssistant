using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Infrastructure.Core;
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

    /// <summary>
    /// 本次分析所属市场。历史报告（该字段上线前归档）为 <c>null</c>，表示市场未知；
    /// 回测与绩效统计据此选择 K 线源与基准，缺失时不做推断填充。
    /// </summary>
    public MarketType? MarketType { get; init; }

    public List<ChatMessage> AnalystMessages { get; init; } = [];

    /// <summary>
    /// Coordinator 的综合分析结果（结构化数据）
    /// 这是经过 AI 智能聚合、冲突解决、搜索验证后的最终投资建议
    /// 唯一包含结构化数据的部分，用于前端 UI 展示
    /// </summary>
    public CoordinatorResult CoordinatorResult { get; init; } = new();

    /// <summary>
    /// 各分析师的机器可读信号快照（评级/操作方向/目标价位）。
    /// 由工作流在生成报告时从分析师结构化输出提取，是回测与分析师绩效统计的数据基础；
    /// 历史报告或提取失败的分析师不出现在列表中。
    /// </summary>
    public List<AnalystSignalSnapshot> AnalystSignals { get; init; } = [];

    /// <summary>
    /// 本次分析运行的 Token 用量汇总（总计 + 按分析师明细）。
    /// 由工作流在运行结束后回填；历史报告或未统计时为 <c>null</c>。
    /// </summary>
    public AnalysisTokenUsage? Usage { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

