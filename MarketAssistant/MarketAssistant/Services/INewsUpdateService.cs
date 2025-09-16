using MarketAssistant.Applications.Telegrams;

namespace MarketAssistant.Services;

/// <summary>
/// 新闻更新服务接口
/// </summary>
public interface INewsUpdateService
{
    /// <summary>
    /// 新闻更新事件
    /// </summary>
    event EventHandler<List<Telegram>>? NewsUpdated;

    /// <summary>
    /// 倒计时更新事件
    /// </summary>
    event EventHandler<string>? CountdownUpdated;

    /// <summary>
    /// 启动定时更新
    /// </summary>
    void StartUpdates();

    /// <summary>
    /// 停止定时更新
    /// </summary>
    void StopUpdates();

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }
}
