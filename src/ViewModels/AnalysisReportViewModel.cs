using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Agents;
using MarketAssistant.Models;
using MarketAssistant.Parsers;
using MarketAssistant.Services.Cache;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 分析报告视图模型
/// </summary>
public partial class AnalysisReportViewModel : ViewModelBase
{
    private readonly IAnalystDataParser _analystDataParser;
    private readonly IAnalysisCacheService _analysisCacheService;

    [ObservableProperty]
    private bool _isReportVisible;

    [ObservableProperty]
    private string _stockSymbol = string.Empty;

    [ObservableProperty]
    private string _targetPrice = string.Empty;

    [ObservableProperty]
    private string _priceChange = string.Empty;

    [ObservableProperty]
    private string _recommendation = string.Empty;

    [ObservableProperty]
    private string _riskLevel = string.Empty;

    [ObservableProperty]
    private string _confidenceLevel = string.Empty;

    [ObservableProperty]
    private float _overallScore;

    [ObservableProperty]
    private string _consensusInfo = string.Empty;

    [ObservableProperty]
    private string _disagreementInfo = string.Empty;

    public ObservableCollection<AnalysisDataItem> AnalysisData { get; } = new();

    [ObservableProperty]
    private ObservableCollection<AnalysisDataItem> _technicalIndicators = new();

    [ObservableProperty]
    private ObservableCollection<AnalysisDataItem> _fundamentalIndicators = new();

    [ObservableProperty]
    private ObservableCollection<AnalysisDataItem> _financialData = new();

    [ObservableProperty]
    private ObservableCollection<AnalysisDataItem> _marketSentimentData = new();

    [ObservableProperty]
    private ObservableCollection<AnalysisDataItem> _newsEventData = new();

    public ObservableCollection<string> InvestmentHighlights { get; } = new();
    public ObservableCollection<string> RiskFactors { get; } = new();
    public ObservableCollection<string> OperationSuggestions { get; } = new();
    public ObservableCollection<ScoreItem> DimensionScores { get; } = new();

    [ObservableProperty]
    private bool _hasConsensusInfo;

    [ObservableProperty]
    private bool _hasDisagreementInfo;

    [ObservableProperty]
    private bool _hasConsensusOrDisagreement;

    [ObservableProperty]
    private string _scorePercentage = "0/10";

    public AnalysisReportViewModel(IAnalystDataParser analystDataParser,
         IAnalysisCacheService analysisCacheService,
        ILogger<AnalysisReportViewModel> logger)
        : base(logger)
    {
        _analystDataParser = analystDataParser;
        _analysisCacheService = analysisCacheService;
    }

    partial void OnConsensusInfoChanged(string value)
    {
        HasConsensusInfo = !string.IsNullOrEmpty(value);
        UpdateConsensusOrDisagreement();
    }

    partial void OnDisagreementInfoChanged(string value)
    {
        HasDisagreementInfo = !string.IsNullOrEmpty(value);
        UpdateConsensusOrDisagreement();
    }

    partial void OnOverallScoreChanged(float value)
    {
        ScorePercentage = $"{value}/10";
    }

    /// <summary>
    /// 更新组合状态
    /// </summary>
    private void UpdateConsensusOrDisagreement()
    {
        HasConsensusOrDisagreement = HasConsensusInfo || HasDisagreementInfo;
    }

