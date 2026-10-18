using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.PriceAlert;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 价格告警页 - 新建规则表单部分（条件 / 作用域 / K 线周期 / 高级选项与创建命令）。
/// 与主页视图模型拆分为 partial 类，避免单文件继续膨胀。
/// </summary>
public partial class PriceAlertPageViewModel
{
    /// <summary>
    /// 触发条件选项（价格 / 涨跌幅 / 量比 / 技术指标）
    /// </summary>
    public List<AlertCondition> ConditionOptions { get; } = [.. Enum.GetValues<AlertCondition>()];

    /// <summary>规则作用域选项（单标的 / 自选股批量）。</summary>
    public List<AlertScope> ScopeOptions { get; } = [.. Enum.GetValues<AlertScope>()];

    /// <summary>指标规则可选的 K 线周期（月线不参与告警评估）。</summary>
    public List<KLineType> KLinePeriodOptions { get; } =
        [KLineType.Minute5, KLineType.Minute15, KLineType.Daily, KLineType.Weekly];

    /// <summary>
    /// 新规则 - 资产搜索输入（下拉文本框）
    /// </summary>
    [ObservableProperty]
    private string _newRuleAssetText = string.Empty;

    /// <summary>
    /// 新规则 - 选中资产
    /// </summary>
    [ObservableProperty]
    private AssetOption? _newRuleSelectedAsset;

    /// <summary>
    /// 新规则 - 资产下拉是否展开
    /// </summary>
    [ObservableProperty]
    private bool _isAssetDropDownOpen;

    /// <summary>
    /// 新规则 - 触发条件
    /// </summary>
    [ObservableProperty]
    private AlertCondition _newRuleCondition = AlertCondition.PriceAbove;

    /// <summary>
    /// 新规则 - 作用域（单标的 / 自选股批量）
    /// </summary>
    [ObservableProperty]
    private AlertScope _newRuleScope = AlertScope.Single;

    /// <summary>
    /// 新规则 - 指标条件使用的 K 线周期（价格条件忽略）
    /// </summary>
    [ObservableProperty]
    private KLineType _newRuleKLineType = KLineType.Daily;

    /// <summary>
    /// 新规则 - 目标值（价格、涨跌幅百分比、量比倍数或 RSI 阈值）
    /// </summary>
    [ObservableProperty]
    private string _newRuleTargetValue = string.Empty;

    /// <summary>
    /// 新规则 - 是否一次性告警。触发一次后自动停用，可手动重新启用；默认持续告警（离开区间自动复位）。
    /// </summary>
    [ObservableProperty]
    private bool _newRuleIsOneTime;

    /// <summary>
    /// 是否展开高级选项（去抖 / 冷却 / 限次 / 交易联动）
    /// </summary>
    [ObservableProperty]
    private bool _isAdvancedOptionsExpanded;

    /// <summary>
    /// 新规则 - 去抖确认：条件需连续满足的行情 tick 数，0 表示立即触发。
    /// </summary>
    [ObservableProperty]
    private int _newRuleConfirmTicks;

    /// <summary>
    /// 新规则 - 去抖持续秒数：条件需持续满足的秒数，0 表示不限制。
    /// 与连续次数同时配置时取更严判定，用于消除 A 股轮询与币安推送的频率差异。
    /// </summary>
    [ObservableProperty]
    private int _newRuleConfirmSeconds;

    /// <summary>
    /// 新规则 - 冷却分钟数：两次触发之间的最小间隔，0 表示不冷却。
    /// </summary>
    [ObservableProperty]
    private int _newRuleCooldownMinutes;

    /// <summary>
    /// 新规则 - 最大触发次数，0 表示不限（与"仅提醒一次"互斥，后者恒为 1 次）。
    /// </summary>
    [ObservableProperty]
    private int _newRuleMaxTriggerCount;

    /// <summary>
    /// 新规则 - 是否升级为确认级告警：触发期间该标的的 AI 交易信号强制走人工确认。
    /// </summary>
    [ObservableProperty]
    private bool _newRuleRequireConfirmation;

    /// <summary>
    /// 新规则 - 市场类型（跟随当前市场）
    /// </summary>
    public MarketType NewRuleMarketType => _marketContext.CurrentMarket;

    /// <summary>
    /// 是否选中指标类条件（控制 K 线周期选择与目标值标签）。
    /// </summary>
    public bool IsIndicatorRuleSelected =>
        NewRuleCondition is not (AlertCondition.PriceAbove or AlertCondition.PriceBelow
            or AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow);

    /// <summary>是否自选股批量规则（选中后无需指定单一标的）。</summary>
    public bool IsWatchlistScopeSelected => NewRuleScope == AlertScope.Watchlist;

    /// <summary>
    /// 目标值输入框标签（涨跌幅显示百分比，指标条件按条件类型区分）
    /// </summary>
    public string TargetLabelText => NewRuleCondition switch
    {
        AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow => "目标涨跌幅（%）",
        AlertCondition.VolumeRatioAbove => "量比阈值（倍）",
        AlertCondition.RsiAbove or AlertCondition.RsiBelow => "RSI 阈值（0-100）",
        _ => "目标价格"
    };

