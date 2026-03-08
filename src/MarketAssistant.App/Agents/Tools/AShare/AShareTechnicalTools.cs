using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Technical;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

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

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetKDJAsync);
        yield return AIFunctionFactory.Create(GetMACDAsync);
        yield return AIFunctionFactory.Create(GetBOLLAsync);
        yield return AIFunctionFactory.Create(GetMAAsync);
    }
}
