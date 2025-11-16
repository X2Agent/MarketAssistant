using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 市场分析工作流请求
/// </summary>
public sealed class MarketAnalysisRequest
{
    public string StockSymbol { get; init; } = string.Empty;

    /// <summary>
    /// 预期的分析师数量
    /// </summary>
    public int ExpectedAnalystCount { get; init; }
}

/// <summary>
/// 聚合后的分析结果
/// </summary>
public sealed class AggregatedAnalysisResult
{
    public MarketAnalysisRequest OriginalRequest { get; init; } = new();
    
    /// <summary>
    /// 各专业分析师的消息（直接使用 ChatMessage，无需额外转换）
    /// </summary>
    public List<ChatMessage> AnalystMessages { get; init; } = new();
}

/// <summary>
/// 最终的市场分析报告
/// </summary>
public sealed class MarketAnalysisReport
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string StockSymbol { get; init; } = string.Empty;
    
    /// <summary>
    /// 各专业分析师的消息（不包含 Coordinator）
    /// 包含：基本面分析师、技术分析师、财务分析师、市场情绪分析师、新闻事件分析师（共5条消息）
    /// 用途：在 UI 中展示各分析师的专业分析内容
    /// </summary>
    public List<ChatMessage> AnalystMessages { get; init; } = new();
    
    /// <summary>
    /// Coordinator 的综合分析结果（结构化数据）
    /// 这是经过 AI 智能聚合、冲突解决、搜索验证后的最终投资建议
    /// 唯一包含结构化数据的部分，用于前端 UI 展示
    /// </summary>
    public CoordinatorResult CoordinatorResult { get; init; } = new();
    
    /// <summary>
    /// 完整的对话历史（包含 Coordinator 的最终消息）
    /// 内容：AnalystMessages（5条）+ Coordinator 最终消息（1条）= 共6条消息
    /// 用途：用于审计、调试和完整的对话回放
    /// 注意：ChatHistory = AnalystMessages + CoordinatorMessage
    /// </summary>
    public IList<ChatMessage> ChatHistory { get; init; } = new List<ChatMessage>();
    
    /// <summary>
    /// 报告创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 分析进度变化事件参数
/// </summary>
public sealed class AnalysisProgressEventArgs : EventArgs
{
    /// <summary>
    /// 当前工作的分析师名称
    /// </summary>
    public string CurrentAnalyst { get; set; } = string.Empty;

    /// <summary>
    /// 当前阶段描述
    /// </summary>
    public string StageDescription { get; set; } = string.Empty;

    /// <summary>
    /// 是否正在进行中
    /// </summary>
    public bool IsInProgress { get; set; } = true;
}

