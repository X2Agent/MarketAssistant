using MarketAssistant.Applications.Telegrams;

namespace MarketAssistant.Applications.News;

/// <summary>
/// 新闻更新服务接口
/// </summary>
public interface INewsUpdateService : IDisposable
{
    /// <summary>
    /// 新闻已更新事件
    /// </summary>
    event EventHandler<List<Telegram>>? NewsUpdated;

    /// <summary>
    /// 倒计时更新事件
    /// </summary>
    event EventHandler<string>? CountdownUpdated;

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 启动定时更新
    /// </summary>
    void StartUpdates();

    /// <summary>
    /// 停止定时更新
    /// </summary>
    void StopUpdates();
}

