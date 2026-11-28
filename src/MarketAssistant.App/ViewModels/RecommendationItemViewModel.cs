using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.InvestmentSelection.Models;
using System.Globalization;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 选股结果里的单只推荐：把 <see cref="InvestmentRecommendation"/> 转成界面展示文案，
/// 并持有这张卡片自己的展开与收藏状态。
/// </summary>
public partial class RecommendationItemViewModel : ObservableObject
{
    private const string EmptyValueText = "—";
    private const string PercentFormat = "+0.0;-0.0;0.0";
    private const string QuotePercentFormat = "+0.00;-0.00;0.00";

    /// <summary>3 行正文大约容纳的汉字数，理由超过它才提供「展开」入口。</summary>
    private const int CollapsedReasonLengthHint = 80;

    private readonly MarketType _marketType;

    public RecommendationItemViewModel(InvestmentRecommendation recommendation, int rank, MarketType marketType)
    {
        _marketType = marketType;

        RankText = $"#{rank}";
        Name = recommendation.Name;
        Symbol = recommendation.Symbol;
        ScoreText = recommendation.RecommendationScore.ToString("F0");
        RiskLevel = recommendation.RiskLevel;
        Reason = recommendation.Reason;
        CanExpandReason = recommendation.Reason.Length > CollapsedReasonLengthHint;

        CurrentPriceText = FormatPrice(recommendation.CurrentPrice);
        ChangeText = FormatSignedPercent(recommendation.ChangePercent, QuotePercentFormat);
        TargetPriceText = FormatPrice(recommendation.TargetPrice);
        StopLossText = FormatPrice(recommendation.StopLoss);
        ExpectedReturnText = FormatSignedPercent((decimal?)recommendation.ExpectedReturn, PercentFormat);
        HoldingText = BuildHoldingText(recommendation);

        FavoriteCode = FavoriteCodeNormalizer.Normalize(recommendation.Symbol, marketType);
        FavoriteMarket = marketType == MarketType.AShare
            ? StockSymbolConverter.SplitExchangePrefix(recommendation.Symbol).Market
            : string.Empty;
    }

    public string RankText { get; }

    public string Name { get; }

    public string Symbol { get; }

    public string ScoreText { get; }

    /// <summary>风险等级，界面经 EnumDescriptionConverter 显示为“低/中/高风险”。</summary>
    public RiskLevel RiskLevel { get; }

    public bool IsLowRisk => RiskLevel == RiskLevel.Low;

    public bool IsMediumRisk => RiskLevel == RiskLevel.Medium;

    public bool IsHighRisk => RiskLevel == RiskLevel.High;

    public string Reason { get; }

    public bool CanExpandReason { get; }

    public string CurrentPriceText { get; }

    /// <summary>筛选快照的涨跌幅（带正负号）；无数据时为破折号。</summary>
    public string ChangeText { get; }

    public string TargetPriceText { get; }

    public string StopLossText { get; }

    public string ExpectedReturnText { get; }

    /// <summary>“建议持有 N 天 · 建议仓位 N%”；两项都缺失时为空。</summary>
    public string HoldingText { get; }

    public bool HasHoldingText => HoldingText.Length > 0;

    /// <summary>收藏存储使用的归一化代码，与收藏页、详情页同一规则。</summary>
    public string FavoriteCode { get; }

    /// <summary>收藏存储使用的来源标记：A 股为交易所前缀，虚拟币为空。</summary>
    public string FavoriteMarket { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteToolTip))]
    private bool _isFavorite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReasonToggleText))]
    private bool _isReasonExpanded;

    public string FavoriteToolTip => IsFavorite ? "取消自选" : "加入自选";

    public string ReasonToggleText => IsReasonExpanded ? "收起" : "展开";

    [RelayCommand]
    private void ToggleReason() => IsReasonExpanded = !IsReasonExpanded;

    /// <summary>A 股按最小报价单位取位（股票 2 位、基金 / ETF 3 位）；虚拟币按量级取位，避免低价币显示成 0.00。</summary>
    private string FormatPrice(decimal? price) => price switch
    {
        null => EmptyValueText,
        decimal value => PriceFormatter.Format(value, _marketType, Symbol)
    };

    /// <summary>固定 InvariantCulture，保证与 PriceChangeColorConverter 的解析端一致，否则逗号小数区域下涨跌色会失效。</summary>
    private static string FormatSignedPercent(decimal? value, string format) =>
        value is { } number
            ? number.ToString(format, CultureInfo.InvariantCulture) + "%"
            : EmptyValueText;

    private static string BuildHoldingText(InvestmentRecommendation recommendation)
    {
        List<string> parts = [];
        if (recommendation.RecommendedHoldingPeriod is { } days)
        {
            parts.Add($"建议持有 {days} 天");
        }

        if (recommendation.RecommendedPosition is { } position)
        {
            parts.Add($"建议仓位 {position:0.#}%");
        }

        return string.Join(" · ", parts);
    }
}
