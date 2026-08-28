using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Crypto;

/// <summary>
/// 交易所交易对精度过滤器（LOT_SIZE stepSize / PRICE_FILTER tickSize）的进程内缓存。
/// 下单前按过滤器对数量/价格取整，避免因精度违规被交易所拒单（如 -1111、-1013）。
/// 过滤器信息极少变化，缓存 24 小时，避免每个下单请求都拉取 exchangeInfo。
/// 拿不到过滤器信息时调用方应保持原始值下单（宁可信服交易所报错也不阻断交易）。
/// </summary>
public sealed class ExchangeSymbolFilterCache
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// 按 "httpClientName:SYMBOL" 维度缓存（实盘/Testnet/Demo 各自独立），跨实例共享，
    /// 因为账户服务由工厂按模式临时创建，实例级缓存会失效。
    /// </summary>
    private static readonly ConcurrentDictionary<string, (DateTimeOffset FetchedAt, SymbolFilters Filters)> Cache = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public ExchangeSymbolFilterCache(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// 获取指定交易对的精度过滤器；网络失败或解析失败返回 null（调用方保持原值并继续）。
    /// </summary>
    /// <param name="symbol">交易对（如 BTCUSDT）</param>
    /// <param name="httpClientName">命名 HttpClient（决定现货/合约/Testnet 域名）</param>
    /// <param name="exchangeInfoEndpoint">exchangeInfo 端点（现货 /api/v3/exchangeInfo，合约 /fapi/v1/exchangeInfo）</param>
    public async Task<SymbolFilters?> GetFiltersAsync(
        string symbol, string httpClientName, string exchangeInfoEndpoint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var normalizedSymbol = symbol.ToUpperInvariant();
        var cacheKey = $"{httpClientName}:{normalizedSymbol}";

        if (Cache.TryGetValue(cacheKey, out var cached) &&
            DateTimeOffset.UtcNow - cached.FetchedAt < CacheLifetime)
        {
            return cached.Filters;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(httpClientName);
            var url = $"{exchangeInfoEndpoint}?symbol={Uri.EscapeDataString(normalizedSymbol)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content
                .ReadFromJsonAsync<ExchangeInfoResponse>(cancellationToken);
            var info = payload?.Symbols?.FirstOrDefault(s =>
                string.Equals(s.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase));
            if (info == null)
            {
                _logger.LogDebug("exchangeInfo 中未找到交易对 {Symbol}（{HttpClientName}）", normalizedSymbol, httpClientName);
                return null;
            }

            var filters = new SymbolFilters
            {
                StepSize = ParsePositiveDecimal(info.LotSizeFilter?.StepSize),
                TickSize = ParsePositiveDecimal(info.PriceFilter?.TickSize)
            };

            Cache[cacheKey] = (DateTimeOffset.UtcNow, filters);
            return filters;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 拿不到过滤器时保持原值下单，由交易所侧校验兜底
            _logger.LogDebug(ex, "获取 {Symbol} 的交易所精度过滤器失败，下单将使用原始数量/价格", normalizedSymbol);
            return null;
        }
    }

    /// <summary>
    /// 数量向下取整到 stepSize 的整数倍（宁可少买不可超量）。
    /// </summary>
    public static decimal? RoundQuantityToStep(decimal quantity, decimal? stepSize)
        => RoundToStep(quantity, stepSize, floor: true);

    /// <summary>
    /// 价格四舍五入到 tickSize 的整数倍。
    /// </summary>
    public static decimal? RoundPriceToTick(decimal price, decimal? tickSize)
        => RoundToStep(price, tickSize, floor: false);

    /// <summary>
    /// 将值取整为步长的整数倍；步长无效或为 0 时返回 null（表示保持原值）。
    /// </summary>
    private static decimal? RoundToStep(decimal value, decimal? step, bool floor)
    {
        if (!step.HasValue || step.Value <= 0 || value <= 0)
            return null;

        var steps = value / step.Value;
        var roundedSteps = floor
            ? Math.Floor(steps)
            : Math.Round(steps, MidpointRounding.AwayFromZero);
        return decimal.Round(roundedSteps * step.Value, GetDecimalPlaces(step.Value));
    }

    /// <summary>
    /// 由步长字符串推断结果应保留的小数位数（如 stepSize 0.001 → 3 位），
    /// 消除 decimal 乘法产生的多余尾位。
    /// </summary>
    private static int GetDecimalPlaces(decimal step)
    {
        var text = step.ToString("0.############################", CultureInfo.InvariantCulture);
        var dotIndex = text.IndexOf('.');
        return dotIndex < 0 ? 0 : text.Length - dotIndex - 1;
    }

    private static decimal? ParsePositiveDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private sealed class ExchangeInfoResponse
    {
        public List<ExchangeSymbolInfo>? Symbols { get; set; }
    }

    private sealed class ExchangeSymbolInfo
    {
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName("filters")]
        public List<ExchangeSymbolFilter>? Filters { get; set; }

        public ExchangeSymbolFilter? LotSizeFilter => Filters?.FirstOrDefault(f =>
            string.Equals(f.FilterType, "LOT_SIZE", StringComparison.OrdinalIgnoreCase));

        public ExchangeSymbolFilter? PriceFilter => Filters?.FirstOrDefault(f =>
            string.Equals(f.FilterType, "PRICE_FILTER", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ExchangeSymbolFilter
    {
        [JsonPropertyName("filterType")]
        public string FilterType { get; set; } = string.Empty;

        [JsonPropertyName("stepSize")]
        public string? StepSize { get; set; }

        [JsonPropertyName("tickSize")]
        public string? TickSize { get; set; }
    }

    /// <summary>
    /// 交易对精度过滤器：数量步长与价格步长。
    /// </summary>
    public sealed record SymbolFilters
    {
        /// <summary>LOT_SIZE stepSize：下单数量必须是该值的整数倍</summary>
        public decimal? StepSize { get; init; }

        /// <summary>PRICE_FILTER tickSize：价格必须是该值的整数倍</summary>
        public decimal? TickSize { get; init; }
    }
}
