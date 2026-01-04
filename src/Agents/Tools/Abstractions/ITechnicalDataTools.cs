using MarketAssistant.Agents.Plugins.Models;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 技术分析数据工具接口
/// </summary>
public interface ITechnicalDataTools
{
    /// <summary>
    /// 获取KDJ技术指标
    /// </summary>
    Task<TechnicalKDJ> GetKDJAsync(string assetSymbol);

    /// <summary>
    /// 获取MACD技术指标
    /// </summary>
    Task<TechnicalMACD> GetMACDAsync(string assetSymbol);

    /// <summary>
    /// 获取BOLL技术指标
    /// </summary>
    Task<TechnicalBoll> GetBOLLAsync(string assetSymbol);

    /// <summary>
    /// 获取MA技术指标
    /// </summary>
    Task<TechnicalMA> GetMAAsync(string assetSymbol);

    /// <summary>
    /// 获取AI工具函数列表
    /// </summary>
    IEnumerable<AIFunction> GetFunctions();
}





