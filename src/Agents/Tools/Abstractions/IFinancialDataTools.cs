using MarketAssistant.Agents.Plugins.Models;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 财务数据工具接口
/// </summary>
public interface IFinancialDataTools
{
    /// <summary>
    /// 获取资产负债表
    /// </summary>
    Task<List<BalanceSheet>> GetBalanceSheetAsync(string assetSymbol);

    /// <summary>
    /// 获取利润表
    /// </summary>
    Task<List<IncomeStatement>> GetIncomeStatementAsync(string assetSymbol);

    /// <summary>
    /// 获取现金流量表
    /// </summary>
    Task<List<CashFlowStatement>> GetCashFlowStatementAsync(string assetSymbol);

    /// <summary>
    /// 获取财务主要指标
    /// </summary>
    Task<List<FinancialRatios>> GetFinancialRatiosAsync(string assetSymbol);

    /// <summary>
    /// 获取股本结构
    /// </summary>
    Task<List<CapitalStructure>> GetCapitalStructureAsync(string assetSymbol);

    /// <summary>
    /// 获取AI工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}