    /// <summary>
    /// 目标值输入框占位文本；无数值目标的条件（交叉/突破）禁用输入。
    /// </summary>
    public string TargetPlaceholderText => NewRuleCondition switch
    {
        AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow => "如 5（%）",
        AlertCondition.VolumeRatioAbove => "如 3（3 倍量）",
        AlertCondition.RsiAbove => "如 70（超买）",
        AlertCondition.RsiBelow => "如 30（超卖）",
        _ => "如 100000"
    };

    /// <summary>当前条件是否携带数值目标（交叉与突破类条件没有目标值）。</summary>
    public bool HasTargetValue => NewRuleCondition is not (
        AlertCondition.MacdGoldenCross or AlertCondition.MacdDeadCross
        or AlertCondition.KdjGoldenCross or AlertCondition.KdjDeadCross
        or AlertCondition.MaGoldenCross or AlertCondition.MaDeadCross
        or AlertCondition.BollUpperBreak or AlertCondition.BollLowerBreak);

    partial void OnNewRuleConditionChanged(AlertCondition value)
    {
        OnPropertyChanged(nameof(TargetLabelText));
        OnPropertyChanged(nameof(TargetPlaceholderText));
        OnPropertyChanged(nameof(IsIndicatorRuleSelected));
        OnPropertyChanged(nameof(HasTargetValue));
    }

    partial void OnNewRuleScopeChanged(AlertScope value)
        => OnPropertyChanged(nameof(IsWatchlistScopeSelected));

    /// <summary>
    /// 添加新告警规则
    /// </summary>
    [RelayCommand]
    private async Task AddRuleAsync()
    {
        ValidationError = string.Empty;

        AssetOption? asset = null;
        if (!IsWatchlistScopeSelected)
        {
            // 校验资产：优先按当前输入文本精确匹配，未命中时取下拉选中项
            asset = ResolveAssetByText(NewRuleAssetText.Trim()) ?? NewRuleSelectedAsset;
            if (asset == null)
            {
                ValidationError = "请从下拉列表中选择资产";
                return;
            }
        }

        var targetValue = 0m;
        if (HasTargetValue
            && (!decimal.TryParse(NewRuleTargetValue.Trim(), out targetValue) || targetValue <= 0))
        {
            ValidationError = "请输入有效的目标值（正数）";
            return;
        }

        if (NewRuleCondition is AlertCondition.RsiAbove or AlertCondition.RsiBelow
            && targetValue is > 100 or < 0)
        {
            ValidationError = "RSI 阈值需在 0-100 之间";
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var rule = new PriceAlertRule
            {
                // 自选股批量规则的 AssetCode 仅作规则标识，实际符号在评估时按市场自选股展开
                AssetCode = asset?.Code ?? "自选股",
                AssetName = asset?.Name ?? "自选股",
                MarketType = NewRuleMarketType,
                Condition = NewRuleCondition,
                Scope = NewRuleScope,
                KLineType = NewRuleKLineType,
                TargetPrice = targetValue,
                IsOneTime = NewRuleIsOneTime,
                MaxTriggerCount = NewRuleIsOneTime ? 1 : NewRuleMaxTriggerCount,
                ConfirmTicks = NewRuleConfirmTicks,
                ConfirmSeconds = NewRuleConfirmSeconds,
                CooldownMinutes = NewRuleCooldownMinutes,
                TradingImpact = NewRuleRequireConfirmation
                    ? AlertTradingImpact.RequireConfirmation
                    : AlertTradingImpact.None,
                Enabled = true
            };

            await _alertService.AddRuleAsync(rule);

            NewRuleSelectedAsset = null;
            NewRuleAssetText = string.Empty;
            NewRuleTargetValue = string.Empty;
            NewRuleConfirmTicks = 0;
            NewRuleConfirmSeconds = 0;
            NewRuleCooldownMinutes = 0;
            NewRuleMaxTriggerCount = 0;
            NewRuleRequireConfirmation = false;
            NewRuleIsOneTime = false;

            Logger?.LogInformation(
                "添加价格告警: {Code} {Scope} {Condition} {KLineType} {Value}",
                rule.AssetCode, rule.Scope, rule.Condition, rule.KLineType, targetValue);
        }, "添加告警规则");
    }

    /// <summary>
    /// 按输入文本在搜索候选中精确匹配（代码或名称），避免用户未点击下拉项时丢失选择。
    /// 搜索为空表示无匹配，不兜底直接输入，防止无效交易对入库。
    /// </summary>
    private AssetOption? ResolveAssetByText(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        return AssetOptions.FirstOrDefault(a =>
            a.Code.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            a.Display.Equals(text, StringComparison.OrdinalIgnoreCase));
    }
}
