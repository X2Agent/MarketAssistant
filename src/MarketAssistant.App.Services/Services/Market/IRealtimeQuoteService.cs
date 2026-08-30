namespace MarketAssistant.Services.Market;

/// <summary>
/// 实时行情订阅抽象：订阅/退订指定资产代码的实时价格推送。
/// 资产代码统一使用应用层格式（如 BTC、600519），市场专属的符号转换由各市场实现内部完成。
/// </summary>
public interface IRealtimeQuoteService
{
    /// <summary>
    /// 收到实时价格更新时触发，参数为 (资产代码, 最新价, 涨跌幅%)。
    /// 回调在后台线程执行，UI 更新须自行派发到 UI 线程。
    /// </summary>
    event Action<string, decimal, decimal>? PriceUpdated;

    /// <summary>
    /// 以指定订阅方身份订阅实时行情。同一订阅方重复调用会整体替换其资产集合，
    /// 调用方应传入该订阅方当前需要的完整集合。
    /// </summary>
    /// <param name="subscriberKey">订阅方标识（见 <see cref="RealtimeQuoteSubscriberKeys"/>）</param>
    /// <param name="codes">应用层资产代码列表</param>
    Task SubscribeAsync(string subscriberKey, IEnumerable<string> codes);

    /// <summary>
    /// 取消指定订阅方的全部订阅，不影响其他订阅方。
    /// </summary>
    Task UnsubscribeAllAsync(string subscriberKey);
}

/// <summary>
/// 预定义订阅方标识，保证各模块退订时使用与订阅时一致的 key。
/// </summary>
public static class RealtimeQuoteSubscriberKeys
{
    public const string PriceAlerts = "price-alerts";
    public const string MarketMonitor = "market-monitor";
    public const string Favorites = "favorites";
    public const string AssetDetail = "asset-detail";
}
