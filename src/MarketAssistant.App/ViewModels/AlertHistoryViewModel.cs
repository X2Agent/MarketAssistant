using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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

    /// <summary>是否正在加载。</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>空列表提示。</summary>
    [ObservableProperty]
    private string _emptyHintText = "暂无告警记录";

    public AlertHistoryViewModel(
        IAlertCenterService alertCenterService,
        ILogger? logger = null)
        : base(logger)
    {
        _alertCenterService = alertCenterService;
        _alertCenterService.AlertsChanged += OnAlertsChanged;
        _ = LoadAsync();
    }

    /// <summary>告警集合变化回调（新告警/合并/已读），切回 UI 线程刷新。</summary>
    private void OnAlertsChanged()
    {
        if (_disposed)
            return;

        Dispatcher.UIThread.Post(() => _ = LoadAsync());
    }

    /// <summary>手动刷新。</summary>
    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

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
    private async Task LoadAsync()
    {
        if (_disposed)
            return;

        IsLoading = true;
        try
        {
            var alerts = await _alertCenterService.GetRecentAlertsAsync(200);
            if (_disposed)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed)
                    return;

                Alerts.Clear();
                foreach (var alert in alerts)
                    Alerts.Add(alert);
                EmptyHintText = Alerts.Count == 0 ? "暂无告警记录" : string.Empty;
            });
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "加载告警历史失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _alertCenterService.AlertsChanged -= OnAlertsChanged;
    }
}
