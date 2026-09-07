using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications;
using MarketAssistant.Applications.News;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace MarketAssistant.ViewModels.Home;

public partial class TelegraphNewsViewModel : ViewModelBase, IDisposable
{
    private readonly MarketContext _marketContext;
    private INewsUpdateService _newsUpdateService;
    private bool _disposed;

    [ObservableProperty]
    private string _telegraphRefreshCountdown = "";

    public ObservableCollection<Telegram> Telegraphs { get; } = new();

    public IAsyncRelayCommand<Telegram> OpenNewsCommand { get; }

    public TelegraphNewsViewModel(
        MarketContext marketContext,
        ILogger<TelegraphNewsViewModel> logger)
        : base(logger)
    {
        _marketContext = marketContext;

        _newsUpdateService = _marketContext.GetService<INewsUpdateService>();

        OpenNewsCommand = new AsyncRelayCommand<Telegram>(OnOpenNewsAsync);

        _newsUpdateService.NewsUpdated += OnNewsUpdated;
        _newsUpdateService.CountdownUpdated += OnCountdownUpdated;

        SubscribeToMarketChanges(_marketContext);

        _newsUpdateService.StartUpdates();
    }

    /// <summary>
    /// 市场切换时更换新闻服务
    /// </summary>
    protected override void OnMarketChanged(MarketType newMarket)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // lambda 是排入 Dispatcher 队列后延迟执行的，期间本 VM 可能已被 Dispose；
            // 不检查 _disposed 会给已释放的 VM 重新订阅单例事件并重启轮询，造成泄漏
            if (_disposed)
                return;

            _newsUpdateService.StopUpdates();
            _newsUpdateService.NewsUpdated -= OnNewsUpdated;
            _newsUpdateService.CountdownUpdated -= OnCountdownUpdated;

            _newsUpdateService = _marketContext.GetService<INewsUpdateService>(newMarket);

            _newsUpdateService.NewsUpdated += OnNewsUpdated;
            _newsUpdateService.CountdownUpdated += OnCountdownUpdated;

            Telegraphs.Clear();
            TelegraphRefreshCountdown = "";

            _newsUpdateService.StartUpdates();

            Logger?.LogInformation("已切换到 {Market} 市场新闻源", newMarket);
        });
    }

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

    private void OnCountdownUpdated(object? sender, string countdown)
    {
        // Avalonia: 使用Dispatcher在UI线程执行
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            TelegraphRefreshCountdown = countdown;
        });
    }

    private async Task OnOpenNewsAsync(Telegram? telegram)
    {
        if (telegram == null || string.IsNullOrEmpty(telegram.Url))
            return;

        await SafeExecuteAsync(async () =>
        {
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
    /// 释放资源（幂等：市场切换回调与 Dispose 存在交错可能，退订可安全重复执行）
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }

        UnsubscribeFromMarketChanges(_marketContext);

        _newsUpdateService.NewsUpdated -= OnNewsUpdated;
        _newsUpdateService.CountdownUpdated -= OnCountdownUpdated;

        _newsUpdateService.StopUpdates();

        GC.SuppressFinalize(this);
    }
}
