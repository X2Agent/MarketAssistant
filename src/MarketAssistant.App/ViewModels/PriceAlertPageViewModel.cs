using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.PriceAlert;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 交易对下拉选项
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
public partial class PriceAlertPageViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly PriceAlertService _alertService;
    private readonly IServiceProvider _serviceProvider;
    private readonly MarketContext _marketContext;

    /// <summary>
    /// 资产下拉列表加载取消令牌，避免快速切换市场时竞态覆盖。
    /// </summary>
    private CancellationTokenSource? _assetLoadCts;

    /// <summary>
    /// 当前市场对应的资产信息服务（Keyed Service，跟随市场切换）。
    /// </summary>
    private IAssetInfoService AssetInfoService =>
        _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(_marketContext.CurrentMarket);

    /// <summary>
    /// 当前页面的规则列表（仅展示当前市场的规则）
    /// </summary>
    public ObservableCollection<PriceAlertRule> Rules { get; } = new();

    /// <summary>
    /// 交易对搜索候选列表（随输入防抖搜索）
    /// </summary>
    public ObservableCollection<AssetOption> AssetOptions { get; } = new();

    /// <summary>
    /// 触发条件选项（涨破/跌破/涨幅超/跌幅超）
    /// </summary>
    public List<AlertCondition> ConditionOptions { get; } = Enum.GetValues<AlertCondition>().ToList();

    /// <summary>
    /// 新规则 - 交易对搜索输入（AutoCompleteBox 文本）
    /// </summary>
    [ObservableProperty]
    private string _newRuleAssetText = string.Empty;

    /// <summary>
    /// 新规则 - 选中交易对
    /// </summary>
    [ObservableProperty]
    private AssetOption? _newRuleSelectedAsset;

    /// <summary>
    /// 新规则 - 触发条件
    /// </summary>
    [ObservableProperty]
    private AlertCondition _newRuleCondition = AlertCondition.PriceAbove;

    /// <summary>
    /// 新规则 - 目标值（价格或涨跌幅百分比）
    /// </summary>
    [ObservableProperty]
    private string _newRuleTargetValue = string.Empty;

    /// <summary>
    /// 新规则 - 市场类型（跟随当前市场）
    /// </summary>
    public MarketType NewRuleMarketType => _marketContext.CurrentMarket;

    /// <summary>
    /// 目标值输入框标签（涨跌幅类型显示百分比）
    /// </summary>
    public string TargetLabelText =>
        NewRuleCondition is AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow
            ? "目标涨跌幅（%）"
            : "目标价格";

    /// <summary>
    /// 目标值输入框占位文本
    /// </summary>
    public string TargetPlaceholderText =>
        NewRuleCondition is AlertCondition.ChangePercentAbove or AlertCondition.ChangePercentBelow
            ? "如 5（%）"
            : "如 100000";

    /// <summary>
    /// 当前是否为虚拟币市场（控制 UI 显示）
    /// </summary>
    public bool IsCryptoMarket => _marketContext.CurrentMarket == MarketType.Crypto;

    /// <summary>
    /// 表单校验错误信息
    /// </summary>
    public string ValidationError { get; set; } = string.Empty;

    /// <summary>
    /// 空列表提示文本
    /// </summary>
    public string EmptyHintText => "在上方添加价格告警规则，系统将自动监听并在价格触发时通知你";

    public PriceAlertPageViewModel(
        PriceAlertService alertService,
        IServiceProvider serviceProvider,
        MarketContext marketContext,
        ILogger<PriceAlertPageViewModel> logger)
        : base(logger)
    {
        _alertService = alertService;
        _serviceProvider = serviceProvider;
        _marketContext = marketContext;
        _alertService.RulesChanged += OnRulesChanged;
        SubscribeToMarketChanges(_marketContext);
        _ = LoadRulesAsync();
    }

    partial void OnNewRuleConditionChanged(AlertCondition value)
    {
        OnPropertyChanged(nameof(TargetLabelText));
        OnPropertyChanged(nameof(TargetPlaceholderText));
    }

    /// <summary>
    /// 交易对文本变化时触发防抖搜索
    /// </summary>
    partial void OnNewRuleAssetTextChanged(string value)
    {
        _ = SearchAssetsAsync(value.Trim());
    }

    protected override void OnMarketChanged(MarketType newMarket)
    {
        OnPropertyChanged(nameof(IsCryptoMarket));
        OnPropertyChanged(nameof(NewRuleMarketType));
        NewRuleSelectedAsset = null;
        NewRuleAssetText = string.Empty;
        NewRuleCondition = AlertCondition.PriceAbove;
        _ = LoadRulesAsync();
    }

    /// <summary>
    /// 防抖搜索当前市场的资产（300ms 内连续输入只触发最后一次搜索，结果作为下拉候选）
    /// </summary>
    private async Task SearchAssetsAsync(string keyword)
    {
        _assetLoadCts?.Cancel();
        _assetLoadCts?.Dispose();
        _assetLoadCts = new CancellationTokenSource();
        var cancellationToken = _assetLoadCts.Token;

        try
        {
            await Task.Delay(SearchDebounceDelay, cancellationToken);

            if (keyword.Length == 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() => AssetOptions.Clear());
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
        await SafeExecuteAsync(async () =>
        {
            Rules.Clear();
            foreach (var rule in _alertService.Rules)
            {
                if (rule.MarketType == _marketContext.CurrentMarket)
                    Rules.Add(rule);
            }
            OnPropertyChanged(nameof(EmptyHintText));
        }, "加载告警规则");
    }

    /// <summary>
    /// 规则变化回调（由 PriceAlertService 触发）
    /// </summary>
    private void OnRulesChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() => _ = LoadRulesAsync());
    }

    /// <summary>
    /// 添加新告警规则
    /// </summary>
    [RelayCommand]
    private async Task AddRuleAsync()
    {
        ValidationError = string.Empty;

        // 校验交易对：优先按当前输入文本精确匹配，未命中时取下拉选中项
        var asset = ResolveAssetByText(NewRuleAssetText.Trim()) ?? NewRuleSelectedAsset;
        if (asset == null)
        {
            ValidationError = "请从下拉列表中选择或搜索交易对";
            OnPropertyChanged(nameof(ValidationError));
            return;
        }

        // 校验目标值
        if (!decimal.TryParse(NewRuleTargetValue.Trim(), out var targetValue) || targetValue <= 0)
        {
            ValidationError = "请输入有效的目标值（正数）";
            OnPropertyChanged(nameof(ValidationError));
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var rule = new PriceAlertRule
            {
                AssetCode = asset.Code,
                AssetName = asset.Name,
                MarketType = NewRuleMarketType,
                Condition = NewRuleCondition,
                TargetPrice = targetValue,
                Enabled = true
            };

            await _alertService.AddRuleAsync(rule);

            // 清空表单
            NewRuleSelectedAsset = null;
            NewRuleAssetText = string.Empty;
            NewRuleTargetValue = string.Empty;

            Logger?.LogInformation("添加价格告警: {Code} {Condition} {Value}", asset.Code, rule.Condition, targetValue);
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

    /// <summary>
    /// 删除告警规则
    /// </summary>
    [RelayCommand]
    private async Task RemoveRuleAsync(string? ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return;

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

    public void Dispose()
    {
        _assetLoadCts?.Cancel();
        _assetLoadCts?.Dispose();
        _assetLoadCts = null;
        _alertService.RulesChanged -= OnRulesChanged;
        UnsubscribeFromMarketChanges(_marketContext);
        GC.SuppressFinalize(this);
    }
}
