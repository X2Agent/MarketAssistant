using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Technical;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<AShareTechnicalTools> _logger;

    public AShareTechnicalTools(
        IHttpClientFactory httpClientFactory,
        IUserSettingService userSettingService,
        ILogger<AShareTechnicalTools> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _logger = logger;
    }

    private async Task<T> GetIndicatorAsync<T>(string indicator, string assetSymbol)
    {
        try
        {
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
            var formattedSymbol = StockSymbolConverter.ToZhiTuFormat(assetSymbol);
            var url = $"/hs/history/{indicator}/{formattedSymbol}/d/n?token={token}&lt=30";

            using var httpClient = _httpClientFactory.CreateClient("ZhiTu");
            var response = await httpClient.GetStringAsync(url);
            var items = JsonSerializer.Deserialize<List<T>>(response);

            if (items == null || !items.Any())
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
    public Task<TechnicalKDJ> GetKDJAsync([Description("股票代码")] string assetSymbol)
        => GetIndicatorAsync<TechnicalKDJ>("kdj", assetSymbol);

    [Description("获取近30日最新日线MACD")]
    public Task<TechnicalMACD> GetMACDAsync([Description("股票代码")] string assetSymbol)
        => GetIndicatorAsync<TechnicalMACD>("macd", assetSymbol);

    [Description("获取近30日最新日线BOLL")]
    public Task<TechnicalBoll> GetBOLLAsync([Description("股票代码")] string assetSymbol)
        => GetIndicatorAsync<TechnicalBoll>("boll", assetSymbol);

    [Description("获取近30日最新日线MA")]
    public Task<TechnicalMA> GetMAAsync([Description("股票代码")] string assetSymbol)
        => GetIndicatorAsync<TechnicalMA>("ma", assetSymbol);

    [Description("获取K线历史序列（OHLCV），interval支持5m/15m/daily/weekly，用于判断趋势方向")]
    public async Task<List<OhlcvBar>> GetKLinesAsync(
        [Description("股票代码")] string assetSymbol,
        [Description("K线周期：5m/15m/daily/weekly")] string interval = "daily",
        [Description("返回根数，最大100")] int count = 30)
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

            var clampedCount = Math.Clamp(count, 1, 100);
            var daysBack = zhiTuInterval switch
            {
                "5" or "15" => 30,
                "w" => clampedCount * 7 + 14,
                _ => (int)(clampedCount * 1.6) + 10
            };

            var startDate = DateTime.Now.AddDays(-daysBack).ToString("yyyyMMdd");
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var url = $"/hs/history/{formattedSymbol}/{zhiTuInterval}/n?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient("ZhiTu");
            var response = await httpClient.GetStringAsync(url);
            var items = JsonSerializer.Deserialize<List<ZhiTuKLineBar>>(response);

            if (items == null || items.Count == 0)
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
