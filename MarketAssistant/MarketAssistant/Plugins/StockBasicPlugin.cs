using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins.Models;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
namespace MarketAssistant.Plugins;

public sealed class StockBasicPlugin
{
    //https://cxdata.caixin.com/stock/details/101002165
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _zhiTuToken;

    public StockBasicPlugin(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _zhiTuToken = userSettingService.CurrentSetting.ZhiTuApiToken;
    }

    [KernelFunction("get_stock_info"), Description("根据股票代码获取股票基本数据")]
    public async Task<StockQuoteInfo> GetStockInfoAsync(string stockSymbol)
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
            stockPriceInfo.Volume = data.GetProperty("business_amount").GetDecimal() / 10000; // 转换为万手
            stockPriceInfo.Amount = data.GetProperty("business_balance").GetDecimal() / 100000000; // 转换为亿
            stockPriceInfo.TurnoverRate = data.GetProperty("tr").GetDecimal();
            stockPriceInfo.PercentageChange3Day = data.GetProperty("change_3").GetDecimal();
            stockPriceInfo.PercentageChange5Day = data.GetProperty("change_5").GetDecimal();
            stockPriceInfo.TotalShares = data.GetProperty("TotalShares").GetDecimal();
            stockPriceInfo.MarketCapitalization = data.GetProperty("mc").GetDecimal() / 100000000; // 转换为亿
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
            stockPriceInfo.CirculationMarketCap = data.GetProperty("cmc").GetDecimal() / 100000000; // 转换为亿
            stockPriceInfo.NonRestrictedShares = data.GetProperty("NonRestrictedShares").GetDecimal();
            stockPriceInfo.NetAssetPerShare = data.GetProperty("NetAssetPS").GetDecimal();
            stockPriceInfo.AveragePrice = data.GetProperty("av_px").GetDecimal();
            stockPriceInfo.VolumeRatio = data.GetProperty("qrr").GetDecimal();
            stockPriceInfo.EntrustRatio = data.GetProperty("entrust_rate").GetDecimal();

            return stockPriceInfo;
        }
        catch (Exception ex)
        {
            throw new Exception($"处理股票价格数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_stock_company_info"), Description("根据股票代码获取上市公司基本面")]
    public async Task<StockCompanyInfo> GetStockCompanyInfoAsync(string stockSymbol)
    {
        try
        {
            // 只保留 stockSymbol 中的数字部分
            stockSymbol = new string(stockSymbol.Where(char.IsDigit).ToArray());

            var url = $"https://api.zhituapi.com/hs/gs/gsjj/{stockSymbol}?token={_zhiTuToken}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var info = JsonSerializer.Deserialize<StockCompanyInfo>(response);

            return info ?? throw new Exception("GetStockCompanyInfoAsync返回数据为空");
        }
        catch (Exception ex)
        {
            throw new Exception($"处理公司基本面数据时发生错误: {ex.Message}", ex);
        }
    }
}
