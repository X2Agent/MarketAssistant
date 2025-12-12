using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools;

public class MarketSentimentTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSettingService _userSettingService;

    public MarketSentimentTools(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
    }

    [Description("获取上市公司十大股东信息，默认返回最近1年的数据")]
    public async Task<List<TopShareholder>> GetTopShareholdersAsync([Description("股票代码")] string stockSymbol)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(stockSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            // 十大股东数据量大（每期10个股东），只查询最近1年的数据
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-1).ToString("yyyyMMdd");

            var url = $"https://api.zhituapi.com/hs/fin/topholder/{stockCode}?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var topShareholders = JsonSerializer.Deserialize<List<TopShareholder>>(response);

            return topShareholders ?? new List<TopShareholder>();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取十大股东信息时发生错误: {ex.Message}", ex);
        }
    }

    [Description("获取上市公司十大流通股东信息，默认返回最近1年的数据")]
    public async Task<List<TopShareholder>> GetTopCirculatingShareholdersAsync([Description("股票代码")] string stockSymbol)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(stockSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            // 十大流通股东数据量大（每期10个股东），只查询最近1年的数据
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-1).ToString("yyyyMMdd");

            var url = $"https://api.zhituapi.com/hs/fin/flowholder/{stockCode}?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var topCirculatingShareholders = JsonSerializer.Deserialize<List<TopShareholder>>(response);

            return topCirculatingShareholders ?? new List<TopShareholder>();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取十大流通股东信息时发生错误: {ex.Message}", ex);
        }
    }

    [Description("获取上市公司股东数变化，可用于分析筹码集中度和市场情绪，默认返回最近2年的数据")]
    public async Task<List<ShareholderCount>> GetShareholderCountAsync([Description("股票代码")] string stockSymbol)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(stockSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            // 查询最近2年的股东数变化，用于分析趋势
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-2).ToString("yyyyMMdd");

            var url = $"https://api.zhituapi.com/hs/fin/hm/{stockCode}?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var shareholderCounts = JsonSerializer.Deserialize<List<ShareholderCount>>(response);

            return shareholderCounts ?? new List<ShareholderCount>();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取公司股东数时发生错误: {ex.Message}", ex);
        }
    }

    [Description("获取股票资金流数据")]
    public async Task<FundFlow> GetFundFlowAsync([Description("股票代码")] string stockSymbol)
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

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetTopShareholdersAsync);
        yield return AIFunctionFactory.Create(GetTopCirculatingShareholdersAsync);
        yield return AIFunctionFactory.Create(GetShareholderCountAsync);
        yield return AIFunctionFactory.Create(GetFundFlowAsync);
    }
}

