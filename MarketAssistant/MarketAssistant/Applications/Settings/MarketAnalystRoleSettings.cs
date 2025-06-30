namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 市场分析师角色配置
/// </summary>
/// <remarks>
/// 用于配置哪些分析师角色参与市场分析讨论
/// </remarks>
public class MarketAnalystRoleSettings
{
    /// <summary>
    /// 分析综合师（必须启用，负责整合和总结分析结果并输出结构化投资建议）
    /// </summary>
    public bool EnableAnalysisSynthesizer => true;

    /// <summary>
    /// 基本面分析师 - 整合了策略分析师和股票研究分析师的功能
    /// </summary>
    public bool EnableFundamentalAnalyst => true;

    /// <summary>
    /// 市场情绪分析师 - 整合了行为金融分析师和市场分析师的功能
    /// </summary>
    public bool EnableMarketSentimentAnalyst { get; set; } = false;

    /// <summary>
    /// 财务分析师（专注于财务报表和财务健康分析）
    /// </summary>
    public bool EnableFinancialAnalyst { get; set; } = true;

    /// <summary>
    /// 技术分析师（专注于图表模式和技术指标分析）
    /// </summary>
    public bool EnableTechnicalAnalyst { get; set; } = true;

    /// <summary>
    /// 新闻事件分析师（专注于新闻事件对股票的影响分析）
    /// </summary>
    public bool EnableNewsEventAnalyst { get; set; } = false;
}