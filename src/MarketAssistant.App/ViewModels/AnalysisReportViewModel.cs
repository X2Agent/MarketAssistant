using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Infrastructure.AdaptiveCards;
using MarketAssistant.Infrastructure.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
// 本类有同名的 InvestmentRating 字符串属性，直接写枚举名会解析成属性
using Rating = MarketAssistant.Agents.MarketAnalysis.Models.InvestmentRating;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 分析报告视图模型
/// 负责展示 MarketAnalysisReport 中的 CoordinatorResult 结构化数据
/// </summary>
public partial class AnalysisReportViewModel(
    ILogger<AnalysisReportViewModel> logger,
    AdaptiveCardConverter adaptiveCardConverter) : ViewModelBase(logger)
{
    [ObservableProperty]
    private bool _isReportVisible;

    [ObservableProperty]
    private string _stockSymbol = string.Empty;

    /// <summary>报告生成时间（本地时区，精确到分钟），空值时不展示</summary>
    [ObservableProperty]
    private string _reportCreatedAtText = string.Empty;

    [ObservableProperty]
    private string _coordinatorSummary = string.Empty;

    // === 聚合的结构化数据（来自所有分析师） ===

    [ObservableProperty]
    private float _overallScore;

    [ObservableProperty]
    private string _investmentRating = string.Empty;

    [ObservableProperty]
    private bool _isRatingPositive;

    [ObservableProperty]
    private bool _isRatingNeutral;

    [ObservableProperty]
    private bool _isRatingNegative;

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

    /// <summary>置信度文本（如 "78%"），供 hero 区与目标价同排展示</summary>
    public string ConfidenceText => $"{ConfidencePercentage:F0}%";

    public bool HasConfidence => ConfidencePercentage > 0;
    public bool HasTargetPrice => !string.IsNullOrWhiteSpace(TargetPrice);
    public bool HasPriceChangeExpectation => !string.IsNullOrWhiteSpace(PriceChangeExpectation);
    public bool HasTimeHorizon => !string.IsNullOrWhiteSpace(TimeHorizon);
    public bool HasRiskLevel => !string.IsNullOrWhiteSpace(RiskLevel);

    /// <summary>报告头部是否展示标的信息行（标的代码或生成时间任一存在即可）</summary>
    public bool HasReportHeader
        => !string.IsNullOrWhiteSpace(StockSymbol) || !string.IsNullOrWhiteSpace(ReportCreatedAtText);

    // === 聚合的列表数据 ===

    public ObservableCollection<ScoreItem> DimensionScores { get; } = [];

    public ObservableCollection<string> InvestmentHighlights { get; } = [];
    public ObservableCollection<string> RiskFactors { get; } = [];
    public ObservableCollection<string> OperationSuggestions { get; } = [];

    // === Coordinator 专用（意见汇总） ===

    [ObservableProperty]
    private string _consensusAnalysis = string.Empty;

    [ObservableProperty]
    private string _disagreementAnalysis = string.Empty;

    [ObservableProperty]
    private bool _hasConsensusAnalysis;

    [ObservableProperty]
    private bool _hasDisagreementAnalysis;

    // === 质量评估指标 ===

    [ObservableProperty]
    private int _dataCompletenessPercent;

    [ObservableProperty]
    private int _analystConsensusPercent;

    [ObservableProperty]
    private string _limitationsNote = string.Empty;

    [ObservableProperty]
    private string _qualityLevelText = string.Empty;

    [ObservableProperty]
    private bool _hasQualityMetrics;

    // === 本次分析的 Token 用量 ===

    /// <summary>是否统计到 Token 用量</summary>
    [ObservableProperty]
    private bool _hasTokenUsage;

    /// <summary>用量摘要文本（如"本次共调用 9 次模型 / 128.4k tokens"）</summary>
    [ObservableProperty]
    private string _tokenUsageSummary = string.Empty;

    /// <summary>用量明细文本（输入/输出分别展示）</summary>
    [ObservableProperty]
    private string _tokenUsageDetail = string.Empty;

    /// <summary>用量中是否包含本地估算值</summary>
    [ObservableProperty]
    private bool _tokenUsageIsEstimated;

    public ObservableCollection<string> MissingDataDimensions { get; } = [];

    /// <summary>是否存在缺失数据维度（控制该区块显隐，避免 XAML 依赖 Count→bool 隐式转换）</summary>
    public bool HasMissingDataDimensions => MissingDataDimensions.Count > 0;

    // === 各分析师的消息 ===

    /// <summary>
    /// 分析师完整结论（消息适配器，含结构化卡片解析结果），报告页"分析师明细"区块的数据源。
    /// 与聊天侧栏共用 <see cref="ChatMessageAdapter"/>，保证同类内容两处渲染一致。
    /// </summary>
    public ObservableCollection<ChatMessageAdapter> AnalystMessages { get; } = [];

    /// <summary>是否存在可展示的分析师明细</summary>
    public bool HasAnalystMessages => AnalystMessages.Count > 0;

    partial void OnOverallScoreChanged(float value)
    {
        OnPropertyChanged(nameof(ScorePercentage));
    }

    /// <summary>评分文本（"8.5/10"），供 hero 区展示</summary>
    public string ScorePercentage => $"{OverallScore:F1}/10";

    partial void OnConfidencePercentageChanged(float value)
    {
        OnPropertyChanged(nameof(ConfidenceText));
        OnPropertyChanged(nameof(HasConfidence));
    }

    partial void OnStockSymbolChanged(string value) => OnPropertyChanged(nameof(HasReportHeader));

    partial void OnReportCreatedAtTextChanged(string value) => OnPropertyChanged(nameof(HasReportHeader));

    partial void OnTargetPriceChanged(string value) => OnPropertyChanged(nameof(HasTargetPrice));

    partial void OnPriceChangeExpectationChanged(string value) => OnPropertyChanged(nameof(HasPriceChangeExpectation));

    partial void OnTimeHorizonChanged(string value) => OnPropertyChanged(nameof(HasTimeHorizon));

    partial void OnRiskLevelChanged(string value) => OnPropertyChanged(nameof(HasRiskLevel));

    /// <summary>
    /// 使用完整的市场分析报告更新视图模型
    /// 直接使用 Coordinator 的综合判断（唯一的结构化数据来源）
    /// 专业分析师只提供自然语言分析，结构化数据全部由 Coordinator 提供
    /// </summary>
    public void UpdateWithReport(MarketAnalysisReport report)
    {
        ClearAllData();

        StockSymbol = report.AssetSymbol;
        ReportCreatedAtText = report.CreatedAt == default
            ? string.Empty
            : report.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        HasTokenUsage = report.Usage is { HasValue: true };
        TokenUsageSummary = TokenUsageFormatter.FormatSummary(report.Usage);
        TokenUsageDetail = TokenUsageFormatter.FormatDetail(report.Usage);
        TokenUsageIsEstimated = report.Usage?.IsEstimated == true;

        var coordinatorResult = report.CoordinatorResult;

        // 绑定 Coordinator 的结构化数据（无需 null 判断，CoordinatorResult 的属性都有默认值）
        OverallScore = coordinatorResult.OverallScore;
        InvestmentRating = coordinatorResult.InvestmentRating.GetDescription();
        var rating = coordinatorResult.InvestmentRating;
        IsRatingPositive = rating is Rating.StrongBuy or Rating.Buy;
        IsRatingNegative = rating is Rating.Reduce or Rating.Sell or Rating.StrongSell;
        IsRatingNeutral = !IsRatingPositive && !IsRatingNegative;
        TargetPrice = coordinatorResult.TargetPrice;
        PriceChangeExpectation = coordinatorResult.PriceChangeExpectation;
        TimeHorizon = coordinatorResult.TimeHorizon.GetDescription() +
                      (string.IsNullOrWhiteSpace(coordinatorResult.TimeHorizonDescription) ? "" : $" ({coordinatorResult.TimeHorizonDescription})");
        RiskLevel = coordinatorResult.RiskLevel.GetDescription();
        ConfidencePercentage = coordinatorResult.ConfidencePercentage;

        // 将强类型数据转换为列表供 UI 绑定
        DimensionScores.Clear();
        DimensionScores.Add(new ScoreItem { Name = "基本面", Score = coordinatorResult.DimensionScores.Fundamental });
        DimensionScores.Add(new ScoreItem { Name = "技术面", Score = coordinatorResult.DimensionScores.Technical });
        DimensionScores.Add(new ScoreItem { Name = "财务面", Score = coordinatorResult.DimensionScores.Financial });
        DimensionScores.Add(new ScoreItem { Name = "市场情绪", Score = coordinatorResult.DimensionScores.Sentiment });
        DimensionScores.Add(new ScoreItem { Name = "新闻事件", Score = coordinatorResult.DimensionScores.News });

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

        var qm = coordinatorResult.QualityMetrics;
        DataCompletenessPercent = qm.DataCompletenessPercent;
        AnalystConsensusPercent = qm.AnalystConsensusPercent;
        LimitationsNote = qm.LimitationsNote;
        QualityLevelText = qm.OverallQualityLevel switch
        {
            Agents.MarketAnalysis.Models.AnalysisQualityLevel.High => "高",
            Agents.MarketAnalysis.Models.AnalysisQualityLevel.Medium => "中",
            Agents.MarketAnalysis.Models.AnalysisQualityLevel.Low => "低",
            _ => "未知"
        };
        MissingDataDimensions.Clear();
        foreach (var dim in qm.MissingDataDimensions)
            MissingDataDimensions.Add(dim);
        HasQualityMetrics = qm.DataCompletenessPercent > 0 || qm.AnalystConsensusPercent > 0;
        OnPropertyChanged(nameof(HasMissingDataDimensions));

        // 添加各专业分析师的自然语言分析（结构化数据由卡片解析器从 JSON 产物中提取）。
        // 协调分析师的产物就是本页报告本身，再列一遍只会重复信息，故排除。
        foreach (var message in report.AnalystMessages)
        {
            if (message.Role == ChatRole.System)
                continue;

            if (string.Equals(message.AuthorName, "CoordinatorAnalyst", StringComparison.Ordinal)
                || string.Equals(message.AuthorName, "Coordinator", StringComparison.Ordinal))
                continue;

            AnalystMessages.Add(new ChatMessageAdapter(message, adaptiveCardConverter));
        }
        OnPropertyChanged(nameof(HasAnalystMessages));

        IsReportVisible = true;

        Logger?.LogInformation(
            "报告视图已更新：股票 {StockSymbol}，综合评分 {Score}，最终评级 {Rating}",
            StockSymbol, OverallScore, InvestmentRating);
    }

    private void ClearAllData()
    {
        DimensionScores.Clear();
        InvestmentHighlights.Clear();
        RiskFactors.Clear();
        OperationSuggestions.Clear();
        AnalystMessages.Clear();
        MissingDataDimensions.Clear();

        StockSymbol = string.Empty;
        ReportCreatedAtText = string.Empty;
        CoordinatorSummary = string.Empty;
        OverallScore = 0f;
        ConfidencePercentage = 0f;
        InvestmentRating = string.Empty;
        IsRatingPositive = false;
        IsRatingNeutral = false;
        IsRatingNegative = false;
        TargetPrice = string.Empty;
        PriceChangeExpectation = string.Empty;
        TimeHorizon = string.Empty;
        RiskLevel = string.Empty;
        ConsensusAnalysis = string.Empty;
        DisagreementAnalysis = string.Empty;
        HasConsensusAnalysis = false;
        HasDisagreementAnalysis = false;
        DataCompletenessPercent = 0;
        AnalystConsensusPercent = 0;
        LimitationsNote = string.Empty;
        QualityLevelText = string.Empty;
        HasQualityMetrics = false;
        HasTokenUsage = false;
        TokenUsageSummary = string.Empty;
        TokenUsageDetail = string.Empty;
        TokenUsageIsEstimated = false;
        OnPropertyChanged(nameof(HasAnalystMessages));
        OnPropertyChanged(nameof(HasMissingDataDimensions));
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
}
