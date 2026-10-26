using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 告警历史 ViewModel：从统一告警中心加载最近告警事件，支持按级别/标的关键词筛选、
/// 手动刷新、全部标记已读，以及对带标的历史告警一键转 AI 归因研判。
/// 随告警页创建，页面销毁时解除订阅。
/// </summary>
public partial class AlertHistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IAlertCenterService _alertCenterService;
    private readonly MarketContext? _marketContext;
    private readonly List<AlertEvent> _allAlerts = [];
    private bool _disposed;

    public ObservableCollection<AlertEvent> Alerts { get; } = [];

    /// <summary>级别筛选项（Level 为 null 表示全部级别）。</summary>
    public ObservableCollection<AlertLevelFilterOption> LevelFilters { get; } =
    [
        new("全部级别", null),
        new("提示", AlertLevel.Info),
        new("警告", AlertLevel.Warning),
        new("紧急", AlertLevel.Critical)
    ];

    [ObservableProperty]
    private AlertLevelFilterOption? _selectedLevelFilter;

    /// <summary>市场筛选项（MarketType 为 null 表示全部市场）。</summary>
    public ObservableCollection<MarketFilterOption> MarketFilters { get; } =
    [
        new("全部市场", null),
        new("A股", MarketType.AShare),
        new("虚拟币", MarketType.Crypto)
    ];

    [ObservableProperty]
    private MarketFilterOption? _selectedMarketFilter;

    /// <summary>标的关键词（匹配标的代码或告警标题，忽略大小写）。</summary>
    [ObservableProperty]
    private string _symbolFilterText = string.Empty;

    /// <summary>当前筛选结果数量展示文本，如"共 12 条"。</summary>
    [ObservableProperty]
    private string _visibleCountText = string.Empty;

    public AlertHistoryViewModel(
        IAlertCenterService alertCenterService,
        MarketContext? marketContext = null,
        ILogger? logger = null)
        : base(logger)
    {
        _alertCenterService = alertCenterService;
        _marketContext = marketContext;
        SelectedLevelFilter = LevelFilters[0];
        SelectedMarketFilter = MarketFilters[0];
        EmptyMessage = "暂无告警记录";
        SetRetryCommand(() => LoadAlertsAsync());
        _alertCenterService.AlertsChanged += OnAlertsChanged;
        _ = LoadAlertsAsync();
    }

    /// <summary>告警集合变化回调（新告警/合并/已读），切回 UI 线程刷新。</summary>
    private void OnAlertsChanged()
    {
        if (_disposed)
            return;

        Dispatcher.UIThread.Post(() => _ = LoadAlertsAsync());
    }

    /// <summary>手动刷新。</summary>
    [RelayCommand]
    private async Task RefreshAsync() => await LoadAlertsAsync();

    /// <summary>全部标记已读（同时清除导航未读徽标）。</summary>
    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        try
        {
            await _alertCenterService.MarkAllReadAsync();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "标记告警全部已读失败");
        }
    }

    /// <summary>
    /// 一键转 AI 归因：跳转多分析师研判页分析该告警标的。
    /// 告警归属市场与当前市场不一致时先切换市场，保证分析链路解析到正确市场的数据服务。
    /// </summary>
    [RelayCommand]
    private void AskAiAttribution(AlertEvent? alert)
    {
        if (alert is null || !alert.HasSymbol)
            return;

        if (_marketContext != null && alert.MarketType != _marketContext.CurrentMarket)
        {
            Logger?.LogInformation(
                "告警 AI 归因切换市场: {From} → {To}", _marketContext.CurrentMarket, alert.MarketType);
            _marketContext.SwitchMarket(alert.MarketType);
        }

        WeakReferenceMessenger.Default.Send(
            new NavigationMessage("Analysis", new AssetNavigationParameter(alert.Symbol, string.Empty)));
        Logger?.LogInformation("告警转 AI 归因: {Symbol}, 告警标题: {Title}", alert.Symbol, alert.Title);
    }

    partial void OnSelectedLevelFilterChanged(AlertLevelFilterOption? value) => ApplyFilter();

    partial void OnSelectedMarketFilterChanged(MarketFilterOption? value) => ApplyFilter();

    partial void OnSymbolFilterTextChanged(string value) => ApplyFilter();

    /// <summary>加载最近告警（新→旧），保留全量副本供筛选。</summary>
    private async Task LoadAlertsAsync()
    {
        if (_disposed)
            return;

        await LoadAsync(async _ =>
        {
            var alerts = await _alertCenterService.GetRecentAlertsAsync(200);
            if (_disposed)
                return _allAlerts.Count > 0;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                _allAlerts.Clear();
                _allAlerts.AddRange(alerts);
                ApplyFilter();
            });

            return _allAlerts.Count > 0;
        }, "加载告警历史");
    }

    /// <summary>
    /// 按当前筛选条件重建可见集合并同步空态文案：
    /// 全量为空是"无告警记录"，筛选后为空是"无匹配告警"，两者提示不同。
    /// </summary>
    private void ApplyFilter()
    {
        var filtered = _allAlerts.Where(MatchesFilter).ToList();

        Alerts.Clear();
        foreach (var alert in filtered)
            Alerts.Add(alert);

        VisibleCountText = $"共 {filtered.Count} 条";

        if (_allAlerts.Count == 0)
        {
            EmptyMessage = "暂无告警记录";
        }
        else if (filtered.Count == 0)
        {
            SetEmpty("没有符合当前筛选条件的告警");
        }
        else if (State == LoadState.Empty)
        {
            SetReady();
        }
    }

    private bool MatchesFilter(AlertEvent alert)
        => AlertHistoryFilter.Matches(
            alert,
            SelectedLevelFilter?.Level,
            SymbolFilterText,
            SelectedMarketFilter?.MarketType);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _alertCenterService.AlertsChanged -= OnAlertsChanged;
        GC.SuppressFinalize(this);
    }
}

/// <summary>告警级别筛选项（Level 为 null 表示全部级别）。</summary>
public sealed record AlertLevelFilterOption(string Text, AlertLevel? Level);
