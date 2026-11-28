using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Technical;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股技术分析工具实现
/// </summary>
public sealed class AShareTechnicalTools(
    ZhiTuMarketClient zhiTuClient,
    IUserSettingService userSettingService,
    ILogger<AShareTechnicalTools> logger) : ITechnicalDataTools
{
    private readonly ZhiTuMarketClient _zhiTuClient = zhiTuClient;
    private readonly IUserSettingService _userSettingService = userSettingService;
    private readonly ILogger<AShareTechnicalTools> _logger = logger;

    private Task<T> GetIndicatorAsync<T>(string indicator, string assetSymbol, CancellationToken cancellationToken = default)
    {
        return ToolExecutor.ExecuteAsync($"获取技术指标 {indicator.ToUpperInvariant()}", assetSymbol, _logger, async _ =>
        {
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
            var formattedSymbol = StockSymbolConverter.ToZhiTuFormat(assetSymbol);
            var items = await _zhiTuClient.GetIndicatorListAsync<T>(indicator, formattedSymbol, token, cancellationToken);

            if (items.Count == 0)
                throw new FriendlyException($"获取 {indicator.ToUpperInvariant()} 数据失败: 返回数据为空或无有效数据 (代码: {formattedSymbol})");

            return items.Last();
        }, cancellationToken);
    }

    [Description("获取近30日最新日线KDJ")]
    public Task<TechnicalKDJ> GetKDJAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetIndicatorAsync<TechnicalKDJ>("kdj", assetSymbol, cancellationToken);

    [Description("获取近30日最新日线MACD")]
    public Task<TechnicalMACD> GetMACDAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetIndicatorAsync<TechnicalMACD>("macd", assetSymbol, cancellationToken);

    [Description("获取近30日最新日线BOLL")]
    public Task<TechnicalBoll> GetBOLLAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetIndicatorAsync<TechnicalBoll>("boll", assetSymbol, cancellationToken);

    [Description("获取近30日最新日线MA")]
    public Task<TechnicalMA> GetMAAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetIndicatorAsync<TechnicalMA>("ma", assetSymbol, cancellationToken);

    [Description("获取K线历史序列（OHLCV），interval支持5m/15m/daily/weekly，用于判断趋势方向")]
    public async Task<List<OhlcvBar>> GetKLinesAsync(
        [Description("股票代码")] string assetSymbol,
        [Description("K线周期：5m/15m/daily/weekly")] string interval = "daily",
        [Description("返回根数，最大250")] int count = 30,
        CancellationToken cancellationToken = default)
    {
        return await ToolExecutor.ExecuteAsync<List<OhlcvBar>>("获取K线序列", assetSymbol, _logger, async _ =>
        {
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
            var formattedSymbol = StockSymbolConverter.ToZhiTuFormat(assetSymbol);
            var zhiTuInterval = interval.ToLowerInvariant() switch
            {
                "5m" => "5",
                "15m" => "15",
                "weekly" => "w",
                _ => "d"
            };

            var clampedCount = Math.Clamp(count, 1, 250);
            var daysBack = zhiTuInterval switch
            {
                "5" or "15" => 60,
                "w" => clampedCount * 7 + 14,
                _ => (int)(clampedCount * 1.6) + 10
            };

            var startDate = DateTime.Now.AddDays(-daysBack).ToString("yyyyMMdd");
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var items = await _zhiTuClient.GetKLineBarsAsync<ZhiTuKLineBar>(
                formattedSymbol, zhiTuInterval, token, startDate, endDate, cancellationToken);

            if (items.Count == 0)
                throw new FriendlyException($"K线数据为空 (代码: {formattedSymbol})");

            // OHLC 为 null 的 bar 是上游缺失的无效数据，跳过；缺失成交量按 0 计
            return [.. items
                .Where(x => x.O.HasValue && x.H.HasValue && x.L.HasValue && x.C.HasValue)
                .OrderBy(x => x.T)
                .TakeLast(clampedCount)
                .Select(x => new OhlcvBar
                {
                    T = x.T,
                    O = x.O.GetValueOrDefault(),
                    H = x.H.GetValueOrDefault(),
                    L = x.L.GetValueOrDefault(),
                    C = x.C.GetValueOrDefault(),
                    V = x.V ?? 0m
                })];
        }, cancellationToken);
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetKDJAsync);
        yield return AIFunctionFactory.Create(GetMACDAsync);
        yield return AIFunctionFactory.Create(GetBOLLAsync);
        yield return AIFunctionFactory.Create(GetMAAsync);
        yield return AIFunctionFactory.Create(GetKLinesAsync);
    }

    /// <summary>
    /// 智兔 K 线条目。OHLC 缺失（null）的 bar 为无效数据，映射时跳过；成交量缺失按 0 计。
    /// </summary>
    private sealed class ZhiTuKLineBar
    {
        [JsonPropertyName("t")] public string T { get; init; } = "";
        [JsonPropertyName("o")] public decimal? O { get; init; }
        [JsonPropertyName("h")] public decimal? H { get; init; }
        [JsonPropertyName("l")] public decimal? L { get; init; }
        [JsonPropertyName("c")] public decimal? C { get; init; }
        [JsonPropertyName("v")] public decimal? V { get; init; }
    }
}
