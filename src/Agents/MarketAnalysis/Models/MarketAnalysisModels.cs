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
/// Coordinator 的综合分析结果（结构化数据，可用于 JSON 反序列化）
/// 只有 Coordinator 提供结构化的投资建议，用于前端 UI 展示
/// </summary>
public sealed class CoordinatorResult
{
    // === 结构化投资建议（用于 UI 卡片展示） ===
    
    /// <summary>
    /// 综合评分（1-10分）
    /// </summary>
    public float OverallScore { get; set; }
    
    /// <summary>
    /// 投资评级（强烈买入/买入/持有/减持/卖出）
    /// </summary>
    public string InvestmentRating { get; set; } = string.Empty;
    
    /// <summary>
    /// 目标价格区间（综合判断）
    /// </summary>
    public string TargetPrice { get; set; } = string.Empty;
    
    /// <summary>
    /// 价格变化预期（综合判断）
    /// </summary>
    public string PriceChangeExpectation { get; set; } = string.Empty;
    
    /// <summary>
    /// 投资时间维度（建议的持有周期）
    /// </summary>
    public string TimeHorizon { get; set; } = string.Empty;
    
    /// <summary>
    /// 风险等级（低风险/中风险/高风险）
    /// </summary>
    public string RiskLevel { get; set; } = string.Empty;
    
    /// <summary>
    /// 置信度百分比（0-100）
    /// </summary>
    public float ConfidencePercentage { get; set; }
    
    /// <summary>
    /// 综合维度评分（整合各分析师的专业意见）
    /// 例如：{"基本面": 8.5, "技术面": 7.0, "市场情绪": 6.5}
    /// </summary>
    public Dictionary<string, float> DimensionScores { get; set; } = new();
    
    /// <summary>
    /// 投资亮点列表（核心优势）
    /// </summary>
    public List<string> InvestmentHighlights { get; set; } = new();
    
    /// <summary>
    /// 风险因素列表（关键风险）
    /// </summary>
    public List<string> RiskFactors { get; set; } = new();
    
    /// <summary>
    /// 操作建议列表（具体可执行的建议）
    /// </summary>
    public List<string> OperationSuggestions { get; set; } = new();
    
    /// <summary>
    /// 一句话总结（30字以内）
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// 核心共识分析（各分析师一致认同的观点）
    /// </summary>
    public string ConsensusAnalysis { get; set; } = string.Empty;
    
    /// <summary>
    /// 主要分歧分析（各分析师意见不一致的地方及 Coordinator 的综合判断）
    /// </summary>
    public string DisagreementAnalysis { get; set; } = string.Empty;
    
    /// <summary>
    /// 关键指标列表（从各专业分析师的分析中提取的核心数据点和建议）
    /// </summary>
    public List<KeyIndicator> KeyIndicators { get; set; } = new();
}

/// <summary>
/// 关键指标（从分析师的自然语言分析中提取）
/// </summary>
public sealed class KeyIndicator
{
    /// <summary>
    /// 分析师来源（技术分析师/财务分析师/基本面分析师等）
    /// </summary>
    public string AnalystSource { get; set; } = string.Empty;
    
    /// <summary>
    /// 指标类别（技术指标/财务数据/估值指标/市场数据）
    /// </summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>
    /// 指标名称（MACD/ROE/PE/市场份额）
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 指标值（金叉/15.2%/25倍/行业第三）
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// 信号/判断（买入/健康/合理/强势）
    /// </summary>
    public string Signal { get; set; } = string.Empty;
    
    /// <summary>
    /// 具体建议（短期目标价50元/继续关注）
    /// </summary>
    public string Suggestion { get; set; } = string.Empty;
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
    /// 各专业分析师的消息（直接使用 ChatMessage）
    /// 包含：基本面分析师、技术分析师、财务分析师、市场情绪分析师、新闻事件分析师
    /// </summary>
    public List<ChatMessage> AnalystMessages { get; init; } = new();
    
    /// <summary>
    /// Coordinator 的综合分析结果（结构化数据）
    /// 这是经过 AI 智能聚合、冲突解决、搜索验证后的最终投资建议
    /// 唯一包含结构化数据的部分，用于前端 UI 展示
    /// </summary>
    public CoordinatorResult CoordinatorResult { get; init; } = new();
    
    /// <summary>
    /// 完整的对话历史（用于审计和调试）
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