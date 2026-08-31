using System.Collections.Concurrent;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 虚拟币市场的实时行情订阅实现：包装 <see cref="BinanceWebSocketService"/>，
/// 对外使用应用层资产代码（如 BTC），内部完成 code ↔ Binance 交易对的双向转换。
/// </summary>
public sealed class CryptoRealtimeQuoteService : IRealtimeQuoteService
{
    private readonly BinanceWebSocketService _wsService;

    /// <summary>Binance 交易对（小写）→ 应用层资产代码。推送回调据此还原为调用方订阅时的代码。</summary>
    private readonly ConcurrentDictionary<string, string> _codeBySymbol = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, decimal, decimal>? PriceUpdated;

    public CryptoRealtimeQuoteService(BinanceWebSocketService wsService, ILogger<CryptoRealtimeQuoteService> logger)
    {
        _wsService = wsService;
        _wsService.PriceUpdated += OnWebSocketPriceUpdated;
    }

    public Task SubscribeAsync(string subscriberKey, IEnumerable<string> codes)
    {
        var codeList = codes as IList<string> ?? codes.ToList();
        foreach (var code in codeList)
        {
            _codeBySymbol[ToBinanceFormat(code)] = code;
        }
        return _wsService.SubscribeAsync(subscriberKey, codeList.Select(code => ToBinanceFormat(code)));
    }

    public Task UnsubscribeAllAsync(string subscriberKey)
        => _wsService.UnsubscribeAllAsync(subscriberKey);

    private void OnWebSocketPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        if (_codeBySymbol.TryGetValue(symbol, out var code))
        {
            PriceUpdated?.Invoke(code, lastPrice, changePercent);
        }
    }
}
