using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.AlertCenter;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 告警历史 ViewModel：从统一告警中心加载最近告警事件，支持手动刷新与全部标记已读。
/// 随告警页创建，页面销毁时解除订阅。
/// </summary>
public partial class AlertHistoryViewModel : ViewModelBase, IDisposable
{
    private readonly IAlertCenterService _alertCenterService;
    private bool _disposed;

    public ObservableCollection<AlertEvent> Alerts { get; } = new();

    public AlertHistoryViewModel(
        IAlertCenterService alertCenterService,
        ILogger? logger = null)
        : base(logger)
    {
        _alertCenterService = alertCenterService;
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

    /// <summary>加载最近告警（新→旧）。</summary>
    private async Task LoadAlertsAsync()
    {
        if (_disposed)
            return;

        await LoadAsync(async _ =>
        {
            var alerts = await _alertCenterService.GetRecentAlertsAsync(200);
            if (_disposed)
                return Alerts.Count > 0;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                Alerts.Clear();
                foreach (var alert in alerts)
                    Alerts.Add(alert);
            });

            return Alerts.Count > 0;
        }, "加载告警历史");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _alertCenterService.AlertsChanged -= OnAlertsChanged;
    }
}
