using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
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
/// 价格告警页面 ViewModel，提供告警规则的增删改查和启停管理。
/// </summary>
public partial class PriceAlertPageViewModel : ViewModelBase, IDisposable
{
    private readonly PriceAlertService _alertService;
    private readonly IServiceProvider _serviceProvider;
    private readonly MarketContext _marketContext;
    private AlertCondition _newRuleCondition = AlertCondition.PriceAbove;

    /// <summary>
    /// 资产搜索建议防抖取消令牌，避免快速输入时频繁请求。
    /// </summary>
    private CancellationTokenSource? _suggestionCts;
    private const int SuggestionDebounceMs = 200;

    /// <summary>
    /// 当前市场对应的资产信息服务（Keyed Service，跟随市场切换）。
    /// </summary>
    private IAssetInfoService AssetInfoService =>
        _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(_marketContext.CurrentMarket);

    /// <summary>
    /// 当前页面的规则列表（含已触发和已禁用的规则）
    /// </summary>
    public ObservableCollection<PriceAlertRule> Rules { get; } = new();

    /// <summary>
    /// 交易对代码搜索建议列表
    /// </summary>
    public ObservableCollection<string> AssetSuggestions { get; } = new();

    /// <summary>
    /// 新规则 - 交易对代码
    /// </summary>
    [ObservableProperty]
    private string _newRuleAssetCode = string.Empty;

    /// <summary>
    /// 新规则 - 交易对名称
    /// </summary>
    public string NewRuleAssetName { get; set; } = string.Empty;

    /// <summary>
    /// 新规则 - 目标价格
    /// </summary>
    public string NewRuleTargetPrice { get; set; } = string.Empty;

    /// <summary>
    /// 新规则 - 触发条件（涨破/跌破）
    /// </summary>
    public AlertCondition NewRuleCondition
    {
        get => _newRuleCondition;
        set
        {
            if (_newRuleCondition != value)
            {
                _newRuleCondition = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPriceAbove));
                OnPropertyChanged(nameof(IsPriceBelow));
            }
        }
    }

    /// <summary>是否选了"涨破"（供 RadioButton 绑定）</summary>
    public bool IsPriceAbove
    {
        get => _newRuleCondition == AlertCondition.PriceAbove;
        set { if (value) NewRuleCondition = AlertCondition.PriceAbove; }
    }

    /// <summary>是否选了"跌破"（供 RadioButton 绑定）</summary>
    public bool IsPriceBelow
    {
        get => _newRuleCondition == AlertCondition.PriceBelow;
        set { if (value) NewRuleCondition = AlertCondition.PriceBelow; }
    }

    /// <summary>
    /// 新规则 - 市场类型（跟随当前市场）
    /// </summary>
    public MarketType NewRuleMarketType => _marketContext.CurrentMarket;

    /// <summary>
    /// 当前是否为虚拟币市场（控制 UI 显示）
    /// </summary>
    public bool IsCryptoMarket => _marketContext.CurrentMarket == MarketType.Crypto;

    /// <summary>
    /// 表单校验错误信息
    /// </summary>
    public string ValidationError { get; set; } = string.Empty;

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

    protected override void OnMarketChanged(MarketType newMarket)
    {
        OnPropertyChanged(nameof(IsCryptoMarket));
        OnPropertyChanged(nameof(NewRuleMarketType));
    }

    /// <summary>
    /// 交易对代码变化时触发防抖搜索建议
    /// </summary>
    partial void OnNewRuleAssetCodeChanged(string value)
    {
        _suggestionCts?.Cancel();
        _suggestionCts?.Dispose();
        _suggestionCts = new CancellationTokenSource();
        var cancellationToken = _suggestionCts.Token;

        if (string.IsNullOrWhiteSpace(value))
        {
            AssetSuggestions.Clear();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SuggestionDebounceMs, cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await UpdateSuggestionsAsync(value, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                Logger?.LogDebug("资产建议搜索防抖被取消，查询：{Keyword}", value);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "搜索资产建议时发生错误，查询：{Keyword}", value);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 调用资产信息服务搜索并更新建议列表
    /// </summary>
    private async Task UpdateSuggestionsAsync(string keyword, CancellationToken ct)
    {
        try
        {
            var results = await AssetInfoService.SearchAsync(keyword, ct);
            var codes = results.Select(r => r.Code).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AssetSuggestions.Clear();
                foreach (var code in codes)
                {
                    AssetSuggestions.Add(code);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // 防抖取消，正常情况
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "搜索资产建议时发生错误，查询：{Keyword}", keyword);
        }
    }

    /// <summary>
    /// 加载所有告警规则到 UI 列表
    /// </summary>
    private async Task LoadRulesAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            Rules.Clear();
            foreach (var rule in _alertService.Rules)
                Rules.Add(rule);
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

        // 校验交易对代码
        var code = NewRuleAssetCode.Trim();
        if (string.IsNullOrEmpty(code))
        {
            ValidationError = "请输入交易对代码（如 BTCUSDT）";
            OnPropertyChanged(nameof(ValidationError));
            return;
        }

        // 校验目标价格
        if (!decimal.TryParse(NewRuleTargetPrice.Trim(), out var targetPrice) || targetPrice <= 0)
        {
            ValidationError = "请输入有效的目标价格（正数）";
            OnPropertyChanged(nameof(ValidationError));
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var rule = new PriceAlertRule
            {
                AssetCode = code,
                AssetName = string.IsNullOrEmpty(NewRuleAssetName.Trim()) ? code : NewRuleAssetName.Trim(),
                MarketType = NewRuleMarketType,
                Condition = NewRuleCondition,
                TargetPrice = targetPrice,
                Enabled = true
            };

            await _alertService.AddRuleAsync(rule);

            // 清空表单
            NewRuleAssetCode = string.Empty;
            NewRuleAssetName = string.Empty;
            NewRuleTargetPrice = string.Empty;
            OnPropertyChanged(nameof(NewRuleAssetName));
            OnPropertyChanged(nameof(NewRuleTargetPrice));

            Logger?.LogInformation("添加价格告警: {Code} {Condition} {Price}", code, rule.Condition, targetPrice);
        }, "添加告警规则");
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
        _suggestionCts?.Cancel();
        _suggestionCts?.Dispose();
        _suggestionCts = null;
        _alertService.RulesChanged -= OnRulesChanged;
        UnsubscribeFromMarketChanges(_marketContext);
        GC.SuppressFinalize(this);
    }
}
