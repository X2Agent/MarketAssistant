using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Technical;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股技术分析工具实现
/// </summary>
public sealed class AShareTechnicalTools : ITechnicalDataTools
{
    private readonly ZhiTuMarketClient _zhiTuClient;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<AShareTechnicalTools> _logger;

    public AShareTechnicalTools(
        ZhiTuMarketClient zhiTuClient,
        IUserSettingService userSettingService,
        ILogger<AShareTechnicalTools> logger)
    {
        _zhiTuClient = zhiTuClient ?? throw new ArgumentNullException(nameof(zhiTuClient));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _logger = logger;
    }

    private async Task<T> GetIndicatorAsync<T>(string indicator, string assetSymbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
            var formattedSymbol = StockSymbolConverter.ToZhiTuFormat(assetSymbol);
            var items = await _zhiTuClient.GetIndicatorListAsync<T>(indicator, formattedSymbol, token, cancellationToken);

            if (items.Count == 0)
                throw new FriendlyException($"获取 {indicator.ToUpper()} 数据失败: 返回数据为空或无有效数据 (代码: {formattedSymbol})");

            return items.Last();
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取技术指标失败: {Indicator} {Symbol}", indicator, assetSymbol);
            throw new FriendlyException($"获取技术指标 {indicator} 时发生错误: {ex.Message} (代码: {assetSymbol})", ex);
        }
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
        try
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

            return items
                .OrderBy(x => x.T)
                .TakeLast(clampedCount)
                .Select(x => new OhlcvBar { T = x.T, O = x.O, H = x.H, L = x.L, C = x.C, V = x.V })
                .ToList();
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取K线序列失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取K线序列失败: {ex.Message} (代码: {assetSymbol})", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetKDJAsync);
        yield return AIFunctionFactory.Create(GetMACDAsync);
        yield return AIFunctionFactory.Create(GetBOLLAsync);
        yield return AIFunctionFactory.Create(GetMAAsync);
        yield return AIFunctionFactory.Create(GetKLinesAsync);
    }

    private sealed class ZhiTuKLineBar
    {
        [JsonPropertyName("t")] public string T { get; init; } = "";
        [JsonPropertyName("o")] public decimal O { get; init; }
        [JsonPropertyName("h")] public decimal H { get; init; }
        [JsonPropertyName("l")] public decimal L { get; init; }
        [JsonPropertyName("c")] public decimal C { get; init; }
        [JsonPropertyName("v")] public decimal V { get; init; }
    }
}
