using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// 缓存键定义中心：所有 IMemoryCache 键在此统一管理，避免硬编码字符串散落在各处。
/// 市场相关键通过 <see cref="MarketType"/> 枚举名动态拼接前缀，新增市场无需修改本类。
/// 与市场无关的全局键以常量形式提供。
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// 资产信息缓存键的业务前缀（不含市场部分）。
    /// </summary>
    private const string AssetInfoPrefix = "AssetInfo";

    /// <summary>
    /// 分析报告缓存键的业务前缀（不含市场部分）。
    /// </summary>
    private const string AnalysisReportPrefix = "MarketAnalysisReport";

    /// <summary>
    /// 虚拟币交易对列表缓存键。
    /// 仅虚拟币使用，与市场无关的全局缓存，无需市场前缀。
    /// </summary>
    public const string CryptoSymbols = "AssetSymbols_Crypto_All";

    /// <summary>
    /// 虚拟币账户余额概览缓存键的业务前缀（不含交易模式部分）。
    /// </summary>
    private const string CryptoAccountSummaryPrefix = "CryptoAccountSummary";

    /// <summary>
    /// GitHub Release 缓存键。
    /// 与市场无关的应用更新检查缓存，无需市场前缀。
    /// </summary>
    public const string GitHubReleases = "GitHubReleases_All";

    /// <summary>
    /// 根据市场类型获取资产信息缓存键前缀。
    /// 前缀由市场枚举名动态拼接，新增市场自动适配，无需修改本方法。
    /// </summary>
    public static string GetAssetInfoPrefix(MarketType type) => $"{AssetInfoPrefix}_{type}_";

    /// <summary>
    /// 生成分析报告缓存键（含市场类型，避免跨市场碰撞）。
    /// 供 <see cref="Services.Cache.AnalysisCacheService"/> 使用。
    /// </summary>
    public static string GetAnalysisReportKey(MarketType market, string assetSymbol)
        => $"{AnalysisReportPrefix}_{market}_{assetSymbol}";

    /// <summary>
    /// 生成交易模块分析报告缓存键（含市场类型，避免跨市场碰撞）。
    /// 供 <c>Trading.AnalysisReportCache</c> 的 ConcurrentDictionary 使用，
    /// 与 <see cref="GetAnalysisReportKey"/> 分属不同存储介质，键格式独立。
    /// </summary>
    public static string GetTradingAnalysisReportKey(MarketType market, string symbol)
        => $"{market}:{symbol}";

    /// <summary>
    /// 生成虚拟币账户余额概览缓存键（含交易模式，避免切换模式后读到旧环境账户数据）。
    /// 供 <c>Trading.CryptoPortfolioService</c> 使用。
    /// </summary>
    public static string GetCryptoAccountSummaryKey(CryptoTradingMode mode)
        => $"{CryptoAccountSummaryPrefix}_{mode}";
}
