using MarketAssistant.Applications.Market.Models;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// A 股五档盘口：复用新浪个股行情接口（与顶栏指数同源，Referer 与重试策略已统一配置）。
/// </summary>
/// <remarks>
/// 新浪仅提供五档（无 Crypto 那样的多档深度），因此忽略 <c>depth</c> 参数中大于 5 的部分。
/// </remarks>
public sealed class AShareOrderBookService(
    SinaStockQuoteClient client,
    ILogger<AShareOrderBookService> logger) : IOrderBookService
{
    /// <summary>新浪个股接口固定返回五档。</summary>
    public const int MaxDepth = 5;

    private readonly SinaStockQuoteClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly ILogger<AShareOrderBookService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<OrderBookSnapshot?> GetOrderBookAsync(
        string code,
        int depth = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var sinaCode = StockSymbolConverter.ToClsFormat(code);
        var quote = await _client.GetStockQuoteAsync(sinaCode, cancellationToken);
        if (quote is null)
        {
            _logger.LogInformation("新浪未返回 {Code} 的五档盘口（数据不可用或接口未覆盖该标的）", sinaCode);
            return null;
        }

        var limit = Math.Clamp(depth, 1, MaxDepth);
        var bids = quote.Bids.Take(limit).Select(level => new OrderBookLevel(level.Price, level.Quantity)).ToList();
        var asks = quote.Asks.Take(limit).Select(level => new OrderBookLevel(level.Price, level.Quantity)).ToList();

        if (bids.Count == 0 && asks.Count == 0)
        {
            return null;
        }

        return new OrderBookSnapshot(bids, asks);
    }
}
