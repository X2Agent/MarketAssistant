using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Agents;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Extensions;
using MarketAssistant.Models;
using MarketAssistant.Services.Cache;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 分析报告视图模型（彻底重构版，基于新的 AnalystResult 模型）
/// 不考虑向后兼容，直接使用结构化数据
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
    /// 处理分析消息（兼容旧接口，实际已不使用）
    /// </summary>
    public void ProcessAnalysisMessage(AnalysisMessage message)
    {
        Logger?.LogWarning("ProcessAnalysisMessage 已弃用，请使用 UpdateWithReport");
    }

    /// <summary>
    /// 异步处理分析消息（兼容旧接口，实际已不使用）
    /// </summary>
    public Task ProcessAnalysisMessageAsync(AnalysisMessage message)
    {
        Logger?.LogWarning("ProcessAnalysisMessageAsync 已弃用，请使用 UpdateWithReport");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 使用完整的市场分析报告更新视图模型（方案 A：极简化版）
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

    /// <summary>
    /// 使用单个分析消息更新视图模型（已完全废弃）
    /// </summary>
    [Obsolete("此方法已废弃，请使用 UpdateWithReport")]
    public void UpdateWithResult(ChatMessage message)
    {
        Logger?.LogWarning("UpdateWithResult 已废弃");
        throw new NotSupportedException("请使用 UpdateWithReport 方法");
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

    /// <summary>
    /// 加载示例数据（用于 UI 设计和测试）
    /// 方案 A：专业分析师只有自然语言，结构化数据来自 Coordinator
    /// </summary>
    public void LoadSampleData()
    {
        ClearAllData();

        StockSymbol = "600519.SH";
        CoordinatorSummary = "综合研判：建议买入";
        
        // 结构化数据（来自 Coordinator）
        OverallScore = 8.2f;
        InvestmentRating = "买入";
        TargetPrice = "180-200元";
        PriceChangeExpectation = "上涨 10-15%";
        TimeHorizon = "长期 1-2 年";
        RiskLevel = "中风险";
        ConfidencePercentage = 85f;

        // 维度评分
        DimensionScores.Add(new ScoreItem { Name = "基本面", Score = 8.5f });
        DimensionScores.Add(new ScoreItem { Name = "技术面", Score = 7.8f });
        DimensionScores.Add(new ScoreItem { Name = "市场情绪", Score = 8.3f });

        // 投资亮点
        InvestmentHighlights.Add("行业龙头地位稳固，品牌护城河深厚");
        InvestmentHighlights.Add("盈利能力强劲，ROE 持续保持高位");
        InvestmentHighlights.Add("技术面呈多头排列，上升趋势明确");

        // 风险因素
        RiskFactors.Add("估值处于历史高位，存在回调风险");
        RiskFactors.Add("行业竞争加剧，市场份额可能受到挑战");

        // 操作建议
        OperationSuggestions.Add("建议在合理估值区间分批建仓");
        OperationSuggestions.Add("设置止损位，控制回撤风险");

        // 共识与分歧
        ConsensusAnalysis = "各分析师一致看好公司基本面和行业地位";
        DisagreementAnalysis = "技术分析师认为短期存在回调风险，基本面分析师认为长期配置价值显著";
        HasConsensusAnalysis = true;
        HasDisagreementAnalysis = true;

        // 专业分析师的自然语言分析（直接使用 ChatMessage）
        AnalystMessages.Add(new ChatMessage(ChatRole.Assistant, 
            "公司基本面稳健，行业地位突出，盈利能力强劲，品牌护城河深厚。建议长期持有。")
        {
            AuthorName = "基本面分析师"
        });

        AnalystMessages.Add(new ChatMessage(ChatRole.Assistant,
            "技术面呈上升趋势，多头排列明显，支撑位稳固。短期可能面临回调，建议回调后买入。")
        {
            AuthorName = "技术分析师"
        });

        IsReportVisible = true;

        Logger?.LogInformation("已加载示例数据（方案 A：专业分析师纯自然语言）");
    }
}

/// <summary>
/// 评分项（用于维度评分展示）
/// </summary>
public class ScoreItem
{
    public string Name { get; set; } = string.Empty;
    public float Score { get; set; }
}
