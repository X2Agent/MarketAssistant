using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using MarketAssistant.Avalonia.ViewModels;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 新闻快讯ViewModel
/// </summary>
public partial class TelegraphNewsViewModel : ViewModelBase, IDisposable
{
    private readonly INewsUpdateService _newsUpdateService;
    private bool _disposed;

    [ObservableProperty]
    private string _telegraphRefreshCountdown = "";

    /// <summary>
    /// 新闻快讯集合
    /// </summary>
    public ObservableCollection<Telegram> Telegraphs { get; } = new();

    /// <summary>
    /// 打开新闻命令
    /// </summary>
    public IAsyncRelayCommand<Telegram> OpenNewsCommand { get; }

    /// <summary>
    /// 刷新新闻命令
    /// </summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>
    /// 启动更新命令
    /// </summary>
    public IRelayCommand StartUpdatesCommand { get; }

    /// <summary>
    /// 停止更新命令
    /// </summary>
    public IRelayCommand StopUpdatesCommand { get; }

    public TelegraphNewsViewModel(INewsUpdateService newsUpdateService, ILogger<TelegraphNewsViewModel> logger) 
        : base(logger)
    {
        _newsUpdateService = newsUpdateService;
        
        OpenNewsCommand = new AsyncRelayCommand<Telegram>(OnOpenNewsAsync);
        RefreshCommand = new RelayCommand(() => _newsUpdateService.StartUpdates());
        StartUpdatesCommand = new RelayCommand(_newsUpdateService.StartUpdates);
        StopUpdatesCommand = new RelayCommand(_newsUpdateService.StopUpdates);

        // 订阅新闻更新服务事件
        _newsUpdateService.NewsUpdated += OnNewsUpdated;
        _newsUpdateService.CountdownUpdated += OnCountdownUpdated;

        // 自动启动新闻更新
        _newsUpdateService.StartUpdates();
    }

    /// <summary>
    /// 处理新闻更新事件
    /// </summary>
    private void OnNewsUpdated(object? sender, List<Telegram> news)
    {
        // Avalonia: 使用Dispatcher在UI线程执行
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Telegraphs.Clear();
            foreach (var item in news)
            {
                Telegraphs.Add(item);
            }
        });
    }

    /// <summary>
    /// 处理倒计时更新事件
    /// </summary>
    private void OnCountdownUpdated(object? sender, string countdown)
    {
        // Avalonia: 使用Dispatcher在UI线程执行
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            TelegraphRefreshCountdown = countdown;
        });
    }

    /// <summary>
    /// 打开新闻
    /// </summary>
    private async Task OnOpenNewsAsync(Telegram? telegram)
    {
        if (telegram == null || string.IsNullOrEmpty(telegram.Url)) 
            return;

        await SafeExecuteAsync(async () =>
        {
            // 使用系统默认浏览器打开URL
            var psi = new ProcessStartInfo
            {
                FileName = telegram.Url,
                UseShellExecute = true
            };
            Process.Start(psi);
            await Task.CompletedTask;
        }, "打开新闻");
    }

    /// <summary>
    /// 启动新闻更新
    /// </summary>
    public void StartUpdates()
    {
        _newsUpdateService.StartUpdates();
        Logger?.LogInformation("新闻更新服务已启动");
    }

    /// <summary>
    /// 停止新闻更新
    /// </summary>
    public void StopUpdates()
    {
        _newsUpdateService.StopUpdates();
        Logger?.LogInformation("新闻更新服务已停止");
    }

    /// <summary>
    /// 是否正在更新
    /// </summary>
    public bool IsUpdating => _newsUpdateService.IsRunning;

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // 取消事件订阅
            _newsUpdateService.NewsUpdated -= OnNewsUpdated;
            _newsUpdateService.CountdownUpdated -= OnCountdownUpdated;
            
            // 停止新闻更新服务
            _newsUpdateService.StopUpdates();
            
            _disposed = true;
        }
        
        GC.SuppressFinalize(this);
    }
}
