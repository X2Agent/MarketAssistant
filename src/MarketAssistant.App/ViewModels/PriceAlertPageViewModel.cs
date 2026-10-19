using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.PriceAlert;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 资产下拉选项（名称 + 代码）
/// </summary>
public sealed class AssetOption
{
    public required string Name { get; init; }
    public required string Code { get; init; }

    /// <summary>下拉列表展示文本</summary>
    public string Display => $"{Name} ({Code})";

    public override string ToString() => Display;
}

/// <summary>
/// 价格告警页面 ViewModel，提供告警规则的增删改查和启停管理。
/// 规则列表与添加表单均跟随全局当前市场类型，不提供独立的市场筛选。
/// </summary>
public partial class PriceAlertPageViewModel : ViewModelBase, INavigationAware<AssetNavigationParameter>, IDisposable
{
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly PriceAlertService _alertService;
    private readonly MarketContext _marketContext;
    private readonly IDialogService _dialogService;
    private readonly IUserSettingService _userSettingService;

    /// <summary>
    /// 资产下拉列表加载取消令牌，避免快速切换市场时竞态覆盖。
    /// </summary>
    private CancellationTokenSource? _assetLoadCts;

    /// <summary>
    /// 当前市场对应的资产信息服务（Keyed Service，跟随市场切换）。
    /// </summary>
    private IAssetInfoService AssetInfoService =>
        _marketContext.GetService<IAssetInfoService>();

    /// <summary>
    /// 当前页面的规则列表（仅展示当前市场的规则）
    /// </summary>
    public ObservableCollection<PriceAlertRule> Rules { get; } = [];

    /// <summary>
    /// 交易对搜索候选列表（随输入防抖搜索）
    /// </summary>
    public ObservableCollection<AssetOption> AssetOptions { get; } = [];

    /// <summary>
    /// 当前是否为虚拟币市场（控制 UI 显示）
    /// </summary>
    public bool IsCryptoMarket => _marketContext.CurrentMarket == MarketType.Crypto;

    /// <summary>
    /// 资产选择字段标签（虚拟币市场为交易对，A股市场为股票名）
    /// </summary>
    public string AssetLabelText =>
        IsCryptoMarket ? "交易对" : "股票名";

    /// <summary>
    /// 资产选择字段占位文本（跟随市场）
    /// </summary>
    public string AssetPlaceholderText =>
        IsCryptoMarket
            ? "选择或搜索交易对，如 BTC"
            : "选择或搜索股票，如 贵州茅台";

    /// <summary>
    /// 表单校验错误信息
    /// </summary>
    [ObservableProperty]
    private string _validationError = string.Empty;

    /// <summary>
    /// 桌面通知是否已开启。关闭时告警仍会触发并保留状态，但不会显示弹窗。
    /// </summary>
    public bool IsNotificationEnabled => _userSettingService.CurrentSetting.Notification;

    /// <summary>页内标签页：0 = 规则管理，1 = 告警历史。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>当前是否选中历史标签页。</summary>
    public bool IsHistoryTabSelected => SelectedTabIndex == 1;

