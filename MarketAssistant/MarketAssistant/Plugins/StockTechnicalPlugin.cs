using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins.Models;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace MarketAssistant.Plugins;

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

    [KernelFunction("get_stock_kdj"), Description("获取最新日线KDJ")]
    public async Task<StockKDJ> GetStockKDJAsync(string stockSymbol)
    {
        try
        {
            // 只保留 stockSymbol 中的数字部分
            stockSymbol = new string(stockSymbol.Where(char.IsDigit).ToArray());

            var url = $"https://api.zhituapi.com/hs/latest/kdj/{stockSymbol}/d?token={_zhiTuToken}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var info = JsonSerializer.Deserialize<StockKDJ>(response);

            if (info == null)
            {
                throw new Exception($"获取KDJ数据失败: 返回数据为空");
            }

            return info;
        }
        catch (Exception ex)
        {
            throw new Exception($"处理KDJ数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_stock_macd"), Description("获取最新日线MACD")]
    public async Task<StockMACD> GetStockMACDAsync(string stockSymbol)
    {
        try
        {

            // 只保留 stockSymbol 中的数字部分
            stockSymbol = new string(stockSymbol.Where(char.IsDigit).ToArray());

            var url = $"https://api.zhituapi.com/hs/latest/macd/{stockSymbol}/d?token={_zhiTuToken}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var info = JsonSerializer.Deserialize<StockMACD>(response);

            if (info == null)
            {
                throw new Exception($"获取MACD数据失败: 返回数据为空");
            }

            return info;
        }
        catch (Exception ex)
        {
            throw new Exception($"处理MACD数据时发生错误: {ex.Message}", ex);
        }
    }

    //最新分时BOLL
    [KernelFunction("get_stock_boll"), Description("获取最新日线BOLL")]
    public async Task<StockBoll> GetStockBOLLAsync(string stockSymbol)
    {
        try
        {

            // 只保留 stockSymbol 中的数字部分
            stockSymbol = new string(stockSymbol.Where(char.IsDigit).ToArray());

            var url = $"https://api.zhituapi.com/hs/latest/boll/{stockSymbol}/d?token={_zhiTuToken}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var info = JsonSerializer.Deserialize<StockBoll>(response);

            if (info == null)
            {
                throw new Exception($"获取BOLL数据失败: 返回数据为空");
            }

            return info;
        }
        catch (Exception ex)
        {

            throw new Exception($"处理BOLL数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_stock_ma"), Description("获取最新日线MA")]
    public async Task<StockMA> GetStockMAAsync(string stockSymbol)
    {
        try
        {
            // 只保留 stockSymbol 中的数字部分
            stockSymbol = new string(stockSymbol.Where(char.IsDigit).ToArray());

            var url = $"https://api.zhituapi.com/hs/latest/ma/{stockSymbol}/d?token={_zhiTuToken}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var info = JsonSerializer.Deserialize<StockMA>(response);

            if (info == null)
            {
                throw new Exception($"获取MA数据失败: 返回数据为空");
            }

            return info;
        }
        catch (Exception ex)
        {

            throw new Exception($"处理MA数据时发生错误: {ex.Message}", ex);
        }
    }
}
