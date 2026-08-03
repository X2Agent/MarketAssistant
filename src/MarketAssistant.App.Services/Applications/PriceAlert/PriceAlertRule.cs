using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.PriceAlert;

/// <summary>
/// 价格预警规则
/// </summary>
public partial class PriceAlertRule : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string AssetCode { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public MarketType MarketType { get; set; } = MarketType.Crypto;
    public AlertCondition Condition { get; set; }
    public decimal TargetPrice { get; set; }
    public bool IsOneTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _triggered;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _enabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentQuote))]
    [NotifyPropertyChangedFor(nameof(CurrentValue))]
    [NotifyPropertyChangedFor(nameof(FloatingValue))]
    [NotifyPropertyChangedFor(nameof(FloatingPercent))]
    [NotifyPropertyChangedFor(nameof(FloatingValueText))]
    [NotifyPropertyChangedFor(nameof(CurrentPriceText))]
    private decimal? _currentPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentValue))]
    [NotifyPropertyChangedFor(nameof(FloatingValue))]
    [NotifyPropertyChangedFor(nameof(FloatingPercent))]
    [NotifyPropertyChangedFor(nameof(FloatingValueText))]
    [NotifyPropertyChangedFor(nameof(QuoteDisplayValue))]
    private decimal? _currentChangePercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuoteStatusText))]
    private DateTime? _lastUpdatedAt;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 是否基于涨跌幅触发（目标值单位为百分比）
    /// </summary>
    public bool IsPercentCondition =>
        Condition is AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow;

    /// <summary>
    /// 当前告警条件实际使用的行情值：价格条件使用现价，涨跌幅条件使用当前涨跌幅。
    /// </summary>
    public decimal? CurrentValue => IsPercentCondition ? CurrentChangePercent : CurrentPrice;

    /// <summary>
    /// 实际比较阈值。跌幅目标以正数录入，但行情涨跌幅使用负数表示下跌。
    /// </summary>
    public decimal ThresholdValue => Condition == AlertCondition.ChangePercentBelow ? -TargetPrice : TargetPrice;

    /// <summary>
    /// 当前值相对实际阈值的差额；正数表示高于阈值，负数表示低于阈值。
    /// </summary>
    public decimal? FloatingValue => CurrentValue.HasValue ? CurrentValue.Value - ThresholdValue : null;

    /// <summary>
    /// 当前价格相对价格目标的百分比差；涨跌幅条件本身已是百分比，不重复换算。
    /// </summary>
    public decimal? FloatingPercent => !IsPercentCondition && TargetPrice != 0 && CurrentPrice.HasValue
        ? (CurrentPrice.Value - TargetPrice) / TargetPrice * 100
        : null;

    public bool HasCurrentQuote => CurrentPrice.HasValue;

    public string StatusText => IsOneTime && Triggered ? "重新启用" : Enabled ? "禁用" : "启用";

    public string AlertModeText => IsOneTime ? "一次性" : "持续";

    public string CurrentPriceText => CurrentPrice?.ToString("0.########") ?? "--";

    public string QuoteDisplayValue => CurrentChangePercent.HasValue
        ? $"{CurrentChangePercent.Value:+0.00;-0.00;0.00}%"
        : "--";

    public string FloatingValueText => FloatingValue switch
    {
        null => "--",
        var value when IsPercentCondition => $"{value:+0.00;-0.00;0.00} 个百分点",
        var value when FloatingPercent.HasValue =>
            $"{value:+0.########;-0.########;0}（{FloatingPercent.Value:+0.00;-0.00;0.00}%）",
        var value => $"{value:+0.########;-0.########;0}"
    };

    public string QuoteStatusText => LastUpdatedAt.HasValue
        ? $"更新于 {LastUpdatedAt.Value.ToLocalTime():HH:mm:ss}"
        : "等待行情";

    /// <summary>
    /// 判断当前行情是否进入告警区间。跌幅目标按用户输入的正数处理，例如 3 表示小于等于 -3%。
    /// </summary>
    public bool IsConditionMet(decimal price, decimal? changePercent = null)
    {
        return Condition switch
        {
            AlertCondition.PriceAbove => price >= TargetPrice,
            AlertCondition.PriceBelow => price <= TargetPrice,
            AlertCondition.ChangePercentAbove => changePercent.HasValue && changePercent.Value >= TargetPrice,
            AlertCondition.ChangePercentBelow => changePercent.HasValue && changePercent.Value <= -TargetPrice,
            _ => false
        };
    }

    /// <summary>
    /// 更新触发状态。一次性告警首次命中后保持触发状态；持续告警离开区间后自动复位。
    /// </summary>
    public bool UpdateTriggerState(decimal price, decimal? changePercent = null)
    {
        if (IsOneTime && Triggered)
            return false;

        var conditionMet = IsConditionMet(price, changePercent);
        var shouldNotify = conditionMet && !Triggered;
        Triggered = IsOneTime ? Triggered || conditionMet : conditionMet;
        return shouldNotify;
    }

    /// <summary>
    /// 更新运行态行情。运行态数据不写入数据库，由行情源持续刷新。
    /// </summary>
    public void UpdateQuote(decimal price, decimal? changePercent, DateTime updatedAt)
    {
        CurrentPrice = price;
        CurrentChangePercent = changePercent;
        LastUpdatedAt = updatedAt;
    }
}

public enum AlertCondition
{
    [Description("涨破")]
    PriceAbove,

    [Description("跌破")]
    PriceBelow,

    [Description("涨幅超")]
    ChangePercentAbove,

    [Description("跌幅超")]
    ChangePercentBelow
}
