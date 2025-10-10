using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Services.Settings;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MarketAssistant.Agents.Plugins;

[Description("根据股票代码获取技术指标")]
public sealed class StockTechnicalPlugin
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _zhiTuToken;

    public StockTechnicalPlugin(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _zhiTuToken = userSettingService.CurrentSetting.ZhiTuApiToken;
    }

    private string ConvertStockSymbol(string stockCode)
    {
        // 提取所有数字字符
        string digits = new string(stockCode.Where(char.IsDigit).ToArray());

        // 解析交易所标识
        string suffix = GetExchangeSuffix(digits);

        return $"{digits}.{suffix}";

        string GetExchangeSuffix(string digits)
        {
            // 沪市逻辑：60开头、688开头（科创板）、900开头（B股）
            if (digits.StartsWith("60") ||
                digits.StartsWith("688") ||
                digits.StartsWith("900"))
                return "SH";

            // 深市逻辑（其他所有情况）：00开头、300开头（创业板）、200开头（B股）
            return "SZ";
        }
    }

    private async Task<T> GetStockIndicatorAsync<T>(string indicator, string stockSymbol)
    {
        var url = $"https://api.zhituapi.com/hs/history/{indicator}/{ConvertStockSymbol(stockSymbol)}/d/n?token={_zhiTuToken}&lt=30";
        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetStringAsync(url);
        var items = JsonSerializer.Deserialize<List<T>>(response);

        if (items == null || !items.Any())
            throw new Exception($"获取{indicator.ToUpper()}数据失败: 返回数据为空或无有效数据");

        return items.Last();
    }

    [KernelFunction("get_stock_kdj"), Description("获取近30日最新日线KDJ")]
    public Task<StockKDJ> GetStockKDJAsync(string stockSymbol)
    => GetStockIndicatorAsync<StockKDJ>("kdj", stockSymbol);

    [KernelFunction("get_stock_macd"), Description("获取近30日最新日线MACD")]
    public Task<StockMACD> GetStockMACDAsync(string stockSymbol)
        => GetStockIndicatorAsync<StockMACD>("macd", stockSymbol);

    [KernelFunction("get_stock_boll"), Description("获取近30日最新日线BOLL")]
    public Task<StockBoll> GetStockBOLLAsync(string stockSymbol)
        => GetStockIndicatorAsync<StockBoll>("boll", stockSymbol);

    [KernelFunction("get_stock_ma"), Description("获取近30日最新日线MA")]
    public Task<StockMA> GetStockMAAsync(string stockSymbol)
        => GetStockIndicatorAsync<StockMA>("ma", stockSymbol);
}
