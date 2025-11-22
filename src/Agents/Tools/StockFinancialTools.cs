using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools;

public class StockFinancialTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSettingService _userSettingService;

    public StockFinancialTools(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
    }

    [Description("获取上市公司资产负债表，默认返回最近2年的数据")]
    public async Task<List<BalanceSheet>> GetBalanceSheetAsync([Description("股票代码")] string stockSymbol)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(stockSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            // 默认查询最近2年的数据（约8个季度）
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-2).ToString("yyyyMMdd");

            var url = $"https://api.zhituapi.com/hs/fin/balance/{stockCode}?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var balanceSheets = JsonSerializer.Deserialize<List<BalanceSheet>>(response);

            return balanceSheets ?? new List<BalanceSheet>();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取资产负债表时发生错误: {ex.Message}", ex);
        }
    }

    [Description("获取上市公司利润表，默认返回最近2年的数据")]
    public async Task<List<IncomeStatement>> GetIncomeStatementAsync([Description("股票代码")] string stockSymbol)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(stockSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            // 默认查询最近2年的数据（约8个季度）
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-2).ToString("yyyyMMdd");

            var url = $"https://api.zhituapi.com/hs/fin/income/{stockCode}?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var incomeStatements = JsonSerializer.Deserialize<List<IncomeStatement>>(response);

            return incomeStatements ?? new List<IncomeStatement>();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取利润表时发生错误: {ex.Message}", ex);
        }
    }

    [Description("获取上市公司现金流量表，默认返回最近2年的数据")]
    public async Task<List<CashFlowStatement>> GetCashFlowStatementAsync([Description("股票代码")] string stockSymbol)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(stockSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            // 默认查询最近2年的数据（约8个季度）
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-2).ToString("yyyyMMdd");

            var url = $"https://api.zhituapi.com/hs/fin/cashflow/{stockCode}?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var cashFlowStatements = JsonSerializer.Deserialize<List<CashFlowStatement>>(response);

            return cashFlowStatements ?? new List<CashFlowStatement>();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取现金流量表时发生错误: {ex.Message}", ex);
        }
    }

    [Description("获取上市公司财务主要指标，默认返回最近2年的数据")]
    public async Task<List<FinancialRatios>> GetFinancialRatiosAsync([Description("股票代码")] string stockSymbol)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(stockSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            // 默认查询最近2年的数据（约8个季度）
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-2).ToString("yyyyMMdd");

            var url = $"https://api.zhituapi.com/hs/fin/ratios/{stockCode}?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var financialRatios = JsonSerializer.Deserialize<List<FinancialRatios>>(response);

            return financialRatios ?? new List<FinancialRatios>();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取财务主要指标时发生错误: {ex.Message}", ex);
        }
    }

    [Description("获取上市公司股本结构，默认返回最近3年的变动记录")]
    public async Task<List<CapitalStructure>> GetCapitalStructureAsync([Description("股票代码")] string stockSymbol)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(stockSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            // 股本变动相对较少，查询最近3年的变动记录
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-3).ToString("yyyyMMdd");

            var url = $"https://api.zhituapi.com/hs/fin/capital/{stockCode}?token={token}&st={startDate}&et={endDate}";

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync(url);
            var capitalStructures = JsonSerializer.Deserialize<List<CapitalStructure>>(response);

            return capitalStructures ?? new List<CapitalStructure>();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取公司股本结构时发生错误: {ex.Message}", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetBalanceSheetAsync);
        yield return AIFunctionFactory.Create(GetIncomeStatementAsync);
        yield return AIFunctionFactory.Create(GetCashFlowStatementAsync);
        yield return AIFunctionFactory.Create(GetFinancialRatiosAsync);
        yield return AIFunctionFactory.Create(GetCapitalStructureAsync);
    }
}
