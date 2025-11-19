using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools;

public sealed class StockTechnicalTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSettingService _userSettingService;

    public StockTechnicalTools(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
    }

    private async Task<T> GetStockIndicatorAsync<T>(string indicator, string stockSymbol)
    {
        var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
        var url = $"https://api.zhituapi.com/hs/history/{indicator}/{StockSymbolConverter.ToZhiTuFormat(stockSymbol)}/d/n?token={token}&lt=30";
        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetStringAsync(url);
        var items = JsonSerializer.Deserialize<List<T>>(response);

        if (items == null || !items.Any())
            throw new Exception($"获取{indicator.ToUpper()}数据失败: 返回数据为空或无有效数据");

        return items.Last();
    }

    [Description("获取近30日最新日线KDJ")]
    public Task<StockKDJ> GetStockKDJAsync([Description("股票代码，支持含前缀或仅数字")] string stockSymbol)
        => GetStockIndicatorAsync<StockKDJ>("kdj", stockSymbol);

    [Description("获取近30日最新日线MACD")]
    public Task<StockMACD> GetStockMACDAsync([Description("股票代码，支持含前缀或仅数字")] string stockSymbol)
        => GetStockIndicatorAsync<StockMACD>("macd", stockSymbol);

    [Description("获取近30日最新日线BOLL")]
    public Task<StockBoll> GetStockBOLLAsync([Description("股票代码，支持含前缀或仅数字")] string stockSymbol)
        => GetStockIndicatorAsync<StockBoll>("boll", stockSymbol);

    [Description("获取近30日最新日线MA")]
    public Task<StockMA> GetStockMAAsync([Description("股票代码，支持含前缀或仅数字")] string stockSymbol)
        => GetStockIndicatorAsync<StockMA>("ma", stockSymbol);

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetStockKDJAsync);
        yield return AIFunctionFactory.Create(GetStockMACDAsync);
        yield return AIFunctionFactory.Create(GetStockBOLLAsync);
        yield return AIFunctionFactory.Create(GetStockMAAsync);
    }
}
