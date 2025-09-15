using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// 分析结果缓存服务接口
/// </summary>
public interface IAnalysisCacheService : IDisposable
{
    /// <summary>
    /// 获取缓存的分析结果
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <returns>缓存的聊天历史，如果没有缓存则返回null</returns>
    Task<ChatHistory?> GetCachedAnalysisAsync(string stockSymbol);

    /// <summary>
    /// 缓存分析结果
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <param name="chatHistory">聊天历史</param>
    Task CacheAnalysisAsync(string stockSymbol, ChatHistory chatHistory);

    /// <summary>
    /// 清除指定股票的缓存
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    Task ClearCacheAsync(string stockSymbol);

}