    /// <summary>
    /// 更新分类数据集合的缓存
    /// </summary>
    private void UpdateFilteredCollections()
    {
        var technicalItems = AnalysisData.Where(x =>
            x.DataType.Contains("Technical", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("技术", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("Indicator", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("指标", StringComparison.OrdinalIgnoreCase)).ToList();

        TechnicalIndicators.Clear();
        foreach (var item in technicalItems)
        {
            TechnicalIndicators.Add(item);
        }

        var fundamentalItems = AnalysisData.Where(x =>
            x.DataType.Contains("Fundamental", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("基本面", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("基础", StringComparison.OrdinalIgnoreCase)).ToList();

        FundamentalIndicators.Clear();
        foreach (var item in fundamentalItems)
        {
            FundamentalIndicators.Add(item);
        }

        var financialItems = AnalysisData.Where(x =>
            x.DataType.Contains("Financial", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("财务", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("Finance", StringComparison.OrdinalIgnoreCase)).ToList();

        FinancialData.Clear();
        foreach (var item in financialItems)
        {
            FinancialData.Add(item);
        }

        var sentimentItems = AnalysisData.Where(x =>
            x.DataType.Contains("Sentiment", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("情绪", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("Market", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("市场", StringComparison.OrdinalIgnoreCase)).ToList();

        MarketSentimentData.Clear();
        foreach (var item in sentimentItems)
        {
            MarketSentimentData.Add(item);
        }

        var newsItems = AnalysisData.Where(x =>
            x.DataType.Contains("News", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("Event", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("新闻", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("事件", StringComparison.OrdinalIgnoreCase)).ToList();

        NewsEventData.Clear();
        foreach (var item in newsItems)
        {
            NewsEventData.Add(item);
        }
    }

    /// <summary>
    /// 处理分析消息
    /// </summary>
    public void ProcessAnalysisMessage(AnalysisMessage message)
    {
        if (message?.Sender == nameof(AnalysisAgents.CoordinatorAnalystAgent))
        {
            _ = ParseAnalystOpinionAsync(message.Content);
        }
    }

    /// <summary>
    /// 异步处理分析消息
    /// </summary>
    public async Task ProcessAnalysisMessageAsync(AnalysisMessage message)
    {
        if (message?.Sender == nameof(AnalysisAgents.CoordinatorAnalystAgent))
        {
            await ParseAnalystOpinionAsync(message.Content);
        }
    }

    private async Task ParseAnalystOpinionAsync(string opinion)
    {
        if (string.IsNullOrEmpty(opinion))
        {
            Logger?.LogWarning("分析师意见为空，无法解析");
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            Logger?.LogInformation("开始解析分析师意见");

            var result = await _analystDataParser.ParseDataAsync(opinion);

            if (result != null)
            {
                UpdateWithResult(result);
                await _analysisCacheService.CacheAnalysisAsync(StockSymbol, result);
            }
        }, "解析分析师意见");
    }

    /// <summary>
    /// 使用分析结果更新视图模型
    /// </summary>
    public void UpdateWithResult(AnalystResult result)
    {
        ClearAllData();

        StockSymbol = result.StockSymbol;
        TargetPrice = result.TargetPrice;
        PriceChange = result.PriceChange;
        Recommendation = !string.IsNullOrEmpty(result.InvestmentRating) ? result.InvestmentRating : result.Rating;
        RiskLevel = result.RiskLevel;
        ConfidenceLevel = result.ConfidencePercentage > 0 ? $"{result.ConfidencePercentage}%" : "--";
        OverallScore = result.OverallScore;

        IsReportVisible = true;

        foreach (var score in result.DimensionScores)
            DimensionScores.Add(new ScoreItem { Name = score.Key, Score = score.Value });

        ConsensusInfo = result.ConsensusInfo;
        DisagreementInfo = result.DisagreementInfo;

        foreach (var highlight in result.InvestmentHighlights)
            InvestmentHighlights.Add(highlight);

        foreach (var risk in result.RiskFactors)
            RiskFactors.Add(risk);

        foreach (var suggestion in result.OperationSuggestions)
            OperationSuggestions.Add(suggestion);

        foreach (var dataItem in result.AnalysisData)
            AnalysisData.Add(dataItem);

        IsReportVisible = true;

        NotifyFilteredCollectionsChanged();

        Logger?.LogInformation($"分析报告更新完成，股票代码：{StockSymbol}，评级：{Recommendation}");
    }

    private void ClearAllData()
    {
        DimensionScores.Clear();
        InvestmentHighlights.Clear();
        RiskFactors.Clear();
        OperationSuggestions.Clear();
        AnalysisData.Clear();
        ConsensusInfo = string.Empty;
        DisagreementInfo = string.Empty;

        HasConsensusInfo = false;
        HasDisagreementInfo = false;
        HasConsensusOrDisagreement = false;
        ScorePercentage = "0/10";

        TechnicalIndicators.Clear();
        FundamentalIndicators.Clear();
        FinancialData.Clear();
        MarketSentimentData.Clear();
        NewsEventData.Clear();
    }

    private void NotifyFilteredCollectionsChanged()
    {
        UpdateFilteredCollections();
    }

    /// <summary>
    /// 加载模拟数据用于调试和演示
    /// </summary>
    public void LoadMockData(string stockCode)
    {
        ClearAllData();

        StockSymbol = stockCode;
        TargetPrice = "目标价：¥45.80";
        PriceChange = "+15.5%";
        Recommendation = "买入";
        RiskLevel = "中等";
        ConfidenceLevel = "85%";
        OverallScore = 7.8f;

        IsReportVisible = true;

        // 维度评分
        DimensionScores.Add(new ScoreItem { Name = "技术面", Score = 8.2f });
        DimensionScores.Add(new ScoreItem { Name = "基本面", Score = 7.5f });
        DimensionScores.Add(new ScoreItem { Name = "资金面", Score = 7.8f });
        DimensionScores.Add(new ScoreItem { Name = "市场情绪", Score = 8.0f });

        // 投资亮点
        InvestmentHighlights.Add("技术面多头排列，短期趋势明确向上");
        InvestmentHighlights.Add("基本面稳健，盈利能力持续改善");
        InvestmentHighlights.Add("资金面积极，机构资金持续流入");
        InvestmentHighlights.Add("估值合理，仍有上升空间");

        // 风险因素
        RiskFactors.Add("市场整体波动可能影响短期走势");
        RiskFactors.Add("行业竞争加剧，需关注市场份额变化");
        RiskFactors.Add("宏观经济政策调整带来的不确定性");

        // 操作建议
        OperationSuggestions.Add("目标价位：当前价格+15% 作为第一目标");
        OperationSuggestions.Add("止损位：跌破 MA20 考虑减仓");
        OperationSuggestions.Add("持有周期：建议 3-6 个月");
        OperationSuggestions.Add("建议仓位：不超过总资产的 20%");

        // 分析数据 - 技术指标
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "技术指标",
            Name = "MA5",
            Value = "¥39.85",
            Signal = "看多",
            Strategy = "5日均线呈上升趋势"
        });
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "技术指标",
            Name = "MA10",
            Value = "¥38.92",
            Signal = "看多",
            Strategy = "10日均线支撑明显"
        });
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "技术指标",
            Name = "RSI",
            Value = "65",
            Signal = "中性",
            Strategy = "处于相对强势区间"
        });
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "技术指标",
            Name = "MACD",
            Value = "正值放大",
            Signal = "看多",
            Strategy = "柱状图由负转正，动能增强"
        });

        // 分析数据 - 基本面
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "基本面",
            Name = "营收增长率",
            Value = "12.3%",
            Unit = "%",
            Impact = "正面",
            Strategy = "同比增长，盈利能力稳定"
        });
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "基本面",
            Name = "毛利率",
            Value = "35%",
            Unit = "%",
            Impact = "正面",
            Strategy = "成本控制良好"
        });
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "基本面",
            Name = "ROE",
            Value = "15.2%",
            Unit = "%",
            Impact = "正面",
            Strategy = "股东回报率较为理想"
        });

        // 分析数据 - 财务数据
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "财务数据",
            Name = "资产负债率",
            Value = "45%",
            Unit = "%",
            Impact = "正面",
            Strategy = "财务结构健康"
        });
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "财务数据",
            Name = "流动比率",
            Value = "1.8",
            Impact = "正面",
            Strategy = "短期偿债能力强"
        });
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "财务数据",
            Name = "现金流",
            Value = "充裕",
            Impact = "正面",
            Strategy = "经营活动现金流为正"
        });

        // 分析数据 - 市场情绪
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "市场情绪",
            Name = "成交量变化",
            Value = "+20%",
            Unit = "%",
            Signal = "积极",
            Strategy = "较前期放大，资金关注度提升"
        });
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "市场情绪",
            Name = "机构持仓",
            Value = "增持",
            Signal = "积极",
            Strategy = "机构资金持续流入"
        });

        // 分析数据 - 新闻事件
        AnalysisData.Add(new AnalysisDataItem
        {
            DataType = "新闻事件",
            Name = "最新动态",
            Value = "积极",
            Impact = "正面",
            Strategy = "公司发布利好公告，市场反应积极"
        });

        // 共识与分歧
        ConsensusInfo = "技术面和基本面分析师对该股票整体看多，认为短期和中期都有上涨空间";
        DisagreementInfo = "对于上涨幅度存在分歧，技术分析师预期较为激进（+20%），基本面分析师相对保守（+10%）";

        NotifyFilteredCollectionsChanged();

        Logger?.LogInformation($"已加载模拟分析数据，股票代码：{StockSymbol}");
    }
}

