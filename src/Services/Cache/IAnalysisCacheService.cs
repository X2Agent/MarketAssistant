using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Services.Cache;

/// <summary>
/// 分析结果缓存服务接口（彻底重构版）
/// 缓存完整的 MarketAnalysisReport 而不是单个 AnalystResult
/// </summary>
public interface IAnalysisCacheService : IDisposable
{
    /// <summary>
    /// 获取缓存的市场分析报告
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <returns>缓存的分析报告，如果没有缓存则返回 null</returns>
    Task<MarketAnalysisReport?> GetCachedAnalysisAsync(string stockSymbol);

    /// <summary>
    /// 缓存市场分析报告
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <param name="report">分析报告</param>
    Task CacheAnalysisAsync(string stockSymbol, MarketAnalysisReport report);

    /// <summary>
    /// 清除指定股票的缓存
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    Task ClearCacheAsync(string stockSymbol);
}
