namespace MarketAssistant.Services.Market;

/// <summary>
/// 无实时推送市场的空实现（A 股 SupportsRealtime=false）：订阅为空操作，事件永不触发。
/// 未来 A 股接入实时行情（如 Level-1 推送）时替换为实现类即可，消费方无需改动。
/// </summary>
public sealed class NoopRealtimeQuoteService : IRealtimeQuoteService
{
    public event Action<string, decimal, decimal>? PriceUpdated
    {
        add { }
        remove { }
    }

    public Task SubscribeAsync(string subscriberKey, IEnumerable<string> codes)
        => Task.CompletedTask;

    public Task UnsubscribeAllAsync(string subscriberKey)
        => Task.CompletedTask;
}
