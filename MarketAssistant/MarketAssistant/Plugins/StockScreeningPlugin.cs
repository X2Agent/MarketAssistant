using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins.Models;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace MarketAssistant.Plugins;

/// <summary>
/// 股票筛选插件，提供股票池筛选和排序功能
/// </summary>
public sealed class StockScreeningPlugin
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _zhiTuToken;

    public StockScreeningPlugin(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _zhiTuToken = userSettingService.CurrentSetting.ZhiTuApiToken;
    }

    /// <summary>
    /// 根据市值范围筛选股票
    /// </summary>
    [KernelFunction("screen_stocks_by_market_cap"), Description("根据市值范围筛选股票，返回符合条件的股票列表")]
    public async Task<List<StockQuoteInfo>> ScreenStocksByMarketCapAsync(
        [Description("最小市值，单位亿元")] decimal minMarketCap = 0,
        [Description("最大市值，单位亿元")] decimal maxMarketCap = 10000,
        [Description("返回股票数量限制")] int limit = 50)
    {
        try
        {
            // 获取A股股票列表（这里使用模拟数据，实际应该调用真实API）
            var stockCodes = await GetStockListAsync();
            var filteredStocks = new List<StockQuoteInfo>();

            foreach (var stockCode in stockCodes.Take(limit * 2)) // 多取一些以便筛选
            {
                try
                {
                    var stockInfo = await GetStockBasicInfoAsync(stockCode);
                    if (stockInfo != null && 
                        stockInfo.MarketCapitalization >= minMarketCap && 
                        stockInfo.MarketCapitalization <= maxMarketCap)
                    {
                        filteredStocks.Add(stockInfo);
                        if (filteredStocks.Count >= limit) break;
                    }
                }
                catch
                {
                    // 忽略单个股票获取失败的情况
                    continue;
                }
            }

            return filteredStocks;
        }
        catch (Exception ex)
        {
            throw new Exception($"按市值筛选股票时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 根据PE范围筛选股票
    /// </summary>
    [KernelFunction("screen_stocks_by_pe"), Description("根据市盈率范围筛选股票，返回符合条件的股票列表")]
    public async Task<List<StockQuoteInfo>> ScreenStocksByPEAsync(
        [Description("最小市盈率")] decimal minPE = 0,
        [Description("最大市盈率")] decimal maxPE = 100,
        [Description("返回股票数量限制")] int limit = 50)
    {
        try
        {
            var stockCodes = await GetStockListAsync();
            var filteredStocks = new List<StockQuoteInfo>();

            foreach (var stockCode in stockCodes.Take(limit * 2))
            {
                try
                {
                    var stockInfo = await GetStockBasicInfoAsync(stockCode);
                    if (stockInfo != null && 
                        stockInfo.PERatio >= minPE && 
                        stockInfo.PERatio <= maxPE &&
                        stockInfo.PERatio > 0) // 排除负PE
                    {
                        filteredStocks.Add(stockInfo);
                        if (filteredStocks.Count >= limit) break;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return filteredStocks;
        }
        catch (Exception ex)
        {
            throw new Exception($"按PE筛选股票时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 根据涨跌幅筛选股票
    /// </summary>
    [KernelFunction("screen_stocks_by_change"), Description("根据涨跌幅范围筛选股票，可用于寻找强势股或超跌股")]
    public async Task<List<StockQuoteInfo>> ScreenStocksByChangeAsync(
        [Description("最小涨跌幅百分比")] decimal minChange = -10,
        [Description("最大涨跌幅百分比")] decimal maxChange = 10,
        [Description("返回股票数量限制")] int limit = 50)
    {
        try
        {
            var stockCodes = await GetStockListAsync();
            var filteredStocks = new List<StockQuoteInfo>();

            foreach (var stockCode in stockCodes.Take(limit * 2))
            {
                try
                {
                    var stockInfo = await GetStockBasicInfoAsync(stockCode);
                    if (stockInfo != null && 
                        stockInfo.PercentageChange >= minChange && 
                        stockInfo.PercentageChange <= maxChange)
                    {
                        filteredStocks.Add(stockInfo);
                        if (filteredStocks.Count >= limit) break;
                    }
                }
                catch
                {
                    continue;
                }
            }

            // 按涨跌幅排序
            return filteredStocks.OrderByDescending(s => s.PercentageChange).ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"按涨跌幅筛选股票时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 根据成交量筛选活跃股票
    /// </summary>
    [KernelFunction("screen_active_stocks"), Description("根据成交量和换手率筛选活跃股票")]
    public async Task<List<StockQuoteInfo>> ScreenActiveStocksAsync(
        [Description("最小换手率百分比")] decimal minTurnoverRate = 1,
        [Description("最小成交额，单位亿元")] decimal minAmount = 1,
        [Description("返回股票数量限制")] int limit = 50)
    {
        try
        {
            var stockCodes = await GetStockListAsync();
            var filteredStocks = new List<StockQuoteInfo>();

            foreach (var stockCode in stockCodes.Take(limit * 2))
            {
                try
                {
                    var stockInfo = await GetStockBasicInfoAsync(stockCode);
                    if (stockInfo != null && 
                        stockInfo.TurnoverRate >= minTurnoverRate && 
                        stockInfo.Amount >= minAmount)
                    {
                        filteredStocks.Add(stockInfo);
                        if (filteredStocks.Count >= limit) break;
                    }
                }
                catch
                {
                    continue;
                }
            }

            // 按换手率排序
            return filteredStocks.OrderByDescending(s => s.TurnoverRate).ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"筛选活跃股票时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取股票列表（模拟实现）
    /// </summary>
    private async Task<List<string>> GetStockListAsync()
    {
        // 这里应该调用真实的API获取股票列表
        // 目前返回一些常见的股票代码作为示例
        await Task.Delay(100); // 模拟网络延迟
        
        return new List<string>
        {
            "sz000001", "sz000002", "sz000858", "sz002594", "sz002415",
            "sh600000", "sh600036", "sh600519", "sh600887", "sh601318",
            "sh601398", "sh601857", "sh601988", "sh603259", "sh688981",
            "sz300059", "sz300750", "sz300760", "sz300896", "sz301029"
        };
    }

    /// <summary>
    /// 获取单个股票的基本信息
    /// </summary>
    private async Task<StockQuoteInfo?> GetStockBasicInfoAsync(string stockSymbol)
    {
        try
        {
            var url = $"https://x-quote.cls.cn/quote/stock/basic?secu_code={stockSymbol}&fields=open_px,av_px,high_px,low_px,change,change_px,down_price,change_3,change_5,qrr,entrust_rate,tr,amp,TotalShares,mc,NetAssetPS,NonRestrictedShares,cmc,business_amount,business_balance,pe,ttm_pe,pb,secu_name,secu_code,trade_status,secu_type,preclose_px,up_price,last_px&app=CailianpressWeb&os=web&sv=8.4.6";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var jsonDocument = JsonDocument.Parse(response);

            var stockPriceInfo = new StockQuoteInfo();
            var data = jsonDocument.RootElement.GetProperty("data");

            stockPriceInfo.CurrentPrice = data.GetProperty("last_px").GetDecimal();
            stockPriceInfo.PriceChange = data.GetProperty("change_px").GetDecimal();
            stockPriceInfo.PercentageChange = data.GetProperty("change").GetDecimal();
            stockPriceInfo.HighPrice = data.GetProperty("high_px").GetDecimal();
            stockPriceInfo.LowPrice = data.GetProperty("low_px").GetDecimal();
            stockPriceInfo.Volume = data.GetProperty("business_amount").GetDecimal() / 10000;
            stockPriceInfo.Amount = data.GetProperty("business_balance").GetDecimal() / 100000000;
            stockPriceInfo.TurnoverRate = data.GetProperty("tr").GetDecimal();
            stockPriceInfo.PercentageChange3Day = data.GetProperty("change_3").GetDecimal();
            stockPriceInfo.PercentageChange5Day = data.GetProperty("change_5").GetDecimal();
            stockPriceInfo.TotalShares = data.GetProperty("TotalShares").GetDecimal();
            stockPriceInfo.MarketCapitalization = data.GetProperty("mc").GetDecimal() / 100000000;
            stockPriceInfo.SecurityName = data.GetProperty("secu_name").GetString() ?? string.Empty;
            stockPriceInfo.SecurityCode = data.GetProperty("secu_code").GetString() ?? string.Empty;
            stockPriceInfo.TradeStatus = data.GetProperty("trade_status").GetString() ?? string.Empty;
            stockPriceInfo.SecurityType = data.GetProperty("secu_type").GetString() ?? string.Empty;
            stockPriceInfo.OpenPrice = data.GetProperty("open_px").GetDecimal();
            stockPriceInfo.PreviousClosePrice = data.GetProperty("preclose_px").GetDecimal();
            stockPriceInfo.UpLimitPrice = data.GetProperty("up_price").GetDecimal();
            stockPriceInfo.DownLimitPrice = data.GetProperty("down_price").GetDecimal();
            stockPriceInfo.Amplitude = data.GetProperty("amp").GetDecimal();
            stockPriceInfo.PERatio = data.GetProperty("pe").GetDecimal();
            stockPriceInfo.TTMPERatio = data.GetProperty("ttm_pe").GetDecimal();
            stockPriceInfo.PBRatio = data.GetProperty("pb").GetDecimal();
            stockPriceInfo.CirculationMarketCap = data.GetProperty("cmc").GetDecimal() / 100000000;
            stockPriceInfo.NonRestrictedShares = data.GetProperty("NonRestrictedShares").GetDecimal();
            stockPriceInfo.NetAssetPerShare = data.GetProperty("NetAssetPS").GetDecimal();
            stockPriceInfo.AveragePrice = data.GetProperty("av_px").GetDecimal();
            stockPriceInfo.VolumeRatio = data.GetProperty("qrr").GetDecimal();
            stockPriceInfo.EntrustRatio = data.GetProperty("entrust_rate").GetDecimal();

            return stockPriceInfo;
        }
        catch
        {
            return null;
        }
    }
}