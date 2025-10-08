using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Agents;
using MarketAssistant.Services.Cache;
using MarketAssistant.Avalonia.Views.Models;
using MarketAssistant.Parsers;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.Avalonia.ViewModels;

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
}

