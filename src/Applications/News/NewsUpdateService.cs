using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using System.Timers;

namespace MarketAssistant.Applications.News;

/// <summary>
/// 新闻更新服务实现
/// </summary>
public class NewsUpdateService : INewsUpdateService
{
    private readonly TelegramService _telegramService;
    private readonly ILogger<NewsUpdateService> _logger;
    private System.Timers.Timer? _updateTimer;
    private bool _disposed;

    public event EventHandler<List<Telegram>>? NewsUpdated;
    public event EventHandler<string>? CountdownUpdated;

    public bool IsRunning => _updateTimer?.Enabled ?? false;

    public NewsUpdateService(TelegramService telegramService, ILogger<NewsUpdateService> logger)
    {
        _telegramService = telegramService;
        _logger = logger;
    }

    /// <summary>
    /// 启动定时更新
    /// </summary>
    public void StartUpdates()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NewsUpdateService));

        if (_updateTimer != null && _updateTimer.Enabled)
            return;

        // 设置统一的定时器，每秒触发一次
        _updateTimer = new System.Timers.Timer(1000); // 1秒
        _updateTimer.Elapsed += OnTimerElapsed;
        _updateTimer.AutoReset = true;
        _updateTimer.Start();

        _logger?.LogInformation("新闻更新定时器已启动");

        // 立即更新一次新闻
        _ = UpdateNewsItemsAsync();
    }

    /// <summary>
    /// 停止定时更新
    /// </summary>
    public void StopUpdates()
    {
        if (_updateTimer != null)
        {
            _updateTimer.Stop();
            _updateTimer.Elapsed -= OnTimerElapsed;
            _updateTimer.Dispose();
            _updateTimer = null;
            _logger?.LogInformation("新闻更新定时器已停止");
        }
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await GlobalExceptionHandler.SafeExecuteAsync(async () =>
        {
            // 更新倒计时
            UpdateCountdown();

            // 每10秒更新一次新闻
            if (DateTime.Now.Second % 10 == 0)
            {
                await UpdateNewsItemsAsync();
            }
        }, operationName: "定时器更新", logger: _logger);
    }

    private void UpdateCountdown()
    {
        try
        {
            var seconds = DateTime.Now.Second % 10;
            var nextUpdate = (seconds == 0) ? 10 : (10 - seconds);
            var countdownText = $"{nextUpdate}秒后更新";
            
            CountdownUpdated?.Invoke(this, countdownText);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "更新倒计时出错");
            CountdownUpdated?.Invoke(this, "更新中...");
        }
    }

    private async Task UpdateNewsItemsAsync()
    {
        try
        {
            // 通知正在更新
            CountdownUpdated?.Invoke(this, "正在更新...");

            var news = await _telegramService.GetTelegraphsAsync(CancellationToken.None);
            
            // 通知新闻已更新
            NewsUpdated?.Invoke(this, news);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取咨询时出错");
            CountdownUpdated?.Invoke(this, "更新失败");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopUpdates();
            _disposed = true;
        }
    }
}

