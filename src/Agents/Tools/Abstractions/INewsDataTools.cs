using MarketAssistant.Agents.Tools.Models;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 新闻数据工具接口
/// </summary>
public interface INewsDataTools
{
    /// <summary>
    /// 获取资产相关的新闻（对于A股从财联社等获取，对于虚拟币从Twitter/X获取）
    /// </summary>
    Task<List<NewsItem>> GetNewsAsync(string assetSymbol, int count = 10);

    /// <summary>
    /// 获取AI工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}






