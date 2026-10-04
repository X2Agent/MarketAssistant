using MarketAssistant.Applications.Market.Models;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// A 股逐笔成交：复用新浪个股行情接口的成交明细字段。
/// </summary>
/// <remarks>
/// <b>已知限制：</b>新浪该字段不含主动买卖方向，因此所有记录的
/// <see cref="TickTrade.IsBuyerMaker"/> 一律为 <c>false</c>，
/// <see cref="TickTrade.IsActiveBuy"/> 不具业务含义。
/// 调用方（如逐笔列表着色）不得据此推断内外盘，否则会向用户展示错误的主动性判断。
/// </remarks>
public sealed class AShareTickTradeService(
    SinaStockQuoteClient client,
    ILogger<AShareTickTradeService> logger) : ITickTradeService
{
    private readonly SinaStockQuoteClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly ILogger<AShareTickTradeService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<TickTrade>> GetRecentTradesAsync(
        string code,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return [];
        }

        var sinaCode = StockSymbolConverter.ToClsFormat(code);
        var quote = await _client.GetStockQuoteAsync(sinaCode, cancellationToken);
        if (quote is null || quote.Ticks.Count == 0)
        {
            _logger.LogInformation("新浪未返回 {Code} 的逐笔成交", sinaCode);
            return [];
        }

        var take = Math.Max(limit, 1);

        // 新浪逐笔字段无成交时间戳，只有价格与数量；以抓取时刻作为时间占位，
        // 保证列表排序与展示统一，同时不虚构精度。
        var capturedAt = DateTime.Now;
        var trades = quote.Ticks
            .Take(take)
            .Select((tick, index) => new TickTrade(
                tick.Price,
                tick.Quantity,
                capturedAt,
                IsBuyerMaker: false))
            .ToList();

        return trades;
    }
}
