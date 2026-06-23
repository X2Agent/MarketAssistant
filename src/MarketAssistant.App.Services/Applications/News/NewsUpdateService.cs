using MarketAssistant.Applications.Telegrams;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.News;

/// <summary>
/// 新闻更新服务实现
/// </summary>
public class NewsUpdateService : INewsUpdateService
{
    private const int UpdateIntervalSeconds = 60;

    private readonly ITelegramService _telegramService;
    private readonly ILogger<NewsUpdateService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _updateLoopTask;
    private bool _disposed;

    public event EventHandler<List<Telegram>>? NewsUpdated;
    public event EventHandler<string>? CountdownUpdated;

    public bool IsRunning => _updateLoopTask != null && !_updateLoopTask.IsCompleted;

    public NewsUpdateService(ITelegramService telegramService, ILogger<NewsUpdateService> logger)
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

        if (_updateLoopTask != null && !_updateLoopTask.IsCompleted)
            return;

        _cts = new CancellationTokenSource();
        _updateLoopTask = UpdateLoopAsync(_cts.Token);

        _logger?.LogInformation("新闻更新定时器已启动");
    }

    /// <summary>
    /// 停止定时更新
    /// </summary>
    public void StopUpdates()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        _updateLoopTask = null;
        _logger?.LogInformation("新闻更新定时器已停止");
    }

    private async Task UpdateLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await UpdateNewsItemsAsync(ct);

                for (var remaining = UpdateIntervalSeconds; remaining > 0; remaining--)
                {
                    ct.ThrowIfCancellationRequested();
                    CountdownUpdated?.Invoke(this, $"{remaining}秒后更新");
                    await Task.Delay(1000, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "新闻更新循环异常退出");
        }
    }

    private async Task UpdateNewsItemsAsync(CancellationToken ct)
    {
        try
        {
            CountdownUpdated?.Invoke(this, "正在更新...");

            var news = await _telegramService.GetTelegraphsAsync(ct);

            NewsUpdated?.Invoke(this, news);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取快讯时出错");
            CountdownUpdated?.Invoke(this, "更新失败");
            await Task.Delay(3000, ct);
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
