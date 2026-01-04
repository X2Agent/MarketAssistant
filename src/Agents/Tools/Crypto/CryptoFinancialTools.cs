using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Agents.Tools.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币财务数据工具实现
/// </summary>
public sealed class CryptoFinancialTools : IFinancialDataTools
{
    private readonly ILogger<CryptoFinancialTools> _logger;

    public CryptoFinancialTools(ILogger<CryptoFinancialTools> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取资产负债表（虚拟币不适用）
    /// </summary>
    public Task<List<BalanceSheet>> GetBalanceSheetAsync(string assetSymbol)
    {
        // 【不适用】虚拟币无传统财务报表概念
        // 
        // 💡 替代方案：提供链上数据（需区块链浏览器 API）
        // 
        // 如需提供类似数据，可考虑：
        // 1. 代币供应量数据（Total Supply, Circulating Supply）
        //    - CoinGecko API: GET /api/v3/coins/{id}
        //    - 返回：total_supply, circulating_supply, max_supply
        // 
        // 2. 链上持币地址分布（Rich List）
        //    - Etherscan API (ETH/ERC20): GET /api?module=account&action=tokentx
        //    - BscScan API (BSC): 类似接口
        //    - 需要各链的 API Key
        // 
        // 3. 智能合约资金锁定量（TVL - Total Value Locked）
        //    - DeFi Llama API: GET /tvl/{protocol}
        //    - 适用于 DeFi 项目
        
        _logger.LogWarning("虚拟币无资产负债表概念，此接口不适用");
        throw new NotImplementedException(
            "虚拟币无传统资产负债表概念。\n" +
            "替代方案：使用 CoinGecko API 获取代币供应量数据，或使用区块链浏览器 API 获取链上资金分布。"
        );
    }

    /// <summary>
    /// 获取利润表（虚拟币不适用）
    /// </summary>
    public Task<List<IncomeStatement>> GetIncomeStatementAsync(string assetSymbol)
    {
        // 【不适用】虚拟币无利润表概念
        // 
        // 💡 替代方案：协议收入数据（仅适用于 DeFi 协议）
        // 
        // - Token Terminal API: 提供协议收入、费用、P/S 比率等
        //   https://tokenterminal.com/terminal/api
        // - 示例数据：
        //   * Revenue（协议收入）
        //   * Protocol Earnings（协议净收入）
        //   * P/S Ratio（市销率）
        //   * P/E Ratio（市盈率，极少协议有）
        
        _logger.LogWarning("虚拟币无利润表概念，此接口不适用");
        throw new NotImplementedException(
            "虚拟币无传统利润表概念。\n" +
            "替代方案：对于 DeFi 协议，可使用 Token Terminal API 获取协议收入和费用数据。"
        );
    }

    /// <summary>
    /// 获取现金流量表（虚拟币不适用）
    /// </summary>
    public Task<List<CashFlowStatement>> GetCashFlowStatementAsync(string assetSymbol)
    {
        // 【不适用】虚拟币无现金流量表概念
        // 
        // 💡 替代方案：链上资金流动分析
        // 
        // - Glassnode API: 提供链上资金流动指标
        //   https://docs.glassnode.com/
        // - 可提供数据：
        //   * Exchange Netflow（交易所净流入/流出）
        //   * Whale Transactions（大额交易）
        //   * Active Addresses（活跃地址数）
        //   * Transaction Volume（交易量）
        
        _logger.LogWarning("虚拟币无现金流量表概念，此接口不适用");
        throw new NotImplementedException(
            "虚拟币无传统现金流量表概念。\n" +
            "替代方案：使用 Glassnode API 获取链上资金流动指标（如交易所净流入、大额交易等）。"
        );
    }

    /// <summary>
    /// 获取财务主要指标（虚拟币不适用）
    /// </summary>
    public Task<List<FinancialRatios>> GetFinancialRatiosAsync(string assetSymbol)
    {
        // 【不适用】虚拟币无传统财务指标
        // 
        // 💡 替代方案：链上指标和估值比率
        // 
        // 可提供的类似指标：
        // 1. 市值/实现市值比（MVRV Ratio）- Glassnode
        // 2. 网络价值/交易量比（NVT Ratio）- CoinMetrics
        // 3. 活跃地址增长率 - Glassnode
        // 4. 持币地址集中度 - Etherscan/BscScan
        // 5. Staking 比率（适用于 PoS 币种）- Staking Rewards API
        
        _logger.LogWarning("虚拟币无传统财务指标概念，此接口不适用");
        throw new NotImplementedException(
            "虚拟币无传统财务指标概念。\n" +
            "替代方案：使用链上分析 API（如 Glassnode, CoinMetrics）获取 MVRV、NVT 等链上估值指标。"
        );
    }

    /// <summary>
    /// 获取股本结构（虚拟币不适用）
    /// </summary>
    public Task<List<CapitalStructure>> GetCapitalStructureAsync(string assetSymbol)
    {
        // 【不适用】虚拟币无股本概念
        // 
        // 💡 替代方案：代币供应量和分配信息
        // 
        // - CoinGecko API: GET /api/v3/coins/{id}
        // - 提供数据：
        //   * Total Supply（总供应量）
        //   * Circulating Supply（流通供应量）
        //   * Max Supply（最大供应量）
        // 
        // - Messari API: 提供更详细的代币分配信息
        //   https://messari.io/api/docs
        // - 可获取：
        //   * Initial Distribution（初始分配）
        //   * Team/Investor Allocation（团队/投资者份额）
        //   * Vesting Schedule（解锁计划）
        
        _logger.LogWarning("虚拟币无股本结构概念，此接口不适用");
        throw new NotImplementedException(
            "虚拟币无传统股本结构概念。\n" +
            "替代方案：\n" +
            "1. 使用 CoinGecko API 获取代币供应量数据（总量、流通量、最大量）\n" +
            "2. 使用 Messari API 获取详细的代币分配和解锁计划信息"
        );
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
