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
        {
            // 旧循环尚未退出（刚调用过 StopUpdates）：串行排队，待其退出后再启动，
            // 避免两个循环并存导致 NewsUpdated 短暂双发
            _logger?.LogInformation("旧更新循环停止中，待其退出后自动重启");
            _ = Task.Run(async () =>
            {
                try
                {
                    await _updateLoopTask;
                }
                catch
                {
                    // 循环内部已记录异常
                }
                StartUpdates();
            });
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        _updateLoopTask = UpdateLoopAsync(cts.Token);

        _logger?.LogInformation("新闻更新定时器已启动");
    }

    /// <summary>
    /// 停止定时更新
    /// </summary>
    public void StopUpdates()
    {
        // 只取消不 Dispose：循环可能正阻塞在带令牌的 Task.Delay 上，
        // 先 Dispose 会让后续注册抛 ObjectDisposedException（CTS 无关联计时器，跳过 Dispose 安全）
        _cts?.Cancel();
        _cts = null;
        // 保留 _updateLoopTask 直至旧循环退出，StartUpdates 依赖 IsCompleted 防止双循环
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
