using MarketAssistant.Applications.Market.Models;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json.Nodes;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// 虚拟币盘口深度：复用币安 <c>/api/v3/depth</c>。
/// 解析逻辑集中在此，Agent 工具与 UI 共用同一份实现。
/// </summary>
public sealed class CryptoOrderBookService : IOrderBookService
{
    /// <summary>币安 depth 接口允许的档位值；其它值会被服务端拒绝。</summary>
    private static readonly int[] AllowedLimits = [5, 10, 20, 50, 100, 500, 1000, 5000];

    private readonly BinanceMarketDataService _binanceService;
    private readonly ILogger<CryptoOrderBookService> _logger;

    public CryptoOrderBookService(
        BinanceMarketDataService binanceService,
        ILogger<CryptoOrderBookService> logger)
    {
        _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OrderBookSnapshot?> GetOrderBookAsync(
        string code,
        int depth = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var symbol = CryptoSymbolConverter.ToBinanceFormat(code);
        var response = await _binanceService.GetDepthAsync(symbol, NormalizeLimit(depth), cancellationToken);
        if (response is null)
        {
            _logger.LogInformation("币安未返回 {Symbol} 的深度数据", symbol);
            return null;
        }

        var bids = ParseLevels(response["bids"]);
        var asks = ParseLevels(response["asks"]);

        if (bids.Count == 0 && asks.Count == 0)
        {
            _logger.LogInformation("币安返回的 {Symbol} 深度数据为空", symbol);
            return null;
        }

        return new OrderBookSnapshot(bids, asks, ParseLong(response["lastUpdateId"]));
    }

    /// <summary>把请求档位吸附到币安允许的最近档位（向上取，至少 5 档）。</summary>
    internal static int NormalizeLimit(int depth)
    {
        var requested = Math.Max(depth, AllowedLimits[0]);
        foreach (var limit in AllowedLimits)
        {
            if (limit >= requested)
                return limit;
        }

        return AllowedLimits[^1];
    }

    /// <summary>
    /// 解析币安 depth 的 <c>[[price, quantity], ...]</c> 数组。币安已按买盘降序、卖盘升序返回。
    /// </summary>
    internal static List<OrderBookLevel> ParseLevels(JsonNode? node)
    {
        var levels = new List<OrderBookLevel>();
        if (node is not JsonArray array)
            return levels;

        foreach (var entry in array)
        {
            if (entry is not JsonArray pair || pair.Count < 2)
                continue;

            levels.Add(new OrderBookLevel(
                ParseDecimal(pair[0]),
                ParseDecimal(pair[1])));
        }

        return levels;
    }

    /// <summary>币安以字符串返回数值，兼容数字字面量；无法解析时返回 0。</summary>
    internal static decimal ParseDecimal(JsonNode? node) =>
        decimal.TryParse(node?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    internal static long ParseLong(JsonNode? node) =>
        long.TryParse(node?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0L;
}
