using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股技术分析工具实现
/// </summary>
public sealed class AShareTechnicalTools : ITechnicalDataTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSettingService _userSettingService;

    public AShareTechnicalTools(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
    }

    private async Task<T> GetIndicatorAsync<T>(string indicator, string assetSymbol)
    {
        var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
        var url = $"https://api.zhituapi.com/hs/history/{indicator}/{StockSymbolConverter.ToZhiTuFormat(assetSymbol)}/d/n?token={token}&lt=30";
        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetStringAsync(url);
        var items = JsonSerializer.Deserialize<List<T>>(response);

        if (items == null || !items.Any())
            throw new Exception($"获取{indicator.ToUpper()}数据失败: 返回数据为空或无有效数据");

        return items.Last();
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
