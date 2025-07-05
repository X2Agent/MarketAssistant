using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins.Models;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace MarketAssistant.Plugins;

public class StockFinancialPlugin
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _zhiTuToken;

    public StockFinancialPlugin(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _zhiTuToken = userSettingService.CurrentSetting.ZhiTuApiToken;
    }

    [KernelFunction("get_fund_flow"), Description("获取股票资金流数据")]
    public async Task<FundFlow> GetFundFlowAsync(string stockSymbol)
    {
        try
        {
            var url = $"https://x-quote.cls.cn/quote/stock/fundflow?secu_code={stockSymbol}&app=CailianpressWeb&os=web&sv=8.4.6";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var jsonDocument = JsonDocument.Parse(response);

            var fundFlow = new FundFlow();

            var data = jsonDocument.RootElement.GetProperty("data");

            fundFlow.MainFundIn = data.GetProperty("main_fund_in").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFundOut = data.GetProperty("main_fund_out").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFundDiff = data.GetProperty("main_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.SuperFundDiff = data.GetProperty("super_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.LargeFundDiff = data.GetProperty("large_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.MediumFundDiff = data.GetProperty("medium_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.LittleFundDiff = data.GetProperty("little_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFund3 = data.GetProperty("main_fund_3").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFund5 = data.GetProperty("main_fund_5").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFund10 = data.GetProperty("main_fund_10").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFund20 = data.GetProperty("main_fund_20").GetInt64() / 10000f; // 转换为万元
            fundFlow.Date = data.GetProperty("date").GetInt32(); // 解析日期

            return fundFlow;
        }
        catch (Exception ex)
        {
            throw new Exception($"处理资金流数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_financial_data"), Description("获取上市公司近四个季度的主要财务指标")]
    public async Task<List<StockFinancialData>> GetFinancialDataAsync(string stockSymbol)
    {
        try
        {
            // 只保留 stockSymbol 中的数字部分
            string stockCode = new string(stockSymbol.Where(char.IsDigit).ToArray());

            // 获取最新财报数据
            var url = $"https://api.zhituapi.com/hs/gs/cwzb/{stockCode}?token={_zhiTuToken}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var financialDatas = JsonSerializer.Deserialize<List<StockFinancialData>>(response);

            return financialDatas ?? new();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取上市公司近四个季度的主要财务指标时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_quarterly_profit"), Description("获取上市公司近一年各季度的利润数据")]
    public async Task<List<QuarterlyProfit>> GetQuarterlyProfitAsync(string stockSymbol)
    {
        try
        {
            // 只保留 stockSymbol 中的数字部分
            string stockCode = new string(stockSymbol.Where(char.IsDigit).ToArray());

            // 获取季度利润数据
            var url = $"https://api.zhituapi.com/hs/gs/jdlr/{stockCode}?token={_zhiTuToken}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var quarterlyProfits = JsonSerializer.Deserialize<List<QuarterlyProfit>>(response);

            return quarterlyProfits ?? new List<QuarterlyProfit>();
        }
        catch (Exception ex)
        {
            throw new Exception($"处理季度利润数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_quarterly_cash_flow"), Description("获取上市公司近一年各季度的现金流数据")]
    public async Task<List<QuarterlyCashFlow>> GetQuarterlyCashFlowAsync(string stockSymbol)
    {
        try
        {
            // 只保留 stockSymbol 中的数字部分
            string stockCode = new string(stockSymbol.Where(char.IsDigit).ToArray());

            // 获取季度现金流数据
            var url = $"https://api.zhituapi.com/hs/gs/jdxj/{stockCode}?token={_zhiTuToken}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var quarterlyCashFlows = JsonSerializer.Deserialize<List<QuarterlyCashFlow>>(response);

            return quarterlyCashFlows ?? new List<QuarterlyCashFlow>();
        }
        catch (Exception ex)
        {
            throw new Exception($"处理季度现金流数据时发生错误: {ex.Message}", ex);
        }
    }
}
