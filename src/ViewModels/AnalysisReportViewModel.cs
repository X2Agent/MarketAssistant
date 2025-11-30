using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Extensions;
using MarketAssistant.Services.Cache;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 分析报告视图模型
/// 负责展示 MarketAnalysisReport 中的 CoordinatorResult 结构化数据
/// </summary>
public partial class AnalysisReportViewModel : ViewModelBase
{
    private readonly IAnalysisCacheService _analysisCacheService;

    [ObservableProperty]
    private bool _isReportVisible;

    [ObservableProperty]
    private string _stockSymbol = string.Empty;

    [ObservableProperty]
    private string _coordinatorSummary = string.Empty;

    // === 聚合的结构化数据（来自所有分析师） ===

    [ObservableProperty]
    private float _overallScore;

    [ObservableProperty]
    private string _investmentRating = string.Empty;

    [ObservableProperty]
    private string _targetPrice = string.Empty;

    [ObservableProperty]
    private string _priceChangeExpectation = string.Empty;

    [ObservableProperty]
    private string _timeHorizon = string.Empty;

    [ObservableProperty]
    private string _riskLevel = string.Empty;

    [ObservableProperty]
    private float _confidencePercentage;

    [ObservableProperty]
    private string _scorePercentage = "0/10";

    // === 聚合的列表数据 ===

    public ObservableCollection<ScoreItem> DimensionScores { get; } = new();
    public ObservableCollection<string> InvestmentHighlights { get; } = new();
    public ObservableCollection<string> RiskFactors { get; } = new();
    public ObservableCollection<string> OperationSuggestions { get; } = new();

    // === Coordinator 专用（意见汇总） ===

    [ObservableProperty]
    private string _consensusAnalysis = string.Empty;

    [ObservableProperty]
    private string _disagreementAnalysis = string.Empty;

    [ObservableProperty]
    private bool _hasConsensusAnalysis;

    [ObservableProperty]
    private bool _hasDisagreementAnalysis;

    // === 各分析师的消息 ===

    public ObservableCollection<ChatMessage> AnalystMessages { get; } = new();

    public AnalysisReportViewModel(
         IAnalysisCacheService analysisCacheService,
        ILogger<AnalysisReportViewModel> logger)
        : base(logger)
    {
        _analysisCacheService = analysisCacheService;
    }

    partial void OnOverallScoreChanged(float value)
    {
        ScorePercentage = $"{value:F1}/10";
    }

    /// <summary>
    /// 使用完整的市场分析报告更新视图模型
    /// 直接使用 Coordinator 的综合判断（唯一的结构化数据来源）
    /// 专业分析师只提供自然语言分析，结构化数据全部由 Coordinator 提供
    /// </summary>
    public void UpdateWithReport(MarketAnalysisReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        ClearAllData();

        try
        {
            StockSymbol = report.StockSymbol;

            var coordinatorResult = report.CoordinatorResult;

            // 绑定 Coordinator 的结构化数据（无需 null 判断，CoordinatorResult 的属性都有默认值）
            OverallScore = coordinatorResult.OverallScore;
            InvestmentRating = coordinatorResult.InvestmentRating.GetDescription();
            TargetPrice = coordinatorResult.TargetPrice;
            PriceChangeExpectation = coordinatorResult.PriceChangeExpectation;
            TimeHorizon = coordinatorResult.TimeHorizon.GetDescription() +
                          (string.IsNullOrWhiteSpace(coordinatorResult.TimeHorizonDescription) ? "" : $" ({coordinatorResult.TimeHorizonDescription})");
            RiskLevel = coordinatorResult.RiskLevel.GetDescription();
            ConfidencePercentage = coordinatorResult.ConfidencePercentage;

            foreach (var (dimension, score) in coordinatorResult.DimensionScores)
            {
                DimensionScores.Add(new ScoreItem { Name = dimension, Score = score });
            }

            foreach (var highlight in coordinatorResult.InvestmentHighlights)
            {
                InvestmentHighlights.Add(highlight);
            }

            foreach (var risk in coordinatorResult.RiskFactors)
            {
                RiskFactors.Add(risk);
            }

            foreach (var suggestion in coordinatorResult.OperationSuggestions)
            {
                OperationSuggestions.Add(suggestion);
            }

            ConsensusAnalysis = coordinatorResult.ConsensusAnalysis;
            DisagreementAnalysis = coordinatorResult.DisagreementAnalysis;
            HasConsensusAnalysis = !string.IsNullOrWhiteSpace(ConsensusAnalysis);
            HasDisagreementAnalysis = !string.IsNullOrWhiteSpace(DisagreementAnalysis);

            CoordinatorSummary = coordinatorResult.Summary;

            // 添加各专业分析师的自然语言分析（无结构化数据）
            foreach (var message in report.AnalystMessages)
            {
                AnalystMessages.Add(message);
            }

            IsReportVisible = true;

            Logger?.LogInformation(
                "报告视图已更新：股票 {StockSymbol}，综合评分 {Score}，最终评级 {Rating}",
                StockSymbol, OverallScore, InvestmentRating);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "更新报告视图时发生错误");
            throw;
        }
    }

    private void ClearAllData()
    {
        DimensionScores.Clear();
        InvestmentHighlights.Clear();
        RiskFactors.Clear();
        OperationSuggestions.Clear();
        AnalystMessages.Clear();

        StockSymbol = string.Empty;
        CoordinatorSummary = string.Empty;
        OverallScore = 0f;
        ConfidencePercentage = 0f;
        InvestmentRating = string.Empty;
        TargetPrice = string.Empty;
        PriceChangeExpectation = string.Empty;
        TimeHorizon = string.Empty;
        RiskLevel = string.Empty;
        ConsensusAnalysis = string.Empty;
        DisagreementAnalysis = string.Empty;
        HasConsensusAnalysis = false;
        HasDisagreementAnalysis = false;
    }
}

/// <summary>
/// 评分项（用于维度评分展示）
/// </summary>
public class ScoreItem
{
    public string Name { get; set; } = string.Empty;
    public float Score { get; set; }

    /// <summary>
    /// 格式化的评分文本 (e.g. "8.5")
    /// </summary>
    public string FormattedScore => $"{Score:F1}";

    /// <summary>
    /// 评分百分比 (0.0 - 1.0)，用于进度条
    /// </summary>
    public double ScorePercentage => Score / 10.0;
}
