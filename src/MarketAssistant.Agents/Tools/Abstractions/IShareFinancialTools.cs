using MarketAssistant.Agents.Tools.Models.AShare;

namespace MarketAssistant.Agents.Tools.Abstractions;

public interface IShareFinancialTools : IFinancialTools
{
    Task<List<BalanceSheet>> GetBalanceSheetAsync(string assetSymbol, CancellationToken cancellationToken = default);

    Task<List<IncomeStatement>> GetIncomeStatementAsync(string assetSymbol, CancellationToken cancellationToken = default);

    Task<List<CashFlowStatement>> GetCashFlowStatementAsync(string assetSymbol, CancellationToken cancellationToken = default);

    Task<List<FinancialRatios>> GetFinancialRatiosAsync(string assetSymbol, CancellationToken cancellationToken = default);

    Task<List<CapitalStructure>> GetCapitalStructureAsync(string assetSymbol, CancellationToken cancellationToken = default);
}