    /// <summary>告警历史子 ViewModel。</summary>
    public AlertHistoryViewModel AlertHistory { get; }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsHistoryTabSelected));

        // 进入历史标签页即视为已阅：清除未读徽标（MainWindowViewModel 经 AlertsChanged 刷新）
        if (value == 1)
            _ = AlertHistory.MarkAllReadCommand.ExecuteAsync(null);
    }

    public PriceAlertPageViewModel(
        PriceAlertService alertService,
        MarketContext marketContext,
        IDialogService dialogService,
        IUserSettingService userSettingService,
        IAlertCenterService alertCenterService,
        ILogger<PriceAlertPageViewModel> logger)
        : base(logger)
    {
        _alertService = alertService;
        _marketContext = marketContext;
        _dialogService = dialogService;
        _userSettingService = userSettingService;
        AlertHistory = new AlertHistoryViewModel(alertCenterService, marketContext, logger);

        EmptyMessage = "在上方添加价格告警规则：持续告警进入目标区间提醒一次、离开后自动复位；一次性告警触发后自动停用";
        SetRetryCommand(() => LoadRulesAsync());

        _alertService.RulesChanged += OnRulesChanged;
        _alertService.RuleQuoteUpdated += OnRuleQuoteUpdated;
        SubscribeToMarketChanges(_marketContext);
        _ = LoadRulesAsync();
    }

    private bool _disposed;

    /// <summary>
    /// 资产文本变化时触发防抖搜索
    /// </summary>
    partial void OnNewRuleAssetTextChanged(string value)
    {
        _ = SearchAssetsAsync(value.Trim());
    }

    protected override void OnMarketChanged(MarketType newMarket)
    {
        // 事件来自单例 MarketContext，Dispose 后不得再触发（更新状态/启动加载）
        if (_disposed)
            return;

        OnPropertyChanged(nameof(IsCryptoMarket));
        OnPropertyChanged(nameof(NewRuleMarketType));
        OnPropertyChanged(nameof(AssetLabelText));
        OnPropertyChanged(nameof(AssetPlaceholderText));
        NewRuleSelectedAsset = null;
        NewRuleAssetText = string.Empty;
        NewRuleCondition = AlertCondition.PriceAbove;
        NewRuleScope = AlertScope.Single;
        NewRuleKLineType = KLineType.Daily;
        _ = LoadRulesAsync();
    }

    /// <summary>
    /// 防抖搜索当前市场的资产（300ms 内连续输入只触发最后一次搜索，结果作为下拉候选）
    /// </summary>
    private async Task SearchAssetsAsync(string keyword)
    {
        var cancellationToken = ResetCancellationToken(ref _assetLoadCts);

        try
        {
            await Task.Delay(SearchDebounceDelay, cancellationToken);

            // 下拉选中项回填文本（如 "BTC (BTCUSDT)"）不触发重复搜索
            if (NewRuleSelectedAsset?.Display.Equals(keyword, StringComparison.OrdinalIgnoreCase) == true)
                return;

            // 空关键字清空候选，等待用户输入后再搜索
            if (keyword.Length == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AssetOptions.Clear();
                    IsAssetDropDownOpen = false;
                });
                return;
            }

            var assets = await AssetInfoService.SearchAsync(keyword, cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AssetOptions.Clear();
                foreach (var (name, code) in assets)
                {
                    AssetOptions.Add(new AssetOption { Name = name, Code = code });
                }
                IsAssetDropDownOpen = AssetOptions.Count > 0;
            });
        }
        catch (OperationCanceledException)
        {
            // 输入变化或市场切换导致搜索被取消，正常情况
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "搜索资产失败");
        }
    }

    /// <summary>
    /// 加载当前市场的告警规则到 UI 列表
    /// </summary>
    private async Task LoadRulesAsync()
    {
        await LoadAsync(async _ =>
        {
            Rules.Clear();
            foreach (var rule in _alertService.Rules)
            {
                if (rule.MarketType == _marketContext.CurrentMarket)
                    Rules.Add(CreateDisplayRule(rule));
            }

            OnPropertyChanged(nameof(IsNotificationEnabled));
            return Rules.Count > 0;
        }, "加载告警规则");
    }

    private static PriceAlertRule CreateDisplayRule(PriceAlertRule source)
    {
        var displayRule = new PriceAlertRule
        {
            Id = source.Id,
            AssetCode = source.AssetCode,
            AssetName = source.AssetName,
            MarketType = source.MarketType,
            Condition = source.Condition,
            TargetPrice = source.TargetPrice,
            IsOneTime = source.IsOneTime,
            MaxTriggerCount = source.MaxTriggerCount,
            ConfirmTicks = source.ConfirmTicks,
            ConfirmSeconds = source.ConfirmSeconds,
            CooldownMinutes = source.CooldownMinutes,
            TradingImpact = source.TradingImpact,
            Scope = source.Scope,
            KLineType = source.KLineType,
            Triggered = source.Triggered,
            Enabled = source.Enabled,
            CreatedAt = source.CreatedAt
        };

        if (source.CurrentPrice.HasValue)
        {
            displayRule.UpdateQuote(
                source.CurrentPrice.Value,
                source.CurrentChangePercent,
                source.LastUpdatedAt ?? DateTime.UtcNow);
        }

        return displayRule;
    }

    /// <summary>
    /// 规则变化回调（由 PriceAlertService 触发）
    /// </summary>
    private void OnRulesChanged()
    {
        Dispatcher.UIThread.Post(() => _ = LoadRulesAsync());
    }

    /// <summary>
    /// 行情更新时规则对象自身已发出属性通知；这里只负责把跨线程通知切回 UI 线程。
    /// </summary>
    private void OnRuleQuoteUpdated(PriceAlertRule rule)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var visibleRule = Rules.FirstOrDefault(item => item.Id == rule.Id);
            if (visibleRule == null)
                return;

            if (rule.CurrentPrice.HasValue)
            {
                visibleRule.UpdateQuote(
                    rule.CurrentPrice.Value,
                    rule.CurrentChangePercent,
                    rule.LastUpdatedAt ?? DateTime.UtcNow);
            }

            visibleRule.Enabled = rule.Enabled;
            visibleRule.Triggered = rule.Triggered;
        });
    }

    /// <summary>
    /// 删除告警规则（带确认提示）
    /// </summary>
    [RelayCommand]
    private async Task RemoveRuleAsync(string? ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return;

        var rule = _alertService.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "删除告警",
            $"确定要删除 {rule.AssetName}({rule.AssetCode}) 的告警规则吗？",
            "删除",
            "取消");
        if (!confirmed) return;

        await SafeExecuteAsync(async () =>
        {
            await _alertService.RemoveRuleAsync(ruleId);
            Logger?.LogInformation("删除价格告警: {RuleId}", ruleId);
        }, "删除告警规则");
    }

    /// <summary>
    /// 启用/禁用告警规则
    /// </summary>
    [RelayCommand]
    private async Task ToggleRuleAsync(string? ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return;

        await SafeExecuteAsync(async () =>
        {
            await _alertService.ToggleRuleAsync(ruleId);
            Logger?.LogInformation("切换告警规则状态: {RuleId}", ruleId);
        }, "切换告警规则");
    }

    /// <summary>
    /// 刷新规则列表
    /// </summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadRulesAsync();

    /// <summary>
    /// 从资产详情页深链进入时，预填新规则的资产，避免用户重新搜索。
    /// GoBack 重新激活不重复预填，否则会覆盖用户已编辑的表单。
    /// </summary>
    public void OnNavigatedTo(AssetNavigationParameter parameter, bool isReactivation)
    {
        // 作为根导航项打开时不会携带参数，此处需容忍 null
        if (isReactivation || parameter is null || string.IsNullOrEmpty(parameter.Code))
            return;

        var option = new AssetOption
        {
            Name = string.IsNullOrEmpty(parameter.Name) ? parameter.Code : parameter.Name,
            Code = parameter.Code
        };

        // 必须先落选中项再回填文本：OnNewRuleAssetTextChanged 的防抖搜索
        // 命中「选中项文本与关键字一致」时直接返回，不会清空下面的候选
        NewRuleSelectedAsset = option;
        AssetOptions.Clear();
        AssetOptions.Add(option);
        NewRuleAssetText = option.Display;
    }

    public void OnNavigatedFrom()
    {
        _assetLoadCts?.Cancel();
    }

    public void Dispose()
    {
        _disposed = true;
        _assetLoadCts?.Cancel();
        _assetLoadCts = null;
        _alertService.RulesChanged -= OnRulesChanged;
        _alertService.RuleQuoteUpdated -= OnRuleQuoteUpdated;
        AlertHistory.Dispose();
        UnsubscribeFromMarketChanges(_marketContext);
        GC.SuppressFinalize(this);
    }
}
