using MarketAssistant.Applications.Market.Models;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// 虚拟币逐笔成交：复用币安 <c>/api/v3/trades</c>（返回最近成交，按时间升序）。
/// </summary>
public sealed class CryptoTickTradeService(
    BinanceMarketDataService binanceService,
    ILogger<CryptoTickTradeService> logger) : ITickTradeService
{
    private readonly BinanceMarketDataService _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
    private readonly ILogger<CryptoTickTradeService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<TickTrade>> GetRecentTradesAsync(
        string code,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return [];

        var symbol = CryptoSymbolConverter.ToBinanceFormat(code);
        var response = await _binanceService.GetRecentTradesAsync(symbol, Math.Clamp(limit, 1, 1000), cancellationToken);
        if (response is null || response.Count == 0)
        {
            _logger.LogInformation("币安未返回 {Symbol} 的成交记录", symbol);
            return [];
        }

        var trades = new List<TickTrade>(response.Count);
        foreach (var item in response)
        {
            if (item is not JsonObject obj)
                continue;

            trades.Add(new TickTrade(
                Price: CryptoOrderBookService.ParseDecimal(obj["price"]),
                Quantity: CryptoOrderBookService.ParseDecimal(obj["qty"]),
                Timestamp: ParseTimestamp(obj["time"]),
                IsBuyerMaker: ParseFlag(obj["isBuyerMaker"]),
                TradeId: ParseTradeId(obj["id"])));
        }

        // 币安按时间升序返回；对外契约统一为最新在前
        trades.Reverse();
        return trades;
    }

    /// <summary>币安以毫秒时间戳返回成交时间；越界或缺失时回退到当前时间。</summary>
    private static DateTime ParseTimestamp(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<long>(out var milliseconds) &&
            milliseconds is >= -62135596800000 and <= 253402300799999)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;
        }

        return DateTime.Now;
    }

    private static long ParseTradeId(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<long>(out var id) ? id : 0;

    private static bool ParseFlag(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
