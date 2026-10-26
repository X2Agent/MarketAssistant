using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Charts.Models;
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

    /// <summary>最大触发次数，0 表示不限（一次性规则恒为 1）。</summary>
    public int MaxTriggerCount { get; set; }

    /// <summary>去抖确认：需连续满足条件的行情 tick 数，0 表示立即触发。</summary>
    public int ConfirmTicks { get; set; }

    /// <summary>去抖确认：条件需持续满足的秒数，0 表示立即触发。</summary>
    public int ConfirmSeconds { get; set; }

    /// <summary>冷却分钟数：两次触发之间的最小间隔，0 表示不冷却。</summary>
    public int CooldownMinutes { get; set; }

    /// <summary>规则作用域：单标的或自选股批量（自选股场景下 AssetCode 仅作规则标识，不作为行情符号）。</summary>
    public AlertScope Scope { get; set; } = AlertScope.Single;

    /// <summary>指标类条件使用的 K 线周期（价格/涨跌幅条件忽略该字段）。</summary>
    public KLineType KLineType { get; set; } = KLineType.Daily;

    /// <summary>告警对交易的影响：确认级告警触发期间，该标的 AI 信号强制人工确认。</summary>
    public AlertTradingImpact TradingImpact { get; set; } = AlertTradingImpact.None;

    // ---- 运行态（不落库，由行情 tick 驱动）----
    private int _consecutiveMetCount;
    private DateTime? _conditionMetSinceUtc;
    private DateTime? _lastTriggeredAtUtc;
    private int _triggerCount;

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

    /// <summary>
    /// 高级选项摘要（去抖 / 冷却 / 限次 / 交易联动），全部为默认配置时返回空字符串。
    /// </summary>
    public string RuleOptionsText
    {
        get
        {
            var parts = new List<string>();
            if (ConfirmTicks > 0) parts.Add($"去抖 {ConfirmTicks} 次");
            if (ConfirmSeconds > 0) parts.Add($"持续 {ConfirmSeconds} 秒");
            if (CooldownMinutes > 0) parts.Add($"冷却 {CooldownMinutes} 分");
            if (!IsOneTime && MaxTriggerCount > 0) parts.Add($"限 {MaxTriggerCount} 次");
            if (TradingImpact == AlertTradingImpact.RequireConfirmation) parts.Add("确认级");
            if (Scope == AlertScope.Watchlist) parts.Add("自选股");
            if (IsIndicatorCondition) parts.Add(KLinePeriodText);
            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
        }
    }

    public string CurrentPriceText => CurrentPrice?.ToString("0.########") ?? "--";

    public string QuoteDisplayValue => CurrentChangePercent.HasValue
        ? PercentFormatter.Format(CurrentChangePercent.Value)
        : "--";

    public string FloatingValueText => FloatingValue switch
    {
        null => "--",
        var value when IsPercentCondition => $"{PercentFormatter.FormatValue(value.Value)} 个百分点",
        var value when FloatingPercent.HasValue =>
            $"{PercentFormatter.FormatCompact(value.Value)}（{PercentFormatter.Format(FloatingPercent.Value)}）",
        var value => PercentFormatter.FormatCompact(value.Value)
    };

    public string QuoteStatusText => LastUpdatedAt.HasValue
        ? $"更新于 {LastUpdatedAt.Value.ToLocalTime():HH:mm:ss}"
        : "等待行情";

    /// <summary>指标规则所用 K 线周期的展示文本。</summary>
    public string KLinePeriodText => KLineType switch
    {
        KLineType.Minute5 => "5分钟",
        KLineType.Minute15 => "15分钟",
        KLineType.Weekly => "周线",
        KLineType.Monthly => "月线",
        _ => "日线"
    };

    /// <summary>
    /// 是否指标类条件（量比 / 技术指标）。指标规则由周期评估器驱动，
    /// 不走价格 tick 路径；价格与涨跌幅条件则相反。
    /// </summary>
    public bool IsIndicatorCondition => Condition is not (
        AlertCondition.PriceAbove or AlertCondition.PriceBelow
        or AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow);

    /// <summary>
    /// 是否自选股批量规则在某符号下的运行时副本。副本不向 UI 回写单一符号的行情
    /// （一行代表多个符号），也避免跨符号的行情互相覆盖。
    /// </summary>
    public bool IsScopedClone { get; private init; }

    /// <summary>
    /// 为自选股批量规则生成绑定到指定符号的运行时副本：配置字段原样复制，
    /// 运行态（去抖计数、冷却时间、触发次数）全新。同一批量规则的每个符号
    /// 必须独立运行态，否则去抖与冷却会跨符号互相污染。
    /// </summary>
    public PriceAlertRule CloneForSymbol(string symbol, string? assetName = null) => new()
    {
        Id = Id,
        AssetCode = symbol,
        AssetName = string.IsNullOrWhiteSpace(assetName) ? symbol : assetName,
        MarketType = MarketType,
        Condition = Condition,
        TargetPrice = TargetPrice,
        IsOneTime = IsOneTime,
        MaxTriggerCount = MaxTriggerCount,
        ConfirmTicks = ConfirmTicks,
        ConfirmSeconds = ConfirmSeconds,
        CooldownMinutes = CooldownMinutes,
        TradingImpact = TradingImpact,
        // 副本已绑定单一符号，按单标的语义评估
        Scope = AlertScope.Single,
        KLineType = KLineType,
        CreatedAt = CreatedAt,
        Enabled = Enabled,
        IsScopedClone = true
    };

    /// <summary>
    /// 判断当前行情是否进入告警区间。跌幅目标按用户输入的正数处理，例如 3 表示小于等于 -3%。
    /// 指标类条件经 <paramref name="metric"/> 评估，metric 为 null（样本不足）时一律不满足。
    /// </summary>
    public bool IsConditionMet(decimal price, decimal? changePercent = null, AlertRuleMetric? metric = null)
    {
        return Condition switch
        {
            AlertCondition.PriceAbove => price >= TargetPrice,
            AlertCondition.PriceBelow => price <= TargetPrice,
            AlertCondition.ChangePercentAbove => changePercent.HasValue && changePercent.Value >= TargetPrice,
            AlertCondition.ChangePercentBelow => changePercent.HasValue && changePercent.Value <= -TargetPrice,
            _ => IsIndicatorConditionMet(metric)
        };
    }

    /// <summary>
    /// 指标类条件判定。交叉类（金叉/死叉）以"当前关系状态"为条件：
    /// 由不满足到满足的跃迁即交叉事件，去抖与冷却逻辑据此生效；
    /// 重启后若状态仍成立会重新评估，与成熟产品的交叉告警语义一致。
    /// </summary>
    private bool IsIndicatorConditionMet(AlertRuleMetric? metric)
    {
        if (metric is null)
            return false;

        return Condition switch
        {
            AlertCondition.VolumeRatioAbove => metric.VolumeRatio is { } ratio && ratio >= TargetPrice,
            AlertCondition.RsiAbove => metric.Rsi is { } rsiAbove && rsiAbove >= TargetPrice,
            AlertCondition.RsiBelow => metric.Rsi is { } rsiBelow && rsiBelow <= TargetPrice,
            AlertCondition.BollUpperBreak => metric.ClosePrice is { } closeUpper
                                             && metric.BollUpper is { } upper && closeUpper >= upper,
            AlertCondition.BollLowerBreak => metric.ClosePrice is { } closeLower
                                             && metric.BollLower is { } lower && closeLower <= lower,
            AlertCondition.MacdGoldenCross => IsFastAboveSlow(metric.MacdDiff, metric.MacdDea) == true,
            AlertCondition.MacdDeadCross => IsFastAboveSlow(metric.MacdDiff, metric.MacdDea) == false,
            AlertCondition.KdjGoldenCross => IsFastAboveSlow(metric.KdjK, metric.KdjD) == true,
            AlertCondition.KdjDeadCross => IsFastAboveSlow(metric.KdjK, metric.KdjD) == false,
            AlertCondition.MaGoldenCross => IsFastAboveSlow(metric.MaShort, metric.MaLong) == true,
            AlertCondition.MaDeadCross => IsFastAboveSlow(metric.MaShort, metric.MaLong) == false,
            _ => false
        };
    }

    /// <summary>快线是否位于慢线上方；任一样本不足返回 null（不评估）。</summary>
    private static bool? IsFastAboveSlow(decimal? fast, decimal? slow)
        => fast is null || slow is null ? null : fast.Value > slow.Value;

    /// <summary>
    /// 更新触发状态（含去抖与冷却判定）。返回 true 表示本次应发出告警。
    /// 去抖：条件需连续满足 ConfirmTicks 个行情 tick 且持续 ConfirmSeconds 秒；
    /// 冷却：两次触发之间至少间隔 CooldownMinutes 分钟；
    /// 一次性规则首次命中后保持触发状态；持续告警离开区间后自动复位。
    /// 指标类条件经 <paramref name="metric"/> 评估（由周期评估器按 K 线周期调用）。
    /// </summary>
    public bool UpdateTriggerState(decimal price, decimal? changePercent = null, AlertRuleMetric? metric = null)
    {
        var nowUtc = DateTime.UtcNow;
        var conditionMet = IsConditionMet(price, changePercent, metric);

        if (!conditionMet)
        {
            // 条件离开区间：复位去抖进度；持续告警自动复位，一次性保持触发
            _consecutiveMetCount = 0;
            _conditionMetSinceUtc = null;
            if (_alertActive)
            {
                // 确认级告警随条件离开而失效，标记服务层解除交易联动门
                _pendingGateClear = TradingImpact == AlertTradingImpact.RequireConfirmation;
                _alertActive = false;
            }
            Triggered = IsOneTime && Triggered;
            return false;
        }

        // 达到触发上限后不再告警，但仍需跟踪条件离开以释放联动门
        if (IsTriggerLimitReached || Triggered)
            return false;

        // 去抖：连续 tick 数与持续秒数需同时满足（配置为 0 的维度不限制）
        _consecutiveMetCount++;
        if (ConfirmTicks > 0 && _consecutiveMetCount < ConfirmTicks)
            return false;

        _conditionMetSinceUtc ??= nowUtc;
        if (ConfirmSeconds > 0 && (nowUtc - _conditionMetSinceUtc.Value).TotalSeconds < ConfirmSeconds)
            return false;

        // 冷却：两次触发之间的最小间隔
        if (CooldownMinutes > 0 && _lastTriggeredAtUtc.HasValue &&
            (nowUtc - _lastTriggeredAtUtc.Value).TotalMinutes < CooldownMinutes)
            return false;

        Triggered = true;
        _triggerCount++;
        _lastTriggeredAtUtc = nowUtc;
        _consecutiveMetCount = 0;
        _conditionMetSinceUtc = null;
        _pendingGateClear = false;
        return true;
    }

    /// <summary>是否达到触发次数上限（一次性规则恒为 1 次，MaxTriggerCount &gt; 0 时按配置限制）。</summary>
    public bool IsTriggerLimitReached =>
        IsOneTime ? _triggerCount >= 1 : MaxTriggerCount > 0 && _triggerCount >= MaxTriggerCount;

    /// <summary>告警已实际发出后调用：标记本次告警进入生效期（条件尚未离开区间）。</summary>
    public void NotifyTriggered() => _alertActive = true;

    /// <summary>
    /// 条件已离开区间且需要解除交易联动门；服务层解除后必须调用 <see cref="ResetTradingGate"/> 复位。
    /// </summary>
    public bool ShouldClearTradingGate => _pendingGateClear;

    /// <summary>解除交易联动门后调用：结束本次告警生效期。</summary>
    public void ResetTradingGate() => _pendingGateClear = false;

    /// <summary>最近一次触发的告警是否仍在条件区间内生效。</summary>
    private bool _alertActive;

    /// <summary>条件离开区间后待服务层处理的联动门解除标记。</summary>
    private bool _pendingGateClear;

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
    ChangePercentBelow,

    // ---- 以下为指标类条件（追加式扩展，持久化整数值不可调整）----

    /// <summary>量比超过目标值（最新 K 线成交量 / 前 N 根均量）。</summary>
    [Description("量比超")]
    VolumeRatioAbove,

    /// <summary>MACD 金叉状态（DIFF 位于 DEA 上方）。</summary>
    [Description("MACD金叉")]
    MacdGoldenCross,

    /// <summary>MACD 死叉状态（DIFF 位于 DEA 下方）。</summary>
    [Description("MACD死叉")]
    MacdDeadCross,

    /// <summary>RSI 超过目标值（超买）。</summary>
    [Description("RSI超买")]
    RsiAbove,

    /// <summary>RSI 低于目标值（超卖）。</summary>
    [Description("RSI超卖")]
    RsiBelow,

    /// <summary>KDJ 金叉状态（K 位于 D 上方）。</summary>
    [Description("KDJ金叉")]
    KdjGoldenCross,

    /// <summary>KDJ 死叉状态（K 位于 D 下方）。</summary>
    [Description("KDJ死叉")]
    KdjDeadCross,

    /// <summary>收盘价突破布林上轨。</summary>
    [Description("突破布林上轨")]
    BollUpperBreak,

    /// <summary>收盘价跌破布林下轨。</summary>
    [Description("跌破布林下轨")]
    BollLowerBreak,

    /// <summary>短期均线（MA5）上穿长期均线（MA10）。</summary>
    [Description("MA金叉")]
    MaGoldenCross,

    /// <summary>短期均线（MA5）下穿长期均线（MA10）。</summary>
    [Description("MA死叉")]
    MaDeadCross
}

/// <summary>
/// 告警规则作用域：Single=单标的；Watchlist=自选股批量（评估时按市场解析自选股列表）。
/// </summary>
public enum AlertScope
{
    [Description("单标的")]
    Single,

    [Description("自选股")]
    Watchlist
}

/// <summary>
/// 指标类告警条件的评估度量快照（由指标评估器按规则 K 线周期计算后传入）。
/// 字段为 null 表示样本不足，对应条件本次不评估（不得伪装为"条件未满足"）。
/// </summary>
public sealed record AlertRuleMetric(
    decimal? VolumeRatio,
    decimal? MacdDiff,
    decimal? MacdDea,
    decimal? Rsi,
    decimal? KdjK,
    decimal? KdjD,
    decimal? BollUpper,
    decimal? BollLower,
    decimal? ClosePrice,
    decimal? MaShort,
    decimal? MaLong);
