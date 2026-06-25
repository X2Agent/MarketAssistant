using MarketAssistant.Agents.Tools.Models.AShare;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 财务数据工具接口（股票市场专用）
/// </summary>
public interface IShareFinancialTools : IFinancialTools
{
    /// <summary>
    /// 获取资产负债表
    /// </summary>
    Task<List<BalanceSheet>> GetBalanceSheetAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取利润表
    /// </summary>
    Task<List<IncomeStatement>> GetIncomeStatementAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取现金流量表
    /// </summary>
    Task<List<CashFlowStatement>> GetCashFlowStatementAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取财务主要指标
    /// </summary>
    Task<List<FinancialRatios>> GetFinancialRatiosAsync(string assetSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取股本结构
    /// </summary>
    Task<List<CapitalStructure>> GetCapitalStructureAsync(string assetSymbol, CancellationToken cancellationToken = default);
}





