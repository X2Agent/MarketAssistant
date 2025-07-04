using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Views.Models;
using MarketAssistant.Views.Parsers;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

public partial class AnalysisReportViewModel : ViewModelBase
{
    private readonly IAnalystDataParser _analystDataParser;

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

    // 统一的分析数据列表
    public ObservableCollection<AnalysisDataItem> AnalysisData { get; } = new();

    // 数据过滤属性
    public ObservableCollection<AnalysisDataItem> TechnicalIndicators =>
        new(AnalysisData.Where(x =>
            x.DataType.Contains("Technical", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("技术", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("Indicator", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("指标", StringComparison.OrdinalIgnoreCase)));

    public ObservableCollection<AnalysisDataItem> FundamentalIndicators =>
        new(AnalysisData.Where(x =>
            x.DataType.Contains("Fundamental", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("基本面", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("基础", StringComparison.OrdinalIgnoreCase)));

    public ObservableCollection<AnalysisDataItem> FinancialData =>
        new(AnalysisData.Where(x =>
            x.DataType.Contains("Financial", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("财务", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("Finance", StringComparison.OrdinalIgnoreCase)));

    public ObservableCollection<AnalysisDataItem> MarketSentimentData =>
        new(AnalysisData.Where(x =>
            x.DataType.Contains("Sentiment", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("情绪", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("Market", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("市场", StringComparison.OrdinalIgnoreCase)));

    public ObservableCollection<AnalysisDataItem> NewsEventData =>
        new(AnalysisData.Where(x =>
            x.DataType.Contains("News", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("Event", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("新闻", StringComparison.OrdinalIgnoreCase) ||
            x.DataType.Contains("事件", StringComparison.OrdinalIgnoreCase)));

    // 其他集合
    public ObservableCollection<string> InvestmentHighlights { get; } = new();
    public ObservableCollection<string> RiskFactors { get; } = new();
    public ObservableCollection<string> OperationSuggestions { get; } = new();
    public ObservableCollection<ScoreItem> DimensionScores { get; } = new();

    // 辅助属性
    public bool HasConsensusInfo => !string.IsNullOrEmpty(ConsensusInfo);
    public bool HasDisagreementInfo => !string.IsNullOrEmpty(DisagreementInfo);
    public bool HasConsensusOrDisagreement => HasConsensusInfo || HasDisagreementInfo;
    public string ScorePercentage => $"{OverallScore}/10";

    public AnalysisReportViewModel(IAnalystDataParser analystDataParser, ILogger<AnalysisReportViewModel> logger)
        : base(logger)
    {
        _analystDataParser = analystDataParser;
    }

    partial void OnConsensusInfoChanged(string value)
    {
        OnPropertyChanged(nameof(HasConsensusInfo));
        OnPropertyChanged(nameof(HasConsensusOrDisagreement));
    }

    partial void OnDisagreementInfoChanged(string value)
    {
        OnPropertyChanged(nameof(HasDisagreementInfo));
        OnPropertyChanged(nameof(HasConsensusOrDisagreement));
    }

    partial void OnOverallScoreChanged(float value)
    {
        OnPropertyChanged(nameof(ScorePercentage));
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
            }
        }, "解析分析师意见");
    }

    private void UpdateWithResult(AnalystResult result)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ClearAllData();

            // 更新基本信息
            StockSymbol = result.StockSymbol;
            TargetPrice = result.TargetPrice;
            PriceChange = result.PriceChange;
            Recommendation = !string.IsNullOrEmpty(result.InvestmentRating) ? result.InvestmentRating : result.Rating;
            RiskLevel = result.RiskLevel;
            ConfidenceLevel = result.ConfidencePercentage > 0 ? $"{result.ConfidencePercentage}%" : "--";
            OverallScore = result.OverallScore;

            // 设置报告可见性
            IsReportVisible = true;

            // 更新维度评分
            foreach (var score in result.DimensionScores)
                DimensionScores.Add(new ScoreItem { Name = score.Key, Score = score.Value });

            // 更新共识和分歧信息
            ConsensusInfo = result.ConsensusInfo;
            DisagreementInfo = result.DisagreementInfo;

            // 更新集合数据
            foreach (var highlight in result.InvestmentHighlights)
                InvestmentHighlights.Add(highlight);

            foreach (var risk in result.RiskFactors)
                RiskFactors.Add(risk);

            foreach (var suggestion in result.OperationSuggestions)
                OperationSuggestions.Add(suggestion);

            // 添加统一的分析数据
            foreach (var dataItem in result.AnalysisData)
                AnalysisData.Add(dataItem);

            // 通知UI更新分析数据的分类视图
            NotifyFilteredCollectionsChanged();

            Logger?.LogInformation($"分析报告更新完成，股票代码：{StockSymbol}，评级：{Recommendation}");
        });
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
    }

    private void NotifyFilteredCollectionsChanged()
    {
        OnPropertyChanged(nameof(TechnicalIndicators));
        OnPropertyChanged(nameof(FundamentalIndicators));
        OnPropertyChanged(nameof(FinancialData));
        OnPropertyChanged(nameof(MarketSentimentData));
        OnPropertyChanged(nameof(NewsEventData));
    }
}