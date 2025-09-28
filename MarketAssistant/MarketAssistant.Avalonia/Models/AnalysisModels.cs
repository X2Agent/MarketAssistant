namespace MarketAssistant.Views.Models
{
    /// <summary>
    /// 分析师结果数据模型 - 综合协调分析师，整合各维度分析统一各类分析师的输出结果
    /// </summary>
    public class AnalystResult
    {
        /// <summary>
        /// 分析师共识信息 - 各分析师的一致性观点
        /// </summary>
        /// <remarks>
        /// 用途：了解分析师之间的共识程度
        /// 提供者：CoordinatorAnalystAgent(专用)
        /// 重要性：中 - 决策可靠性参考
        /// 示例："公司基本面稳健，长期发展前景良好 共识度评分8分"
        /// </remarks>
        public string ConsensusInfo { get; set; } = string.Empty;

        /// <summary>
        /// 分析师分歧信息 - 各分析师的不同观点
        /// </summary>
        /// <remarks>
        /// 用途：识别分析中的不确定性和争议点
        /// 提供者：CoordinatorAnalystAgent(专用)
        /// 重要性：中 - 风险识别参考
        /// 示例："短期目标价位存在分歧，技术面分析师相对谨慎"
        /// </remarks>
        public string DisagreementInfo { get; set; } = string.Empty;


        #region 通用评分数据 (所有分析师都会提供)

        /// <summary>
        /// 维度评分字典 - 各分析师的核心评分维度
        /// </summary>
        /// <remarks>
        /// 用途：存储各分析师的专业评分
        /// 提供者：所有分析师
        /// 示例：{"基本面评估": 8, "技术面评估": 7, "市场情绪评估": 9}
        /// </remarks>
        public Dictionary<string, float> DimensionScores { get; set; } = new();

        /// <summary>
        /// 综合评分 - 分析师给出的总体评分(1-10分)
        /// </summary>
        /// <remarks>
        /// 用途：快速了解分析师对标的的整体看法
        /// 提供者：所有分析师
        /// 重要性：高 - 决策的关键指标
        /// </remarks>
        public float OverallScore { get; set; }

        /// <summary>
        /// 置信度百分比 - 分析师对自己分析结果的信心程度
        /// </summary>
        /// <remarks>
        /// 用途：评估分析结果的可靠性
        /// 提供者：CoordinatorAnalystAgent, TechnicalAnalystAgent
        /// 重要性：中 - 辅助决策参考
        /// </remarks>
        public float ConfidencePercentage { get; set; }

        #endregion

        #region 投资建议数据 (主要由Coordinator和部分专业分析师提供)

        /// <summary>
        /// 投资评级 - 买入/卖出/持有等建议
        /// </summary>
        /// <remarks>
        /// 用途：直接的投资操作建议
        /// 提供者：CoordinatorAnalystAgent(主要), FundamentalAnalystAgent, TechnicalAnalystAgent, MarketSentimentAnalystAgent
        /// 重要性：高 - 最终决策依据
        /// 示例："买入", "卖出", "持有", "强烈买入"
        /// </remarks>
        public string Rating { get; set; } = string.Empty;

        /// <summary>
        /// 目标价格 - 分析师预期的合理价格区间
        /// </summary>
        /// <remarks>
        /// 用途：价格预期和止盈参考
        /// 提供者：CoordinatorAnalystAgent(主要), FundamentalAnalystAgent, TechnicalAnalystAgent
        /// 重要性：高 - 交易策略制定
        /// 示例："50-55元", "335.00-340.00"
        /// </remarks>
        public string TargetPrice { get; set; } = string.Empty;

        /// <summary>
        /// 股票代码 - 分析标的股票代码
        /// </summary>
        /// <remarks>
        /// 用途：标识分析对象
        /// 提供者：所有分析师
        /// 重要性：高 - 分析结果标识
        /// 示例："AAPL", "sz002594"
        /// </remarks>
        public string StockSymbol { get; set; } = string.Empty;

        /// <summary>
        /// 投资评级 - 投资建议评级
        /// </summary>
        /// <remarks>
        /// 用途：投资决策参考
        /// 提供者：所有分析师
        /// 重要性：高 - 投资决策核心
        /// 示例："买入", "卖出", "持有", "强烈买入"
        /// </remarks>
        public string InvestmentRating { get; set; } = string.Empty;

        /// <summary>
        /// 价格变化预期 - 预期的涨跌幅度
        /// </summary>
        /// <remarks>
        /// 用途：收益预期评估
        /// 提供者：主要由CoordinatorAnalystAgent提供，其他分析师也可提供
        /// 重要性：中 - 收益率参考
        /// 示例："上涨空间25% / 下跌风险15%"
        /// </remarks>
        public virtual string PriceChange { get; set; } = string.Empty;

        /// <summary>
        /// 风险等级 - 投资风险评估
        /// </summary>
        /// <remarks>
        /// 用途：风险管理和仓位控制
        /// 提供者：CoordinatorAnalystAgent(主要), TechnicalAnalystAgent, FinancialAnalystAgent
        /// 重要性：高 - 风险控制关键
        /// 示例："低风险", "中风险", "高风险"
        /// </remarks>
        public string RiskLevel { get; set; } = string.Empty;

        #endregion

        #region 分析要点数据 (各分析师的专业见解)

        /// <summary>
        /// 投资亮点 - 支持投资的积极因素
        /// </summary>
        /// <remarks>
        /// 用途：投资逻辑梳理，支撑买入决策
        /// 提供者：CoordinatorAnalystAgent(主要), FundamentalAnalystAgent, NewsEventAnalystAgent
        /// 重要性：高 - 投资逻辑核心
        /// 示例：["行业龙头地位稳固", "技术创新能力强", "财务状况健康"]
        /// </remarks>
        public List<string> InvestmentHighlights { get; set; } = new List<string>();

        /// <summary>
        /// 风险因素 - 需要关注的潜在风险
        /// </summary>
        /// <remarks>
        /// 用途：风险识别和防范
        /// 提供者：所有分析师
        /// 重要性：高 - 风险管理必需
        /// 示例：["市场竞争加剧风险", "宏观经济波动影响", "原材料价格波动"]
        /// </remarks>
        public List<string> RiskFactors { get; set; } = new List<string>();

        /// <summary>
        /// 操作建议 - 具体的交易操作指导
        /// </summary>
        /// <remarks>
        /// 用途：具体的买卖时机和策略指导
        /// 提供者：CoordinatorAnalystAgent(主要), TechnicalAnalystAgent, MarketSentimentAnalystAgent
        /// 重要性：高 - 实际操作指南
        /// 示例：["建议在回调至支撑位45-47元区间分批买入", "止损位42元"]
        /// </remarks>
        public List<string> OperationSuggestions { get; set; } = new List<string>();

        #endregion

        #region 通用分析数据 (重构后的统一数据结构)

        /// <summary>
        /// 通用分析数据项 - 统一的分析数据结构
        /// </summary>
        /// <remarks>
        /// 用途：统一存储各类专业分析数据，提高扩展性和维护性
        /// 提供者：所有专业分析师
        /// 重要性：中 - 专业分析数据的统一载体
        /// 数据类型：技术指标、基本面指标、财务数据、市场情绪、新闻事件等
        /// </remarks>
        public List<AnalysisDataItem> AnalysisData { get; set; } = new List<AnalysisDataItem>();

        #endregion
    }

    /// <summary>
    /// 通用分析数据项 - 统一的分析数据结构
    /// </summary>
    /// <remarks>
    /// 用途：统一存储各类专业分析数据，替代原有的专业化类型
    /// 支持数据类型：技术指标、基本面指标、财务数据、市场情绪、新闻事件等
    /// 优势：提高扩展性、减少代码重复、简化维护
    /// </remarks>
    public class AnalysisDataItem
    {
        /// <summary>
        /// 数据类型 - 标识数据的分类
        /// </summary>
        /// <remarks>
        /// 示例："TechnicalIndicator", "FundamentalIndicator", "FinancialData", "MarketSentiment", "NewsEvent"
        /// </remarks>
        public string DataType { get; set; } = string.Empty;

        /// <summary>
        /// 数据项名称
        /// </summary>
        /// <remarks>
        /// 示例："5日MA", "行业地位", "流动比率", "主力资金", "行业政策"
        /// </remarks>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 数据项值
        /// </summary>
        /// <remarks>
        /// 示例："344.5", "行业龙头", "1.12", "净流出", "比亚迪等布局超快充技术"
        /// </remarks>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// 数据单位（可选）
        /// </summary>
        /// <remarks>
        /// 示例："%", "倍", "万元", "亿元"
        /// </remarks>
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// 信号或趋势（可选）
        /// </summary>
        /// <remarks>
        /// 示例："买入", "卖出", "持续流出", "利好", "中性"
        /// </remarks>
        public string Signal { get; set; } = string.Empty;

        /// <summary>
        /// 影响评估（可选）
        /// </summary>
        /// <remarks>
        /// 示例："利好", "利空", "中性", "重要性7/10"
        /// </remarks>
        public string Impact { get; set; } = string.Empty;

        /// <summary>
        /// 策略建议（可选）
        /// </summary>
        /// <remarks>
        /// 示例："关注技术落地进展", "警惕短期波动"
        /// </remarks>
        public string Strategy { get; set; } = string.Empty;
    }
}