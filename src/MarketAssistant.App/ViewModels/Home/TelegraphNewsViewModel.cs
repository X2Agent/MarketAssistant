using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.News;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 新闻快讯ViewModel
/// </summary>
public partial class TelegraphNewsViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MarketContext _marketContext;
    private INewsUpdateService _newsUpdateService;
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

    public TelegraphNewsViewModel(
        IServiceProvider serviceProvider,
        MarketContext marketContext,
        ILogger<TelegraphNewsViewModel> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
        _marketContext = marketContext;

        // 根据当前市场类型获取对应的 NewsUpdateService
        _newsUpdateService = _serviceProvider.GetRequiredKeyedService<INewsUpdateService>(_marketContext.CurrentMarket);

        OpenNewsCommand = new AsyncRelayCommand<Telegram>(OnOpenNewsAsync);

        // 订阅新闻更新服务事件
        _newsUpdateService.NewsUpdated += OnNewsUpdated;
        _newsUpdateService.CountdownUpdated += OnCountdownUpdated;

        // 订阅市场切换事件
        SubscribeToMarketChanges(_marketContext);

        // 自动启动新闻更新
        _newsUpdateService.StartUpdates();
    }

    /// <summary>
    /// 市场切换时更换新闻服务
    /// </summary>
    protected override void OnMarketChanged(MarketType newMarket)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 停止旧服务并取消事件订阅
            _newsUpdateService.StopUpdates();
            _newsUpdateService.NewsUpdated -= OnNewsUpdated;
            _newsUpdateService.CountdownUpdated -= OnCountdownUpdated;

            // 获取新市场的服务
            _newsUpdateService = _serviceProvider.GetRequiredKeyedService<INewsUpdateService>(newMarket);

            // 订阅新服务事件
            _newsUpdateService.NewsUpdated += OnNewsUpdated;
            _newsUpdateService.CountdownUpdated += OnCountdownUpdated;

            // 清空旧新闻
            Telegraphs.Clear();
            TelegraphRefreshCountdown = "";

            // 始终启动新闻更新
            _newsUpdateService.StartUpdates();

            Logger?.LogInformation("已切换到 {Market} 市场新闻源", newMarket);
        });
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
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // 取消市场切换事件订阅
            UnsubscribeFromMarketChanges(_marketContext);

            // 取消新闻服务事件订阅
            _newsUpdateService.NewsUpdated -= OnNewsUpdated;
            _newsUpdateService.CountdownUpdated -= OnCountdownUpdated;

            // 停止新闻更新服务
            _newsUpdateService.StopUpdates();

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
