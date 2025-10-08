using MarketAssistant.Avalonia.Views.Models;

namespace MarketAssistant.Services.Cache;

/// <summary>
/// 分析结果缓存服务接口
/// </summary>
public interface IAnalysisCacheService : IDisposable
{
    /// <summary>
    /// 获取缓存的分析结果
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <returns>缓存的分析结果，如果没有缓存则返回null</returns>
    Task<AnalystResult?> GetCachedAnalysisAsync(string stockSymbol);

    /// <summary>
    /// 缓存分析结果
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <param name="analysisResult">分析结果</param>
    Task CacheAnalysisAsync(string stockSymbol, AnalystResult analysisResult);

    /// <summary>
    /// 清除指定股票的缓存
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    Task ClearCacheAsync(string stockSymbol);
}

