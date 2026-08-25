using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.AShare;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股财务数据工具实现
/// </summary>
public sealed class AShareFinancialTools : IShareFinancialTools
{
    private readonly ZhiTuMarketClient _zhiTuClient;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<AShareFinancialTools> _logger;

    public AShareFinancialTools(
        ZhiTuMarketClient zhiTuClient,
        IUserSettingService userSettingService,
        ILogger<AShareFinancialTools> logger)
    {
        _zhiTuClient = zhiTuClient ?? throw new ArgumentNullException(nameof(zhiTuClient));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _logger = logger;
    }

    /// <summary>
    /// 通用财务数据获取方法（HTTP 与容错反序列化由 ZhiTuMarketClient 负责）
    /// </summary>
    private async Task<List<T>> GetFinancialDataAsync<T>(string endpoint, string assetSymbol, int years = 2, CancellationToken cancellationToken = default)
    {
        try
        {
            var stockCode = StockSymbolConverter.ToZhiTuFormat(assetSymbol);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
            var endDate = DateTime.Now.ToString("yyyyMMdd");
            var startDate = DateTime.Now.AddYears(-years).ToString("yyyyMMdd");

            return await _zhiTuClient.GetFinancialListAsync<T>(
                endpoint, stockCode, token, startDate, endDate, cancellationToken);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取财务数据失败: {Endpoint} {Symbol}", endpoint, assetSymbol);
            throw new FriendlyException($"获取财务数据({endpoint})时发生错误: {ex.Message}", ex);
        }
    }

    [Description("获取上市公司资产负债表，默认返回最近2年的数据")]
    public Task<List<BalanceSheet>> GetBalanceSheetAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetFinancialDataAsync<BalanceSheet>("balance", assetSymbol, cancellationToken: cancellationToken);

    [Description("获取上市公司利润表，默认返回最近2年的数据")]
    public Task<List<IncomeStatement>> GetIncomeStatementAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetFinancialDataAsync<IncomeStatement>("income", assetSymbol, cancellationToken: cancellationToken);

    [Description("获取上市公司现金流量表，默认返回最近2年的数据")]
    public Task<List<CashFlowStatement>> GetCashFlowStatementAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetFinancialDataAsync<CashFlowStatement>("cashflow", assetSymbol, cancellationToken: cancellationToken);

    [Description("获取上市公司财务主要指标，默认返回最近2年的数据")]
    public Task<List<FinancialRatios>> GetFinancialRatiosAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetFinancialDataAsync<FinancialRatios>("ratios", assetSymbol, cancellationToken: cancellationToken);

    [Description("获取上市公司股本结构，默认返回最近3年的变动记录")]
    public Task<List<CapitalStructure>> GetCapitalStructureAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
        => GetFinancialDataAsync<CapitalStructure>("capital", assetSymbol, years: 3, cancellationToken: cancellationToken);

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetBalanceSheetAsync);
        yield return AIFunctionFactory.Create(GetIncomeStatementAsync);
        yield return AIFunctionFactory.Create(GetCashFlowStatementAsync);
        yield return AIFunctionFactory.Create(GetFinancialRatiosAsync);
        yield return AIFunctionFactory.Create(GetCapitalStructureAsync);
    }
}
